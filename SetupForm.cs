using OBSWebsocketDotNet;

class SetupForm : Form
{
    class RowControls
    {
        public string? MonitorKey; // null = fila por defecto ("Portátil")
        public string? OriginalSceneName; // nombre que tenía esta fila al abrir el asistente (null si es nueva)
        public TextBox SceneNameBox = null!;
        public ComboBox CameraCombo = null!;
        public string? PreviouslyAssignedDeviceId;
        public string? PreviouslyAssignedDeviceName;
    }

    const int LeftMargin = 12;
    const int RowHeight = 34;
    const int LabelWidth = 190;
    const int SceneWidth = 170;
    const int ComboWidth = 270;

    readonly OBSWebsocket obs;
    readonly SceneConfig existingConfig;
    readonly List<RowControls> rows = new();
    List<CameraDevice> availableCameras = new();
    Panel rowsPanel = null!;

    public SceneConfig? ResultConfig { get; private set; }

    public SetupForm(OBSWebsocket obs, SceneConfig? existingConfig)
    {
        this.obs = obs;
        this.existingConfig = existingConfig ?? new SceneConfig();

        Text = "CameraSwitch - Configurar cámaras";
        Width = 700;
        Height = 460;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        BuildLayout();
        RefreshCameraList();
    }

    void BuildLayout()
    {
        var headerY = 12;
        Controls.Add(new Label { Text = "Posición", Location = new Point(LeftMargin, headerY), Size = new Size(LabelWidth, 20), Font = new Font(Font, FontStyle.Bold) });
        Controls.Add(new Label { Text = "Nombre de escena", Location = new Point(LeftMargin + LabelWidth, headerY), Size = new Size(SceneWidth, 20), Font = new Font(Font, FontStyle.Bold) });
        Controls.Add(new Label { Text = "Cámara", Location = new Point(LeftMargin + LabelWidth + SceneWidth + 10, headerY), Size = new Size(ComboWidth, 20), Font = new Font(Font, FontStyle.Bold) });

        rowsPanel = new Panel
        {
            Location = new Point(0, 40),
            Size = new Size(ClientSize.Width, ClientSize.Height - 40 - 60),
            AutoScroll = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        Controls.Add(rowsPanel);

        var screens = Screen.AllScreens.OrderBy(s => s.Bounds.X).ToList();
        int y = 4;
        for (int i = 0; i < screens.Count; i++)
        {
            var key = $"{screens[i].Bounds.X}_{screens[i].Bounds.Y}";
            var existing = existingConfig.Scenes.FirstOrDefault(s => s.MonitorKey == key);
            AddRow(ref y, key, $"Monitor {i + 1} ({key})",
                existing?.SceneName ?? $"Monitor{i + 1}", existing?.SceneName, existing?.DeviceId, existing?.DeviceName);
        }

        // El nombre "Portátil" por defecto solo cuenta como escena ya existente si de verdad
        // se llegó a configurar antes (con cámara asignada); si no, no hay nada que renombrar.
        var defaultOriginalName = existingConfig.DefaultDeviceId != null ? existingConfig.DefaultSceneName : null;
        AddRow(ref y, null, "Portátil / por defecto",
            existingConfig.DefaultSceneName, defaultOriginalName, existingConfig.DefaultDeviceId, existingConfig.DefaultDeviceName);

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Location = new Point(0, ClientSize.Height - 50),
            Size = new Size(ClientSize.Width, 50),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Padding = new Padding(12)
        };
        var saveButton = new Button { Text = "Guardar", AutoSize = true };
        saveButton.Click += (s, e) => Save();
        var cancelButton = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
        var refreshButton = new Button { Text = "Detectar cámaras de nuevo", AutoSize = true };
        refreshButton.Click += (s, e) => RefreshCameraList();
        var identifyButton = new Button { Text = "Identificar monitores", AutoSize = true };
        identifyButton.Click += (s, e) => IdentifyMonitors();
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(refreshButton);
        buttonPanel.Controls.Add(identifyButton);
        Controls.Add(buttonPanel);

        CancelButton = cancelButton;
    }

    void AddRow(ref int y, string? monitorKey, string positionLabel,
        string sceneName, string? originalSceneName, string? deviceId, string? deviceName)
    {
        rowsPanel.Controls.Add(new Label
        {
            Text = positionLabel,
            Location = new Point(LeftMargin, y + 4),
            Size = new Size(LabelWidth, 22)
        });

        var sceneBox = new TextBox { Text = sceneName, Location = new Point(LeftMargin + LabelWidth, y), Width = SceneWidth - 10 };
        rowsPanel.Controls.Add(sceneBox);

        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(LeftMargin + LabelWidth + SceneWidth + 10, y),
            Width = ComboWidth,
            DisplayMember = nameof(CameraDevice.Name)
        };
        rowsPanel.Controls.Add(combo);

        rows.Add(new RowControls
        {
            MonitorKey = monitorKey,
            OriginalSceneName = originalSceneName,
            SceneNameBox = sceneBox,
            CameraCombo = combo,
            PreviouslyAssignedDeviceId = deviceId,
            PreviouslyAssignedDeviceName = deviceName
        });

        y += RowHeight;
    }

    void IdentifyMonitors()
    {
        var screens = Screen.AllScreens.OrderBy(s => s.Bounds.X).ToList();
        var overlays = new List<Form>();

        for (int i = 0; i < screens.Count; i++)
        {
            var overlay = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Bounds = screens[i].Bounds,
                BackColor = Color.Black,
                TopMost = true,
                ShowInTaskbar = false
            };
            overlay.Controls.Add(new Label
            {
                Text = (i + 1).ToString(),
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 180, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter
            });
            overlays.Add(overlay);
            overlay.Show();
        }

        var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            timer.Dispose();
            foreach (var o in overlays)
            {
                o.Close();
                o.Dispose();
            }
        };
        timer.Start();
    }

    void RefreshCameraList()
    {
        try
        {
            availableCameras = ObsSceneProvisioner.ListCameras(obs);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se han podido detectar las cámaras:\n{ex.Message}", "CameraSwitch",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            availableCameras = new List<CameraDevice>();
        }

        foreach (var row in rows)
        {
            var previousSelection = (row.CameraCombo.SelectedItem as CameraDevice)?.Id ?? row.PreviouslyAssignedDeviceId;

            row.CameraCombo.Items.Clear();
            row.CameraCombo.Items.Add(new CameraDevice { Name = "(Ninguna cámara)", Id = "" });
            foreach (var cam in availableCameras)
                row.CameraCombo.Items.Add(cam);

            if (!string.IsNullOrEmpty(previousSelection) && !availableCameras.Any(c => c.Id == previousSelection))
            {
                row.CameraCombo.Items.Add(new CameraDevice
                {
                    Name = $"{row.PreviouslyAssignedDeviceName ?? "Cámara asignada"} (no conectada ahora)",
                    Id = previousSelection
                });
            }

            var match = row.CameraCombo.Items.Cast<CameraDevice>().FirstOrDefault(c => c.Id == previousSelection);
            row.CameraCombo.SelectedItem = match ?? row.CameraCombo.Items[0];
        }
    }

    void Save()
    {
        if (!ValidateSceneNames())
            return;

        var config = new SceneConfig();

        foreach (var row in rows)
        {
            var selected = row.CameraCombo.SelectedItem as CameraDevice;
            var sceneName = row.SceneNameBox.Text.Trim();

            if (selected == null || string.IsNullOrEmpty(selected.Id))
                continue; // "Ninguna cámara": no se toca OBS ni se guarda esta fila

            string inputName;
            try
            {
                inputName = ObsSceneProvisioner.ApplyEntry(obs, sceneName, row.OriginalSceneName, $"Camara {sceneName}", selected.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se ha podido configurar la escena \"{sceneName}\":\n{ex.Message}", "CameraSwitch",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (row.MonitorKey != null)
            {
                config.Scenes.Add(new MonitorSceneEntry
                {
                    MonitorKey = row.MonitorKey,
                    SceneName = sceneName,
                    InputName = inputName,
                    DeviceId = selected.Id,
                    DeviceName = selected.Name
                });
            }
            else
            {
                config.DefaultSceneName = sceneName;
                config.DefaultInputName = inputName;
                config.DefaultDeviceId = selected.Id;
                config.DefaultDeviceName = selected.Name;
            }
        }

        ResultConfig = config;
        DialogResult = DialogResult.OK;
        Close();
    }

    bool ValidateSceneNames()
    {
        var names = rows.Select(r => r.SceneNameBox.Text.Trim()).ToList();
        if (names.Any(string.IsNullOrEmpty))
        {
            MessageBox.Show("El nombre de escena no puede estar vacío.", "CameraSwitch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
        {
            MessageBox.Show("No puede haber dos filas con el mismo nombre de escena.", "CameraSwitch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        return true;
    }
}
