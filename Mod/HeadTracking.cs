using System.Text;
using UnityEngine;
using Valve.VR;

namespace ManifoldProbe;

// Background mode for desktop tests; Scene mode when the native submit bridge is enabled.
internal static class HeadTracking
{
    static CVRSystem? system;
    static bool sceneApplication;
    public static CVRSystem? System => system;
    public static HmdMatrix34_t RenderPose { get; private set; }
    static readonly TrackedDevicePose_t[] poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    static bool calibrated;
    static Vector3 referencePosition;
    static Quaternion referenceRotation;
    static double nextLog;
    static long appliedFrames;
    static bool waitingLogged;
    public static bool Enabled { get; private set; }

    public static void Start(bool scene = false)
    {
        if (system != null && scene != sceneApplication) Shutdown();
        if (system == null)
        {
            var error = EVRInitError.None;
            system = OpenVR.Init(ref error, scene ? EVRApplicationType.VRApplication_Scene : EVRApplicationType.VRApplication_Background);
            if (error != EVRInitError.None || system == null)
            {
                Probe.Write("HMD_INIT_FAILED " + error);
                system = null;
                return;
            }
            var name = new StringBuilder(256);
            var propertyError = ETrackedPropertyError.TrackedProp_Success;
            system.GetStringTrackedDeviceProperty(0, ETrackedDeviceProperty.Prop_ModelNumber_String, name, 256, ref propertyError);
            sceneApplication = scene;
            Probe.Write("HMD_INITIALIZED model=" + name + "; mode=" + (scene ? "Scene" : "Background"));
        }
        Enabled = true;
        Recenter();
        Probe.Write("HMD_TRACKING_ENABLED");
    }

    public static void Stop()
    {
        Enabled = false;
        calibrated = false;
        Probe.Write("HMD_TRACKING_DISABLED");
    }

    public static void Recenter() { calibrated = false; waitingLogged = false; }

    public static void Shutdown()
    {
        Enabled = false;
        if (system != null) OpenVR.Shutdown();
        system = null;
    }

    public static bool TryApply(Camera camera)
    {
        if (!Enabled || system == null) return false;
        system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, sceneApplication ? 0.015f : 0, poses);
        var p = poses[0];
        var activity = system.GetTrackedDeviceActivityLevel(0);
        if (!p.bDeviceIsConnected || !p.bPoseIsValid || p.eTrackingResult != ETrackingResult.Running_OK ||
            activity == EDeviceActivityLevel.k_EDeviceActivityLevel_Standby)
        {
            if (!waitingLogged) Probe.Write($"HMD_WAITING valid={p.bPoseIsValid} connected={p.bDeviceIsConnected} activity={activity}");
            waitingLogged = true;
            calibrated = false;
            return false;
        }
        waitingLogged = false;
        var m = p.mDeviceToAbsoluteTracking;
        RenderPose = m;
        // OpenVR is right-handed (-Z forward); Unity is left-handed (+Z forward).
        var position = new Vector3(m.m3, m.m7, -m.m11);
        var rotation = Quaternion.LookRotation(new Vector3(-m.m2, -m.m6, m.m10), new Vector3(m.m1, m.m5, -m.m9));
        if (!calibrated)
        {
            referencePosition = position;
            referenceRotation = rotation;
            calibrated = true;
            Probe.Write("HMD_RECENTERED camera=" + camera.name);
        }
        var relativeRotation = Quaternion.Inverse(referenceRotation) * rotation;
        var relativePosition = Quaternion.Inverse(referenceRotation) * (position - referencePosition);
        var baseRotation = camera.transform.rotation;
        camera.transform.position += baseRotation * relativePosition;
        camera.transform.rotation = baseRotation * relativeRotation;
        appliedFrames++;
        if (Time.realtimeSinceStartupAsDouble >= nextLog)
        {
            nextLog = Time.realtimeSinceStartupAsDouble + 2;
            Probe.Write($"HMD_APPLIED frames={appliedFrames} gameFrame={Time.frameCount} offset=({relativePosition.x:F4},{relativePosition.y:F4},{relativePosition.z:F4}) rotationDeltaDegrees={Quaternion.Angle(Quaternion.identity, relativeRotation):F2} poseActivity={activity}");
        }
        return true;
    }
}
