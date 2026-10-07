using System.Text.Json;

namespace ManifoldProbe;

internal sealed class VrSettings
{
    internal const int CurrentControlLayoutVersion = 1;

    public int ControlLayoutVersion { get; set; } = CurrentControlLayoutVersion;

    // Retained for compatibility with existing JSON; --vr now selects the launch mode.
    public bool Enabled { get; set; }
    public bool SuppressDistortion { get; set; } = true;
    public float RenderScale { get; set; } = 0.65f;
    public bool ControllersEnabled { get; set; } = true;
    public float ControllerDeadzone { get; set; } = 0.2f;
    public float SnapTurnDegrees { get; set; } = 45;
    public VrTurnMode TurnMode { get; set; } = VrTurnMode.Snap;
    public float SmoothTurnDegreesPerSecond { get; set; } = 90;
    public VrButton InteractBinding { get; set; } = VrButton.RightTrigger;
    public VrButton GravityBinding { get; set; } = VrButton.RightGrip;
    public VrButton RunBinding { get; set; } = VrButton.LeftGrip;
    public VrButton ClockwiseBinding { get; set; } = VrButton.A;
    public VrButton CounterclockwiseBinding { get; set; } = VrButton.B;

    internal bool ValidControllers => float.IsFinite(ControllerDeadzone) && ControllerDeadzone >= 0.1f &&
                                      ControllerDeadzone <= 0.5f && float.IsFinite(SnapTurnDegrees) &&
                                      SnapTurnDegrees >= 15 && SnapTurnDegrees <= 90 &&
                                      Enum.IsDefined(typeof(VrTurnMode), TurnMode) &&
                                      float.IsFinite(SmoothTurnDegreesPerSecond) &&
                                      SmoothTurnDegreesPerSecond >= 30 && SmoothTurnDegreesPerSecond <= 240 &&
                                      VrBindings.Valid(this);

    internal static bool ValidScale(float value) => float.IsFinite(value) && value >= 0.25f && value <= 1.5f;

    internal static VrSettings Load(string path, Action<string> log)
    {
        if (!File.Exists(path)) return new();
        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<VrSettings>(json)
                           ?? throw new JsonException("Empty configuration");
            if (!ValidScale(settings.RenderScale)) throw new JsonException("RenderScale must be between 0.25 and 1.5");
            using var document = JsonDocument.Parse(json);
            int storedLayout = document.RootElement.TryGetProperty(nameof(ControlLayoutVersion), out var version)
                ? version.GetInt32()
                : 0;
            if (storedLayout < CurrentControlLayoutVersion)
            {
                // Adopt the agreed layout once, including previously customized mappings.
                // Save preserves the old JSON as .bak and leaves turning/render settings intact.
                VrBindings.ResetControls(settings);
                settings.ControlLayoutVersion = CurrentControlLayoutVersion;
            }

            if (!settings.ValidControllers)
                throw new JsonException("Invalid VR turn mode, speed, deadzone or button mappings");
            if (storedLayout < CurrentControlLayoutVersion)
            {
                log("VR_CONTROL_LAYOUT_MIGRATED version=" + CurrentControlLayoutVersion);
                settings.Save(path, log);
            }

            return settings;
        }
        catch (Exception ex)
        {
            log("VR_CONFIG_LOAD_ERROR: using defaults; original file preserved. " + ex.Message);
            return new();
        }
    }

    internal bool Save(string path, Action<string> log)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (!ValidScale(RenderScale)) throw new InvalidOperationException("Invalid render scale");
            if (!ValidControllers) throw new InvalidOperationException("Invalid controller configuration");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            // Same-directory rename keeps the live JSON complete without ReplaceFile's ACL merge privileges.
            File.Move(temporary, path, true);
            log("VR_CONFIG_SAVED " + path);
            return true;
        }
        catch (Exception ex)
        {
            log("VR_CONFIG_SAVE_ERROR: current session only. " + ex.Message);
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (Exception ex)
            {
                log("VR_CONFIG_TEMP_CLEANUP_ERROR " + ex.Message);
            }
        }
    }
}