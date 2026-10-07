using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

[assembly: MelonInfo(typeof(ManifoldProbe.Probe), "Manifold Garden VR (Experimental)", "0.8.29", "Local VR research")]
[assembly: MelonGame("William Chyr Studio", "Manifold Garden")]

namespace ManifoldProbe;

// Opt-in diagnostics and OpenVR head tracking. Does not write game saves.
public sealed class Probe : MelonMod
{
    internal static readonly string Output = ResolveOutput();

    static string ResolveOutput()
    {
        // Static initialization must not read the workspace marker or create directories in flat mode.
        if (!VrStartup.HasVrArgument(Environment.GetCommandLineArgs().Skip(1))) return "";
        var gameRoot = Path.GetDirectoryName(Environment.ProcessPath)!;
        var marker = Path.Combine(gameRoot, "MGVR.workspace.txt");
        var root = File.Exists(marker) ? File.ReadAllText(marker).Trim() : "";
        var output = Path.IsPathFullyQualified(root)
            ? Path.Combine(root, "Artifacts", "Runtime")
            : Path.Combine(gameRoot, "_vr_analysis");
        Directory.CreateDirectory(output);
        return output;
    }

    private static readonly string Command = Path.Combine(Output, "probe-command.txt");
    static Probe? instance;
    static long renderCalls, playerLateCalls;
    static Camera? playerCamera;
    double nextSnapshot;
    int snapshots;
    bool pendingProbe;
    static readonly HashSet<string> installedHooks = new();
    static Camera? shiftedCamera;
    static Vector3 savedPosition;
    static Quaternion savedRotation;
    static bool trackingFrame;
    static bool trackingCapture;
    static int captureStage;
    static double nextCapture;

    public override void OnInitializeMelon()
    {
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (!VrStartup.HasVrArgument(arguments)) return;

        // Without --vr, leave every hook, menu, configuration and diagnostic inactive.
        instance = this;
        Write("PLUGIN_LOADED version=0.8.29 baseline=0.8.17");
        VrStartup.Initialize(arguments: arguments);
    }

    public override void OnLateInitializeMelon()
    {
        if (!VrStartup.LaunchedInVr) return;
        ProjectionProbe.Install(HarmonyInstance);
        QuestInput.Install(HarmonyInstance);
        VrInteraction.Install(HarmonyInstance);
        VrSettingsMenu.Install(HarmonyInstance);
        VrLevelSelect.Install(HarmonyInstance);
        VrMenuPointer.Install(HarmonyInstance);
        nextSnapshot = Time.realtimeSinceStartupAsDouble + 8;
        Write("LATE_INITIALIZE_COMPLETE");
    }

    internal static bool HasWorldCamera => VrCamera.Current != null;
    internal static bool CanStartVr => VrCamera.ReadyForPresentation;

    internal static bool EnsureVrHooks()
    {
        if (!VrStartup.LaunchedInVr) return false;
        instance!.Patch("RigidbodyController", "MG_LateUpdate", nameof(PlayerLatePostfix), true);
        instance.Patch("MobiusRenderPipe", "Render", nameof(RenderPrefix), false);
        return installedHooks.Contains("RigidbodyController") && installedHooks.Contains("MobiusRenderPipe");
    }

    void Patch(string typeName, string methodName, string callback, bool postfix)
    {
        if (installedHooks.Contains(typeName)) return;
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");
            var type = assembly.GetType("Il2Cpp." + typeName) ?? assembly.GetType(typeName)
                ?? assembly.GetTypes().First(t => t.Name == typeName);
            var method = type.GetMethod(methodName,
                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         ?? throw new MissingMethodException(type.FullName, methodName);
            var patch = new HarmonyMethod(typeof(Probe).GetMethod(callback,
                BindingFlags.Static | BindingFlags.NonPublic)!);
            HarmonyInstance.Patch(method, prefix: postfix ? null : patch,
                postfix: postfix
                    ? patch
                    : new HarmonyMethod(typeof(Probe).GetMethod(nameof(RenderPostfix),
                        BindingFlags.Static | BindingFlags.NonPublic)!),
                finalizer: postfix
                    ? null
                    : new HarmonyMethod(typeof(Probe).GetMethod(nameof(RenderFinalizer),
                        BindingFlags.Static | BindingFlags.NonPublic)!));
            installedHooks.Add(typeName);
            Write($"HOOK_INSTALLED {type.FullName}.{methodName}");
        }
        catch (Exception ex)
        {
            Write($"HOOK_FAILED {typeName}.{methodName}: {ex}");
        }
    }

    static void RenderPrefix(Il2Cpp.MobiusRenderPipe __instance, ScriptableRenderContext __0,
        Il2CppReferenceArray<Camera> __1)
    {
        if (StereoRenderer.Nested || OpenXRRenderer.Nested) return;
        renderCalls++;
        OpenXRRenderer.Render(__instance, __0, __1, VrCamera.Current);
        StereoRenderer.Render(__instance, __0, __1, playerCamera != null ? playerCamera : Camera.main);
        TextureProbe.Begin(playerCamera != null ? playerCamera : Camera.main);
        if (captureStage == 0)
        {
            if (!HeadTracking.Enabled) return;
            var trackedCamera = playerCamera != null ? playerCamera : Camera.main;
            if (trackedCamera == null || !trackedCamera.gameObject.scene.name.StartsWith("World_")) return;
            savedPosition = trackedCamera.transform.position;
            savedRotation = trackedCamera.transform.rotation;
            shiftedCamera = trackedCamera;
            trackingFrame = true;
            try
            {
                if (HeadTracking.TryApply(trackedCamera) && trackingCapture)
                {
                    trackingCapture = false;
                    var file = Path.Combine(Output, "hmd-view-" + Time.frameCount + ".png");
                    ScreenCapture.CaptureScreenshot(file);
                    Write("HMD_CAPTURE " + file);
                }
            }
            catch (Exception ex)
            {
                RestoreCamera();
                HeadTracking.Stop();
                Write("HMD_ERROR " + ex);
            }

            return;
        }

        if (Time.realtimeSinceStartupAsDouble < nextCapture) return;
        var camera = playerCamera != null ? playerCamera : Camera.main;
        if (camera == null)
        {
            captureStage = 0;
            Write("CAPTURE_ABORT_NO_CAMERA");
            return;
        }

        string eye = captureStage == 1 ? "left" : "right";
        float offset = captureStage == 1 ? -0.032f : 0.032f;
        savedPosition = camera.transform.position;
        savedRotation = camera.transform.rotation;
        trackingFrame = false;
        shiftedCamera = camera;
        try
        {
            camera.transform.position = savedPosition + camera.transform.right * offset;
            ScreenCapture.CaptureScreenshot(Path.Combine(Output, "view-" + eye + ".png"));
            Write(
                $"EYE_RENDER_REQUEST eye={eye} frame={Time.frameCount} worldOffset={offset} original={savedPosition} shifted={camera.transform.position} rotation={camera.transform.rotation}");
            captureStage = captureStage == 1 ? 2 : 0;
            nextCapture = Time.realtimeSinceStartupAsDouble + 2;
        }
        catch (Exception ex)
        {
            captureStage = 0;
            RestoreCamera();
            Write("CAPTURE_ERROR " + ex);
        }
    }

    static void RenderPostfix()
    {
        if (StereoRenderer.Nested || OpenXRRenderer.Nested) return;
        RestoreCamera();
        TextureProbe.End();
    }

    static Exception? RenderFinalizer(Exception? __exception)
    {
        if (!StereoRenderer.Nested && !OpenXRRenderer.Nested)
        {
            RestoreCamera();
            TextureProbe.End();
        }

        return __exception;
    }

    static void RestoreCamera()
    {
        if (shiftedCamera == null) return;
        shiftedCamera.transform.position = savedPosition;
        shiftedCamera.transform.rotation = savedRotation;
        if (!trackingFrame)
            Write($"EYE_RENDER_RESTORED frame={Time.frameCount} position={shiftedCamera.transform.position}");
        shiftedCamera = null;
    }

    static void PlayerLatePostfix(Il2Cpp.RigidbodyController __instance)
    {
        playerLateCalls++;
        try
        {
            if (playerCamera == __instance.myCamera) return;
            playerCamera = __instance.myCamera;
            if (playerCamera != null) Write($"PLAYER_CAMERA_FOUND {playerCamera.name}");
        }
        catch (Exception ex)
        {
            if (playerLateCalls == 1) Write("PLAYER_CAMERA_ERROR " + ex.Message);
        }
    }

    public override void OnUpdate()
    {
        if (!VrStartup.LaunchedInVr) return;
        try
        {
            TextureProbe.Update();
            StereoRenderer.Update();
            VrDisplay.Update();
            MovementDiagnostics.Tick();
            if (File.Exists(Command))
            {
                string cmd;
                try
                {
                    cmd = File.ReadAllText(Command).Trim();
                    File.Delete(Command);
                }
                catch (IOException)
                {
                    return;
                } // Retry next frame if a writer still owns the request.

                if (cmd == "probe-camera") pendingProbe = true;
                if (cmd == "snapshot") Snapshot();
                if (cmd == "xr-input-status") QuestInput.Report();
                if (cmd == "xr-settings-status") VrSettingsMenu.Report();
                if (cmd == "xr-settings-open") VrSettingsMenu.Report(true);
                if (cmd == "xr-movement-trace") MovementDiagnostics.Start();
                if (cmd == "xr-movement-stop") MovementDiagnostics.Stop();
                if (cmd == "xr-distortion-off") VrStartup.SetDistortion(true);
                if (cmd == "xr-distortion-on") VrStartup.SetDistortion(false);
                if (cmd == "xr-status")
                {
                    VrStartup.Status();
                    OpenXRRenderer.ReportTracking();
                }

                if (cmd == "xr-retry") VrStartup.Retry();
                if (cmd.StartsWith("xr-scale ")) VrStartup.SetScale(cmd.Substring(9));
                if (cmd == "xr-compare") OpenXRDiagnostics.Request();
                if (cmd == "xr-discover") OpenXRProbe.Discover();
                if (cmd == "xr-initialize") OpenXRSessionProbe.Initialize();
                if (cmd == "xr-start") VrStartup.SetEnabled(true);
                if (cmd == "xr-shutdown") VrStartup.SetEnabled(false);
                if (cmd == "hook-player")
                    Patch("RigidbodyController", "MG_LateUpdate", nameof(PlayerLatePostfix), true);
                if (cmd == "hook-render") Patch("MobiusRenderPipe", "Render", nameof(RenderPrefix), false);
                if (cmd == "capture-stereo")
                {
                    Patch("RigidbodyController", "MG_LateUpdate", nameof(PlayerLatePostfix), true);
                    Patch("MobiusRenderPipe", "Render", nameof(RenderPrefix), false);
                    if (installedHooks.Contains("MobiusRenderPipe"))
                    {
                        captureStage = 1;
                        nextCapture = Time.realtimeSinceStartupAsDouble + 1;
                    }
                }

                if ((cmd == "tracking-on" || cmd == "vr-on") && (VrStartup.Enabled || OpenXRSessionProbe.Active))
                {
                    Write("OPENVR_REFUSED_OPENXR_ACTIVE: send XrShutdown first");
                    return;
                }

                if (cmd == "tracking-on")
                {
                    VrDisplay.Stop();
                    Patch("RigidbodyController", "MG_LateUpdate", nameof(PlayerLatePostfix), true);
                    Patch("MobiusRenderPipe", "Render", nameof(RenderPrefix), false);
                    if (installedHooks.Contains("MobiusRenderPipe")) HeadTracking.Start();
                }

                if (cmd == "tracking-off")
                {
                    VrDisplay.Stop();
                    HeadTracking.Stop();
                }

                if (cmd == "recenter")
                {
                    if (VrStartup.Enabled || OpenXRSessionProbe.Active) OpenXRRenderer.Recenter();
                    else HeadTracking.Recenter();
                }

                if (cmd == "tracking-capture") trackingCapture = true;
                if (cmd == "texture-probe")
                {
                    Patch("RigidbodyController", "MG_LateUpdate", nameof(PlayerLatePostfix), true);
                    Patch("MobiusRenderPipe", "Render", nameof(RenderPrefix), false);
                    if (installedHooks.Contains("MobiusRenderPipe")) TextureProbe.Request();
                }

                if (cmd == "stereo-frame")
                {
                    Patch("RigidbodyController", "MG_LateUpdate", nameof(PlayerLatePostfix), true);
                    Patch("MobiusRenderPipe", "Render", nameof(RenderPrefix), false);
                    if (installedHooks.Contains("MobiusRenderPipe")) StereoRenderer.Request();
                }

                if (cmd == "vr-on")
                {
                    Patch("RigidbodyController", "MG_LateUpdate", nameof(PlayerLatePostfix), true);
                    Patch("MobiusRenderPipe", "Render", nameof(RenderPrefix), false);
                    if (installedHooks.Contains("MobiusRenderPipe"))
                    {
                        VrDisplay.Start();
                        StereoRenderer.Request();
                    }
                }

                if (cmd == "vr-off")
                {
                    VrDisplay.Stop();
                    StereoRenderer.Release();
                }

                if (cmd == "vr-flip") VrDisplay.ToggleFlip();
            }

            VrStartup.Update();
            if (Time.realtimeSinceStartupAsDouble >= nextSnapshot && snapshots < 20)
            {
                nextSnapshot = Time.realtimeSinceStartupAsDouble + 5;
                snapshots++;
                if (snapshots == 1 && VrStartup.LaunchedInVr) OpenXRProbe.Discover();
                Snapshot();
            }
        }
        catch (Exception ex)
        {
            Write("UPDATE_ERROR " + ex);
            nextSnapshot = double.MaxValue;
        }
    }

    public override void OnLateUpdate()
    {
        if (!VrStartup.LaunchedInVr) return;
        VrSettingsMenu.Tick();
        VrLevelSelect.Tick();
        VrMenuPointer.Tick();
        if (!pendingProbe) return;
        pendingProbe = false;
        var camera = playerCamera != null ? playerCamera : Camera.main;
        if (camera == null)
        {
            Write("CAMERA_PROBE_NO_CAMERA");
            return;
        }

        var transform = camera.transform;
        var before = transform.localPosition;
        var offset = new Vector3(0.01f, 0, 0);
        bool setterPassed = false;
        try
        {
            transform.localPosition = before + offset;
            setterPassed = (transform.localPosition - before - offset).sqrMagnitude < 0.00000001f;
        }
        finally
        {
            transform.localPosition = before;
        }

        bool restored = (transform.localPosition - before).sqrMagnitude < 0.00000001f;
        Write(
            $"CAMERA_READ_WRITE_RESTORE camera={camera.name} setter={setterPassed} restored={restored}; same callback, no altered frame rendered");
    }

    static void Snapshot()
    {
        var asset = GraphicsSettings.currentRenderPipeline;
        string pipeline = asset == null ? "built-in/null" : asset.name + " / " + asset.GetIl2CppType().FullName;
        Write(
            $"SNAPSHOT frame={Time.frameCount} renderCalls={renderCalls} playerLateCalls={playerLateCalls} pipeline={pipeline}");
        Write("ENUMERATE_CAMERAS_BEGIN");
        int count = Camera.allCamerasCount;
        Write($"CAMERA_COUNT {count}");
        var cameras = new Il2CppReferenceArray<Camera>(count);
        int found = Camera.GetAllCameras(cameras);
        Write($"CAMERAS_FETCHED {found}");
        for (int i = 0; i < found; i++)
        {
            var camera = cameras[i];
            Write($"CAMERA name={camera.name} enabled={camera.enabled} active={camera.gameObject.activeInHierarchy} " +
                  $"scene={camera.gameObject.scene.name} depth={camera.depth} fov={camera.fieldOfView} " +
                  $"size={camera.pixelWidth}x{camera.pixelHeight} stereo={camera.stereoEnabled} " +
                  $"position={camera.transform.position} target={(camera.targetTexture == null ? "screen" : camera.targetTexture.name)}");
        }

        Write("ENUMERATE_CAMERAS_END");
        var current = VrCamera.Current;
        Write($"VR_VIEW camera={(current == null ? "null" : current.name)} " +
              $"scene={(current == null ? "null" : current.gameObject.scene.name)} " +
              $"enabled={(current != null && current.enabled)} doUpdate={Il2Cpp.GameManager.DoUpdate} " +
              $"title={Il2Cpp.LevelLoader.IsOnTitleScreen}");
        VrMenu.Report();
    }

    public override void OnDeinitializeMelon()
    {
        if (!VrStartup.LaunchedInVr) return;
        MovementDiagnostics.Stop();
        OpenXRSessionProbe.Shutdown();
        VrDisplay.Stop();
        HeadTracking.Shutdown();
    }

    internal static void Write(string line)
    {
        if (instance == null) return;
        instance.LoggerInstance.Msg(line);
        File.AppendAllText(Path.Combine(Output, "runtime-probe.log"),
            DateTime.Now.ToString("O") + " " + line + Environment.NewLine);
    }
}
