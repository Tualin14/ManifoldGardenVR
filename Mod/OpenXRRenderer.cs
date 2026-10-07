using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace ManifoldProbe;

// Mobius is a custom mono SRP. Render it once for each SDK-provided eye target.
internal static class OpenXRRenderer
{
    static readonly Dictionary<int, (Camera camera, StereoTargetEyeMask eye)> camerasSaved = new();

    // 2020.3 native binding; the game's IL2CPP build stripped this managed type.
    // Only the fixed view/projection prefix is consumed. Tail includes viewport,
    // optional occlusion mesh and version-dependent previous-view fields.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    struct RenderParameter
    {
        [FieldOffset(0)] public Matrix4x4 view;
        [FieldOffset(64)] public Matrix4x4 projection;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void GetParameterFn(ref XRDisplaySubsystem.XRRenderPass pass, IntPtr camera, int index,
        out RenderParameter parameter);

    static GetParameterFn? getParameter;
    static double nextReport;
    static bool failed, trackingSuspended = true;
    internal static bool Failed => failed;
    internal static bool Calibrated => poseGate.Calibrated && !trackingSuspended;
    static readonly VrStartupPolicy poseGate = new();
    static Vector3 stablePosition;
    static Quaternion stableRotation;
    static bool haveAnchor;
    static volatile bool recenterRequested;
    static Vector3 referencePosition;
    static Quaternion referenceYaw;
    static int currentCameraId;
    static Vector3 lastPosition;
    static Quaternion lastRotation = Quaternion.identity;
    private static bool _titlePresented;

    internal static void RequestRecenter()
    {
        recenterRequested = true;
    }

    internal static void Recenter()
    {
        trackingSuspended = true;
        haveAnchor = false;
        recenterRequested = false;
        poseGate.Recenter();
        VrMenu.ResetAnchor();
        Probe.Write("OPENXR_RECENTER_PENDING: hold a comfortable forward pose until tracking is stable");
    }

    static void SuspendTracking()
    {
        if (!trackingSuspended) Probe.Write("OPENXR_TRACKING_SUSPENDED referencePreserved=" + poseGate.Calibrated);
        trackingSuspended = true;
        haveAnchor = false;
        poseGate.ResetPose();
    }

    public static bool Nested { get; private set; }
    public static int EyeIndex { get; private set; }
    public static Matrix4x4 EyeProjection { get; private set; }

    internal static void Prepare()
    {
        failed = false;
        Recenter();
        PrepareCameras();
        if (!ProjectionProbe.Ready) throw new InvalidOperationException("Eye projection hook unavailable");
        var address =
            IL2CPP.il2cpp_resolve_icall("UnityEngine.XR.XRDisplaySubsystem/XRRenderPass::GetRenderParameter_Injected");
        if (address == IntPtr.Zero) throw new InvalidOperationException("XR GetRenderParameter native binding missing");
        getParameter = Marshal.GetDelegateForFunctionPointer<GetParameterFn>(address);
        Probe.Write($"OPENXR_RENDER_BINDING address=0x{address.ToInt64():X}");
        ProjectionProbe.Request();
    }

    internal static void PrepareCameras()
    {
        var cameras = new Il2CppReferenceArray<Camera>(Camera.allCamerasCount);
        int count = Camera.GetAllCameras(cameras);
        for (int i = 0; i < count; i++) MakeMono(cameras[i]);
    }

    static void MakeMono(Camera camera)
    {
        if (!camerasSaved.ContainsKey(camera.GetInstanceID()))
            camerasSaved.Add(camera.GetInstanceID(), (camera, camera.stereoTargetEye));
        camera.stereoTargetEye = StereoTargetEyeMask.None;
    }

    internal static void Restore()
    {
        foreach (var item in camerasSaved.Values)
            if (item.camera != null)
                item.camera.stereoTargetEye = item.eye;
        camerasSaved.Clear();
        VrMenu.Release();
        VrPointer.Release();
        currentCameraId = 0;
        _titlePresented = false;
        failed = false;
        Recenter();
    }

    static bool TryHeadPose(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        if (!OpenXRSessionProbe.Focused) return false;
        // List<XRNodeState> has no usable AOT instantiation in this game's IL2CPP build.
        // Use non-generic device feature bindings and the already-tested center-eye pose API.
        // Feature names match CommonUsages in the bundled Unity 2020.3 XR module.
        var device = InputTracking.GetDeviceIdAtXRNode(XRNode.Head);
        if (!InputDevices.IsDeviceValid(device) ||
            !InputDevices.TryGetFeatureValue_bool(device, "IsTracked", out var tracked) || !tracked ||
            !InputDevices.TryGetFeatureValue_UInt32(device, "TrackingState", out var trackingState) ||
            (trackingState & 3) != 3) return false; // Position (1) and rotation (2) must both be valid.
        position = InputTracking.GetLocalPosition(XRNode.CenterEye);
        rotation = InputTracking.GetLocalRotation(XRNode.CenterEye);
        float norm = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z +
                     rotation.w * rotation.w;
        return float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z) &&
               float.IsFinite(norm) && norm >= 0.9f && norm <= 1.1f;
    }

    internal static bool TryGameplayPose(Camera camera, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        if (failed || recenterRequested || !Calibrated || !VrCamera.OwnsLook || camera != VrCamera.Current ||
            !VrCamera.ReadyForStartup || !TryHeadPose(out var headPosition, out var headRotation)) return false;
        VrCamera.TrackedPose(camera, headPosition, headRotation, referencePosition, referenceYaw,
            out position, out rotation);
        return true;
    }

    internal static bool TryControllerPose(Camera camera, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        if (failed || recenterRequested || !Calibrated || !VrCamera.OwnsLook || camera != VrCamera.Current ||
            !VrCamera.ReadyForStartup || !TryHeadPose(out _, out _) ||
            !ControllerAim.TryRead(out var handPosition, out var handRotation)) return false;
        VrCamera.TrackedPose(camera, handPosition, handRotation, referencePosition, referenceYaw,
            out position, out rotation);
        return true;
    }

    internal static bool TryMenuControllerPose(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        if (failed || recenterRequested || !Calibrated || !VrStartup.Enabled ||
            !OpenXRSessionProbe.Running || !QuestInput.RayMenuActive ||
            !TryHeadPose(out _, out _) || !ControllerAim.TryRead(out var handPosition, out var handRotation))
            return false;
        var camera = VrCamera.Current ?? VrMenu.FallbackCamera;
        VrCamera.TrackedPose(camera, handPosition, handRotation, referencePosition, referenceYaw,
            out position, out rotation);
        return true;
    }

    internal static void ReportTracking()
    {
        try
        {
            var device = InputTracking.GetDeviceIdAtXRNode(XRNode.Head);
            bool valid = InputDevices.IsDeviceValid(device);
            bool hasTracked = InputDevices.TryGetFeatureValue_bool(device, "IsTracked", out var tracked);
            bool hasState = InputDevices.TryGetFeatureValue_UInt32(device, "TrackingState", out var trackingState);
            Probe.Write(
                $"OPENXR_TRACKING_STATUS device={device} valid={valid} focused={OpenXRSessionProbe.Focused} hasTracked={hasTracked} tracked={tracked} hasState={hasState} trackingState={trackingState} calibrated={Calibrated} referencePreserved={poseGate.Calibrated}");
        }
        catch (Exception ex)
        {
            Probe.Write("OPENXR_TRACKING_STATUS_ERROR " + ex);
        }
    }

    internal static void Render(Il2Cpp.MobiusRenderPipe pipe, ScriptableRenderContext context,
        Il2CppReferenceArray<Camera> cameras, Camera? camera)
    {
        var display = OpenXRSessionProbe.Display;
        if (failed || display == null || !display.running) return;
        bool world = camera != null && camera.gameObject.activeInHierarchy;
        bool title = Il2Cpp.LevelLoader.IsOnTitleScreen;
        if (title) _titlePresented = true;
        int id = camera == null ? 0 : camera.GetInstanceID();
        if (id != currentCameraId)
        {
            currentCameraId = id;
            QuestInput.Reset();
            VrMenu.ResetAnchor();
            Probe.Write(
                $"OPENXR_VIEW_CHANGED id={id} scene={(camera == null ? "none" : camera.gameObject.scene.name)}");
        }

        if (camera == null)
        {
            camera = VrMenu.FallbackCamera;
            // A persistent player may retain sideways gravity when returning
            // to the title. The title panel always uses a neutral upright frame.
            camera.transform.SetPositionAndRotation(title ? Vector3.zero : lastPosition,
                title ? Quaternion.identity : lastRotation);
        }
        else
        {
            lastPosition = camera.transform.position;
            lastRotation = camera.transform.rotation;
        }

        int count = display.GetRenderPassCount();
        if (recenterRequested) Recenter();
        if (world && VrCamera.ReadyForStartup && _titlePresented)
        {
            // Bind the first real player view to a fresh comfortable reference.
            // Keep the XR session; later world-to-world changes retain the
            // existing tracking/reference behavior.
            _titlePresented = false;
            Recenter();
            Probe.Write("OPENXR_TITLE_TO_GAMEPLAY: waiting for stable player calibration");
        }

        if (count == 0)
        {
            SuspendTracking();
            return;
        }

        PrepareCameras();
        MakeMono(camera);
        var position = camera.transform.position;
        var rotation = camera.transform.rotation;
        var vrRotation = VrCamera.Rotation(camera);
        var projection = camera.projectionMatrix;
        var target = camera.targetTexture;
        float aspect = camera.aspect;
        bool report = Time.realtimeSinceStartupAsDouble >= nextReport;
        try
        {
            if (count != 2) throw new InvalidOperationException("Expected two multipass eye targets, got " + count);
            if (!TryHeadPose(out var headPosition, out var headRotation))
            {
                SuspendTracking();
                return;
            }

            if (!Calibrated)
            {
                bool moved = !haveAnchor || Vector3.Distance(headPosition, stablePosition) > 0.08f ||
                             Quaternion.Angle(headRotation, stableRotation) > 8;
                if (moved)
                {
                    stablePosition = headPosition;
                    stableRotation = headRotation;
                    haveAnchor = true;
                }

                if (!poseGate.PoseReady(Time.realtimeSinceStartupAsDouble, true, moved)) return;
                if (!poseGate.Calibrated)
                {
                    referencePosition = headPosition;
                    referenceYaw = Quaternion.Euler(0, headRotation.eulerAngles.y, 0);
                    poseGate.MarkCalibrated();
                    Probe.Write($"OPENXR_RECENTERED head={headPosition} yaw={referenceYaw.eulerAngles.y:F2}");
                }
                else Probe.Write("OPENXR_TRACKING_RESUMED referencePreserved=True");

                trackingSuspended = false;
            }

            VrCamera.TrackedPose(camera, headPosition, headRotation, referencePosition, referenceYaw,
                out var trackedPosition, out var trackedRotation);
            var localOffset = Quaternion.Inverse(referenceYaw) * (headPosition - referencePosition);
            VrPointer.Prepare();
            camera.transform.SetPositionAndRotation(trackedPosition, trackedRotation);
            if (report)
                Probe.Write(
                    $"OPENXR_HEAD_APPLIED offset={Quaternion.Inverse(referenceYaw) * (headPosition - referencePosition)} deltaDegrees={Quaternion.Angle(referenceYaw, headRotation):F2}");
            // XR supplies the eye offset around this tracked camera pose.
            // Query both eyes before changing the camera for either eye render.
            var parameters = new RenderParameter[count];
            var targets = new RenderTexture[count];
            for (int i = 0; i < count; i++)
            {
                display.GetRenderPass(i, out var pass);
                getParameter!(ref pass, camera.Pointer, 0, out parameters[i]);
                targets[i] = display.GetRenderTextureForRenderPass(i);
                if (targets[i] == null) throw new InvalidOperationException("Missing XR eye texture " + i);
            }

            if (world && MovementDiagnostics.Due)
                MovementDiagnostics.Capture(camera, position, rotation, localOffset,
                    parameters[0].view.inverse.GetColumn(3), parameters[1].view.inverse.GetColumn(3));

            // Pausing removes the disabled camera from Unity's camera array.
            // Render the current player explicitly; never replay an old array
            // containing the previous scene's camera or no camera at all.
            var eyeCameras = new Il2CppReferenceArray<Camera>(new[] { camera });
            bool menu = VrMenu.Prepare(context, camera, vrRotation);
            bool capture = OpenXRDiagnostics.BeginFrame();
            Nested = true;
            for (int eye = 0; eye < count; eye++)
            {
                var parameter = parameters[eye];
                var inverseView = parameter.view.inverse;
                Vector3 eyePosition = inverseView.GetColumn(3);
                Vector3 forward = -inverseView.GetColumn(2);
                Vector3 up = inverseView.GetColumn(1);
                camera.transform.SetPositionAndRotation(eyePosition, Quaternion.LookRotation(forward, up));
                camera.targetTexture = targets[eye];
                camera.aspect = (float)targets[eye].width / targets[eye].height;
                EyeIndex = eye;
                EyeProjection = parameter.projection;
                camera.projectionMatrix = EyeProjection;
                if (world)
                    using (DistortionControl.ApplyIfEnabled())
                        pipe.Render(context, eyeCameras);
                if (world && !menu) VrPointer.Render(context, camera, targets[eye]);
                if (menu || !world) VrMenu.Render(context, camera, targets[eye], !world, menu);
                if (capture && menu) OpenXRDiagnostics.SaveMenu(targets[eye], eye);
                else if (capture && world)
                    OpenXRDiagnostics.CaptureEye(pipe, context, eyeCameras, camera, targets[eye], eye);
                if (report)
                    Probe.Write(
                        $"OPENXR_EYE_RENDERED eye={eye} frame={Time.frameCount} target={targets[eye].width}x{targets[eye].height} position={eyePosition} base={position} projectionPreserved={camera.projectionMatrix.Equals(EyeProjection)}");
            }

            if (report) nextReport = Time.realtimeSinceStartupAsDouble + 5;
        }
        catch (Exception ex)
        {
            failed = true;
            Probe.Write("OPENXR_RENDER_ERROR " + ex);
        }
        finally
        {
            camera.targetTexture = target;
            camera.aspect = aspect;
            camera.projectionMatrix = projection;
            camera.transform.SetPositionAndRotation(position, rotation);
            Nested = false;
            VrMenu.RestoreDesktopTarget(context);
        }
    }
}
