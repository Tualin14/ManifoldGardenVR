using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ManifoldProbe;

internal static class VrInteraction
{
    internal sealed record CameraState(Camera Camera, Vector3 Position, Quaternion Rotation);

    static Camera? scopedCamera;
    static bool failed;
    static int reportedFrame = -1;

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var patches = new List<(MethodInfo original, MethodInfo patch)>();
        try
        {
            void RayPatch(Type type, string name)
            {
                var original = AccessTools.Method(type, name) ?? throw new MissingMethodException(type.Name, name);
                var patch = AccessTools.Method(typeof(VrInteraction), nameof(AimRay));
                patches.Add((original, patch));
                harmony.Patch(original, postfix: new HarmonyMethod(patch));
            }

            RayPatch(typeof(Il2Cpp.OperateObjects), "GenerateRayForObjectInteraction");
            RayPatch(typeof(Il2Cpp.CarryRigidbody), "GenerateRayForCubeInteraction");
            foreach (var name in new[] { "Update", "FixedUpdate" })
            {
                var original = AccessTools.Method(typeof(Il2Cpp.CarryRigidbody), name) ??
                               throw new MissingMethodException("CarryRigidbody", name);
                var prefix = AccessTools.Method(typeof(VrInteraction), nameof(BeginCarry));
                var finalizer = AccessTools.Method(typeof(VrInteraction), nameof(EndCarry));
                patches.Add((original, prefix));
                patches.Add((original, finalizer));
                harmony.Patch(original, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
            }

            Probe.Write("VR_INTERACTION_HOOKS_READY rays=2 carry=2");
        }
        catch (Exception ex)
        {
            foreach (var patch in patches) harmony.Unpatch(patch.original, patch.patch);
            failed = true;
            Probe.Write("VR_INTERACTION_HOOK_ERROR " + ex);
        }
    }

    static bool Gameplay => !failed && VrCamera.OwnsLook && VrCamera.ReadyForStartup &&
                            OpenXRRenderer.Calibrated && OpenXRSessionProbe.Focused && Il2Cpp.GameManager.DoUpdate &&
                            Il2Cpp.GameManager.PlayerController != null &&
                            !Il2Cpp.GameManager.PlayerController.IsFrozen &&
                            Il2Cpp.InputController.IsSetUp && Il2Cpp.InputController.Instance != null &&
                            !Il2Cpp.InputController.Instance.DisableInput &&
                            Il2Cpp.InputController.currentControlMapping ==
                            Il2Cpp.InputController.MG_ControlMapping.gameplay;

    static bool TryPose(out Camera? camera, out Vector3 position, out Quaternion rotation)
    {
        camera = null;
        position = default;
        rotation = Quaternion.identity;
        if (!Gameplay) return false;
        camera = VrCamera.Current;
        if (camera == null) return false;
        if (scopedCamera == camera)
        {
            position = camera.transform.position;
            rotation = camera.transform.rotation;
            return true;
        }

        return VrStartup.Settings.ControllersEnabled
            ? OpenXRRenderer.TryControllerPose(camera, out position, out rotation)
            : OpenXRRenderer.TryGameplayPose(camera, out position, out rotation);
    }

    static void AimRay(ref Ray __result)
    {
        if (TryPose(out _, out var position, out var rotation))
            __result = new Ray(position, rotation * Vector3.forward);
        else if (Gameplay && VrStartup.Settings.ControllersEnabled)
            __result = new Ray(Vector3.zero, Vector3.zero);
    }

    internal static bool TryPointerRay(out Ray ray)
    {
        ray = default;
        if (!VrStartup.Settings.ControllersEnabled || !TryPose(out _, out var position, out var rotation)) return false;
        ray = new Ray(position, rotation * Vector3.forward);
        return true;
    }

    // The native carry code reads player.myCamera for spring targets and drop checks.
    // Supply controller aim only within that code; restore even on native exceptions.
    static bool BeginCarry(out CameraState? __state)
    {
        __state = null;
        if (scopedCamera != null) return true;
        if (!TryPose(out var camera, out var position, out var rotation))
            return !Gameplay || !VrStartup.Settings.ControllersEnabled;
        __state = new CameraState(camera!, camera!.transform.position, camera.transform.rotation);
        scopedCamera = camera;
        camera.transform.SetPositionAndRotation(position, rotation);
        return true;
    }

    static void EndCarry(CameraState? __state)
    {
        if (__state == null) return;
        try
        {
            if (__state.Camera != null)
                __state.Camera.transform.SetPositionAndRotation(__state.Position, __state.Rotation);
        }
        finally
        {
            scopedCamera = null;
        }
    }

    internal static void ReportInteract()
    {
        if (reportedFrame == Time.frameCount) return;
        reportedFrame = Time.frameCount;
        bool aiming = TryPose(out _, out var position, out var rotation);
        var carry = Il2Cpp.GameManager.PlayerCarry;
        Probe.Write(
            $"VR_INTERACT_DOWN frame={Time.frameCount} aimValid={aiming} source={(VrStartup.Settings.ControllersEnabled ? "right-controller" : "head")} origin={position} forward={rotation * Vector3.forward} carrying={(carry != null && carry.CurrentCube != null)}");
    }
}