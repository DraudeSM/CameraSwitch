using System.Text.Json;
using System.Text.Json.Serialization;

class MonitorSceneEntry
{
    public string MonitorKey { get; set; } = "";
    public string SceneName { get; set; } = "";
    public string InputName { get; set; } = "";
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
}

class SceneConfig
{
    public List<MonitorSceneEntry> Scenes { get; set; } = new();
    public string DefaultSceneName { get; set; } = "Portatil";
    public string DefaultInputName { get; set; } = "Camara Portatil";
    public string? DefaultDeviceId { get; set; }
    public string? DefaultDeviceName { get; set; }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static SceneConfig? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SceneConfig>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }

    public Dictionary<string, string> ToSceneMap() =>
        Scenes.ToDictionary(s => s.MonitorKey, s => s.SceneName);
}
