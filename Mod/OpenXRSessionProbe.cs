using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SubsystemsImplementation;
using UnityEngine.XR;

namespace ManifoldProbe;

// Unity's native OpenXR provider lifecycle; VrStartup owns intent and retry timing.
internal static class OpenXRSessionProbe
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    delegate bool LoadLibraryFn([MarshalAs(UnmanagedType.LPStr)] string path);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate IntPtr GetProcFn([MarshalAs(UnmanagedType.U1)] bool loaderDefault);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void SetProcFn(IntPtr proc);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    delegate bool BoolFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void VoidFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void SetIntFn(int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void EventFn(int kind, ulong payload);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void SetCallbackFn(EventFn callback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void AppInfoFn([MarshalAs(UnmanagedType.LPStr)] string name,
        [MarshalAs(UnmanagedType.LPStr)] string version, uint hash, [MarshalAs(UnmanagedType.LPStr)] string engine);

    static readonly EventFn Callback = OnNativeEvent;
    static IntPtr module;
    static bool libraryLoaded, sessionInitialized, startRequested, ready, begun, exitRequested;
    static double nextReport;
    internal static bool Active => sessionInitialized;
    internal static bool Running => begun;
    internal static bool Focused => focused;
    internal static bool CleanupFailed => cleanupFailed;

    internal static bool Clean =>
        !libraryLoaded && !sessionInitialized && input == null && display == null && !cleanupFailed;

    static volatile bool focused;
    static bool cleanupFailed;
    internal static XRDisplaySubsystem? Display => begun ? display : null;
    static XRDisplaySubsystem? display;
    static XRInputSubsystem? input;

    static T Export<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));

    internal static void Initialize()
    {
        if (cleanupFailed)
        {
            Probe.Write("OPENXR_REFUSED: cleanup failed; restart game");
            return;
        }

        if (sessionInitialized)
        {
            Probe.Write("OPENXR_SESSION_ALREADY_INITIALIZED");
            return;
        }

        if (VrDisplay.Enabled || HeadTracking.Enabled)
        {
            Probe.Write("OPENXR_SESSION_REFUSED_OPENVR_ACTIVE");
            return;
        }

        try
        {
            OpenXRProbe.Discover();
            var root = Path.GetDirectoryName(Environment.ProcessPath)!;
            var native = Path.Combine(root, "ManifoldGarden_Data", "Plugins", "x86_64");
            if (module == IntPtr.Zero) module = NativeLibrary.Load(Path.Combine(native, "UnityOpenXR.dll"));
            libraryLoaded = Export<LoadLibraryFn>("main_LoadOpenXRLibrary")(Path.Combine(native, "openxr_loader.dll"));
            if (!libraryLoaded) throw new InvalidOperationException("OpenXR loader library rejected");
            var proc = Export<GetProcFn>("NativeConfig_GetProcAddressPtr")(true);
            if (proc == IntPtr.Zero) throw new InvalidOperationException("xrGetInstanceProcAddr missing");
            Export<SetProcFn>("NativeConfig_SetProcAddressPtrAndLoadStage1")(proc);
            sessionInitialized = Export<BoolFn>("session_InitializeSession")();
            if (!sessionInitialized) throw new InvalidOperationException("Native session initialization failed");
            Export<AppInfoFn>("NativeConfig_SetApplicationInfo")("Manifold Garden VR", "0.7.0", 700,
                Application.unityVersion);
            Export<SetCallbackFn>("NativeConfig_SetCallbacks")(Callback);
            Export<SetIntFn>("NativeConfig_SetRenderMode")(0);
            Export<SetIntFn>("NativeConfig_SetDepthSubmissionMode")(0);
            var descriptors = SubsystemDescriptorStore.s_IntegratedDescriptors;
            foreach (int i in Enumerable.Range(0, descriptors.Count)
                         .OrderBy(index => descriptors[index].id == "OpenXR Display" ? 0 : 1))
            {
                var descriptor = descriptors[i];
                if (descriptor.id != "OpenXR Display" && descriptor.id != "OpenXR Input") continue;
                Probe.Write("OPENXR_SUBSYSTEM_CREATE_BEGIN " + descriptor.id);
                var ptr = SubsystemDescriptorBindings.Create(descriptor.m_Ptr);
                if (ptr == IntPtr.Zero)
                    throw new InvalidOperationException("Subsystem creation failed: " + descriptor.id);
                var subsystem = SubsystemManager.GetIntegratedSubsystemByPtr(ptr);
                if (subsystem == null)
                    throw new InvalidOperationException("Managed subsystem missing: " + descriptor.id);
                subsystem.m_SubsystemDescriptor = descriptor.Cast<ISubsystemDescriptor>();
                if (descriptor.id == "OpenXR Display") display = subsystem.Cast<XRDisplaySubsystem>();
                else input = subsystem.Cast<XRInputSubsystem>();
                Probe.Write(
                    $"OPENXR_SUBSYSTEM_CREATED {descriptor.id} native=0x{ptr.ToInt64():X} running={subsystem.running}");
            }

            if (display == null || input == null)
                throw new InvalidOperationException("Both XR subsystems are required");
            Probe.Write("OPENXR_SESSION_INITIALIZED displayCreated=True inputCreated=True renderingStarted=False");
        }
        catch (Exception ex)
        {
            Probe.Write("OPENXR_SESSION_ERROR " + ex);
            Shutdown();
        }
    }

    internal static void Start()
    {
        if (startRequested || begun) return;
        Initialize();
        if (!sessionInitialized) return;
        try
        {
            OpenXRRenderer.Prepare();
            display!.scaleOfAllRenderTargets = VrStartup.Settings.RenderScale;
            startRequested = true;
            if (!Export<BoolFn>("session_CreateSessionIfNeeded")())
                throw new InvalidOperationException("xrCreateSession failed");
            Probe.Write("OPENXR_START_REQUESTED");
            Update();
        }
        catch (Exception ex)
        {
            Probe.Write("OPENXR_START_ERROR " + ex);
            Shutdown();
        }
    }

    internal static void Update()
    {
        if (!sessionInitialized || !startRequested) return;
        try
        {
            Export<VoidFn>("messagepump_PumpMessageLoop")();
            if (exitRequested)
            {
                Shutdown();
                return;
            }

            if (ready && !begun)
            {
                begun = true;
                Export<VoidFn>("session_BeginSession")();
                // Same point in startup as OpenXRLoader 1.3.1: begin, attach actions, start subsystems.
                try
                {
                    OpenXRControllerActions.Register(module);
                }
                catch (Exception ex)
                {
                    Probe.Write("OPENXR_CONTROLLER_ACTIONS_ERROR " + ex);
                }

                display!.Start();
                input!.Start();
                if (!display.running || !input.running)
                    throw new InvalidOperationException("XR subsystem did not start");
                input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                Probe.Write("OPENXR_STARTED display=True input=True");
            }

            if (begun)
            {
                if (!display!.running || !input!.running) throw new InvalidOperationException("XR subsystem stopped");
                OpenXRRenderer.PrepareCameras();
            }

            if (!begun || Time.realtimeSinceStartupAsDouble < nextReport) return;
            nextReport = Time.realtimeSinceStartupAsDouble + 3;
            var position = InputTracking.GetLocalPosition(XRNode.CenterEye);
            var rotation = InputTracking.GetLocalRotation(XRNode.CenterEye);
            int passes = display!.GetRenderPassCount();
            Probe.Write($"OPENXR_RUNNING passes={passes} head={position} rotation={rotation}");
        }
        catch (Exception ex)
        {
            Probe.Write("OPENXR_UPDATE_ERROR " + ex);
            Shutdown();
        }
    }

    static void OnNativeEvent(int kind, ulong payload)
    {
        // Never allow a managed exception to escape through an unmanaged callback.
        try
        {
            if (kind == 11) ready = true;
            // Values from the installed OpenXR 1.3.1 OpenXRFeature.NativeEvent.
            if (kind >= 10 && kind <= 18)
            {
                focused = kind == 14;
            }

            if (kind == 6) OpenXRRenderer.RequestRecenter();
            if (kind >= 15 && kind <= 19) exitRequested = true;
            Probe.Write($"OPENXR_NATIVE_EVENT kind={kind} payload={payload}");
        }
        catch
        {
        }
    }

    internal static void Shutdown()
    {
        QuestInput.Reset();
        OpenXRControllerActions.Reset();
        startRequested = false;
        Cleanup(() =>
        {
            if (input != null && input.running) input.Stop();
        });
        Cleanup(() =>
        {
            if (display != null && display.running) display.Stop();
        });
        if (begun)
        {
            Cleanup(() => Export<VoidFn>("session_RequestExitSession")());
            Cleanup(() => Export<VoidFn>("session_EndSession")());
        }

        begun = false;
        Cleanup(() =>
        {
            if (input != null)
            {
                input.Destroy();
                input = null;
            }
        });
        Cleanup(() =>
        {
            if (display != null)
            {
                display.Destroy();
                display = null;
            }
        });
        Cleanup(() =>
        {
            if (sessionInitialized)
            {
                Export<VoidFn>("session_DestroySession")();
                sessionInitialized = false;
            }
        });
        Cleanup(() =>
        {
            if (libraryLoaded && input == null && display == null && !sessionInitialized)
            {
                Export<VoidFn>("main_UnloadOpenXRLibrary")();
                libraryLoaded = false;
            }
        });
        Cleanup(OpenXRRenderer.Restore);
        focused = ready = exitRequested = false;
        Probe.Write("OPENXR_SESSION_SHUTDOWN clean=" + Clean);
        // Keep Unity's plugin DLL loaded; the engine owns its provider callbacks.
    }

    static void Cleanup(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            cleanupFailed = true;
            Probe.Write("OPENXR_SHUTDOWN_ERROR " + ex);
        }
    }
}