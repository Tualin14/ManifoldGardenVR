using System.Globalization;
using UnityEngine;

namespace ManifoldProbe;

internal static class VrStartup
{
    internal static VrSettings Settings { get; private set; } = new();
    internal static bool LaunchedInVr { get; private set; }
    internal static bool Enabled => LaunchedInVr && sessionEnabled;
    static bool sessionEnabled;
    static readonly VrStartupPolicy policy = new();
    static string configPath = "";
    static string state = "";
    static double startedAt;
    static bool attempted;

    internal static bool HasVrArgument(IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
            if (string.Equals(argument, "--vr", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    internal static void Initialize(string? settingsPath = null, IEnumerable<string>? arguments = null)
    {
        LaunchedInVr = HasVrArgument(arguments ?? Environment.GetCommandLineArgs().Skip(1));
        sessionEnabled = LaunchedInVr;
        configPath = settingsPath ??
                     Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "UserData", "MGVR.json");
        attempted = false;
        policy.RetryNow();
        state = "Loaded";
        Settings = VrSettings.Load(configPath, Probe.Write);
        DistortionControl.SetSuppressed(Settings.SuppressDistortion);
        if (!File.Exists(configPath)) Settings.Save(configPath, Probe.Write);
        Probe.Write("VR_LAUNCH_MODE " + (LaunchedInVr ? "VR (--vr)" : "Flat (no --vr)"));
        Status();
    }

    internal static void SetEnabled(bool enabled)
    {
        if (enabled && !LaunchedInVr)
        {
            Probe.Write("VR_START_IGNORED: restart the game with --vr");
            return;
        }

        bool wasEnabled = Enabled;
        // Launch intent is per process. The legacy JSON Enabled field is not a mode switch.
        sessionEnabled = enabled;
        if (enabled)
        {
            if (!wasEnabled || !OpenXRSessionProbe.Active) Retry();
        }
        else
        {
            attempted = false;
            OpenXRSessionProbe.Shutdown();
            SetState("Disabled");
        }
    }

    internal static void Retry()
    {
        if (!Enabled)
        {
            Probe.Write("VR_RETRY_IGNORED: --vr launch and an enabled session are required");
            return;
        }

        if (OpenXRSessionProbe.Active) OpenXRSessionProbe.Shutdown();
        attempted = false;
        policy.RetryNow();
        SetState("WaitingForMenuOrLevel");
    }

    internal static void SetDistortion(bool suppressed)
    {
        Settings.SuppressDistortion = suppressed;
        DistortionControl.SetSuppressed(suppressed);
        Settings.Save(configPath, Probe.Write);
    }

    internal static bool SaveInputSettings()
    {
        QuestInput.Reset();
        return Settings.Save(configPath, Probe.Write);
    }

    internal static void SetScale(string value)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) ||
            !VrSettings.ValidScale(scale))
        {
            Probe.Write("VR_SCALE_REJECTED: expected 0.25 through 1.5");
            return;
        }

        // Apply on the next session, so eye targets cannot resize midway through a frame.
        Settings.RenderScale = scale;
        Settings.Save(configPath, Probe.Write);
        if (Enabled) Retry();
    }

    internal static void Update()
    {
        if (OpenXRSessionProbe.CleanupFailed)
        {
            SetState("CleanupFailed_RestartGame");
            return;
        }

        OpenXRSessionProbe.Update();
        if (!Enabled) return;
        double now = Time.realtimeSinceStartupAsDouble;
        if (attempted)
        {
            if (OpenXRRenderer.Failed ||
                (OpenXRSessionProbe.Active && !OpenXRSessionProbe.Running && now - startedAt >= 20))
            {
                Probe.Write("VR_SESSION_RESTART: renderer failed or session readiness timed out");
                OpenXRSessionProbe.Shutdown();
            }

            if (!OpenXRSessionProbe.Active)
            {
                attempted = false;
                policy.Failed(now);
                SetState($"RetryIn{policy.NextAttempt - now:F0}s");
                return;
            }

            if (OpenXRSessionProbe.Running)
                SetState(OpenXRRenderer.Calibrated ? "Running" : "WaitingForStableTracking");
            return;
        }

        if (now < policy.NextAttempt) return;
        if (!OpenXRSessionProbe.Clean)
        {
            SetState("CleanupFailed_RestartGame");
            return;
        }

        if (VrDisplay.Enabled || HeadTracking.Enabled)
        {
            SetState("WaitingForOpenVRShutdown");
            return;
        }

        if (!Probe.CanStartVr)
        {
            SetState("WaitingForMenuOrLevel");
            return;
        }

        if (!Probe.EnsureVrHooks())
        {
            policy.Failed(now);
            SetState("WaitingForRenderHooks");
            return;
        }

        attempted = true;
        startedAt = now;
        Probe.Write("VR_START_PRESENTATION " + (VrCamera.TitleMenuReady ? "title-menu" : "gameplay"));
        SetState("Starting");
        OpenXRSessionProbe.Start();
    }

    static void SetState(string value)
    {
        if (state == value) return;
        state = value;
        Probe.Write("VR_STATE " + state);
    }

    internal static void Status() =>
        Probe.Write(
            $"VR_STATUS state={state} launch={(LaunchedInVr ? "VR" : "Flat")} enabled={Enabled} suppression={Settings.SuppressDistortion} scale={Settings.RenderScale.ToString(CultureInfo.InvariantCulture)} config={configPath}");
}