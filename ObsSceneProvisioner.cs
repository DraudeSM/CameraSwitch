using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

class CameraDevice
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
}

static class ObsSceneProvisioner
{
    const string DshowKind = "dshow_input";
    const string ProbeSceneName = "CameraSwitch_Probe";
    const string ProbeInputName = "CameraSwitch_ProbeInput";

    // GetInputPropertiesListPropertyItems (el método tipado de la librería) lanza
    // InvalidCastException al parsear la respuesta de "video_device_id" contra OBS 5.7.
    // Se pide en bruto con SendRequest y se parsea el JSON manualmente para evitarlo.
    public static List<CameraDevice> ListCameras(OBSWebsocket obs)
    {
        var existing = obs.GetInputList(DshowKind);
        string probeInput;
        bool createdProbeInput = false;
        bool createdProbeScene = false;

        if (existing.Count > 0)
        {
            probeInput = existing[0].InputName;
        }
        else
        {
            var scenes = obs.GetSceneList().Scenes.Select(s => s.Name).ToList();
            if (scenes.Contains(ProbeSceneName))
                obs.RemoveScene(ProbeSceneName); // restos de una ejecución anterior interrumpida

            obs.CreateScene(ProbeSceneName);
            createdProbeScene = true;

            var defaults = obs.GetInputDefaultSettings(DshowKind);
            obs.CreateInput(ProbeSceneName, ProbeInputName, DshowKind, defaults, false);
            probeInput = ProbeInputName;
            createdProbeInput = true;
        }

        try
        {
            var request = new JObject { ["inputName"] = probeInput, ["propertyName"] = "video_device_id" };
            var response = obs.SendRequest("GetInputPropertiesListPropertyItems", request);
            var items = response["propertyItems"] as JArray ?? new JArray();

            return items
                .Select(i => new CameraDevice
                {
                    Name = i["itemName"]?.ToString() ?? "",
                    Id = i["itemValue"]?.ToString() ?? ""
                })
                .Where(d => !string.IsNullOrEmpty(d.Id) && d.Name != "OBS Virtual Camera")
                .ToList();
        }
        finally
        {
            // Solo se borra lo que se ha creado aquí; si se reutilizó un input real del usuario, no se toca.
            if (createdProbeScene)
                obs.RemoveScene(ProbeSceneName); // se lleva también el input de sondeo
            else if (createdProbeInput)
                obs.RemoveInput(ProbeInputName);
        }
    }

    // Crea la escena/fuente si no existen, o actualiza el dispositivo si ya existían.
    // Si la escena ya tenía una fuente de cámara (dshow_input) con OTRO nombre —p.ej. creada a
    // mano por el usuario—, se reutiliza esa en vez de crear una nueva; nunca duplica fuentes.
    // Si originalSceneName difiere de sceneName (el usuario renombró la fila en el asistente),
    // se renombra la escena real en OBS en vez de crear una escena nueva y dejar huérfana la vieja.
    // Devuelve el nombre real del input usado (puede no coincidir con preferredInputName).
    public static string ApplyEntry(OBSWebsocket obs, string sceneName, string? originalSceneName, string preferredInputName, string deviceId)
    {
        var scenes = obs.GetSceneList().Scenes.Select(s => s.Name).ToList();

        Program.Log($"ApplyEntry: sceneName='{sceneName}' originalSceneName='{originalSceneName ?? "(null)"}' " +
            $"scenes=[{string.Join(", ", scenes)}] containsOriginal={originalSceneName != null && scenes.Contains(originalSceneName)} containsTarget={scenes.Contains(sceneName)}");

        if (originalSceneName != null && originalSceneName != sceneName
            && scenes.Contains(originalSceneName) && !scenes.Contains(sceneName))
        {
            Program.Log($"ApplyEntry: renombrando escena '{originalSceneName}' -> '{sceneName}'");
            obs.SetSceneName(originalSceneName, sceneName);
        }
        else if (!scenes.Contains(sceneName))
        {
            Program.Log($"ApplyEntry: creando escena nueva '{sceneName}'");
            obs.CreateScene(sceneName);
        }
        else
        {
            Program.Log($"ApplyEntry: reutilizando escena existente '{sceneName}' tal cual");
        }

        var settings = new JObject { ["video_device_id"] = deviceId };
        var dshowInputNames = obs.GetInputList(DshowKind).Select(i => i.InputName).ToHashSet();
        var sceneItemNames = obs.GetSceneItemList(sceneName).Select(si => si.SourceName).ToList();
        var existingCameraInScene = sceneItemNames.FirstOrDefault(n => dshowInputNames.Contains(n));

        if (existingCameraInScene != null)
        {
            obs.SetInputSettings(existingCameraInScene, settings, true);
            return existingCameraInScene;
        }

        if (dshowInputNames.Contains(preferredInputName))
        {
            // Un input global con ese nombre existe en otra escena; se reutiliza también aquí.
            obs.SetInputSettings(preferredInputName, settings, true);
            obs.CreateSceneItem(sceneName, preferredInputName, true);
            return preferredInputName;
        }

        obs.CreateInput(sceneName, preferredInputName, DshowKind, settings, true);
        return preferredInputName;
    }
}
