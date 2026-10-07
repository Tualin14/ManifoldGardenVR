using UnityEngine;

namespace ManifoldProbe;

internal static class VrCamera
{
    internal static bool OwnsLook => VrStartup.Enabled && OpenXRSessionProbe.Running && Current != null;

    // Body orientation includes gravity and locomotion yaw. Desktop mouse
    // pitch must not tilt the positional head-tracking coordinate frame.
    internal static Quaternion Rotation(Camera camera)
    {
        var player = Il2Cpp.GameManager.PlayerController;
        return camera == Current && player != null && player.bodyTransform != null
            ? player.bodyTransform.rotation
            : camera.transform.rotation;
    }

    // Rendering, pickup rays and the original carry solver share the same center-eye pose.
    internal static void TrackedPose(Camera camera, Vector3 headPosition, Quaternion headRotation,
        Vector3 referencePosition, Quaternion referenceYaw, out Vector3 position, out Quaternion rotation)
    {
        var body = Rotation(camera);
        var inverseYaw = Quaternion.Inverse(referenceYaw);
        position = camera.transform.position + body * (inverseYaw * (headPosition - referencePosition));
        rotation = body * (inverseYaw * headRotation);
    }

    // The logo scene also creates a temporary player/camera before the title
    // flag is set. Wait for a real, fully loaded level before starting XR.
    internal static bool ReadyForStartup
    {
        get
        {
            if (Current == null || Il2Cpp.LevelLoader.IsLoadingOrPendingLoad) return false;
            var player = Il2Cpp.GameManager.PlayerController;
            return player != null && player.playerLevelSystems != null;
        }
    }

    // A title menu has no gameplay camera. Its existing overlay is presented
    // by VrMenu's neutral camera; a temporary logo player is still insufficient.
    internal static bool TitleMenuReady =>
        Il2Cpp.GameManager.IsInitialized && Il2Cpp.LevelLoader.IsOnTitleScreen &&
        !Il2Cpp.LevelLoader.IsLoadingOrPendingLoad && Il2Cpp.InputController.IsSetUp &&
        Il2Cpp.InputController.Instance != null && !Il2Cpp.InputController.Instance.DisableInput &&
        Il2Cpp.InputController.currentControlMapping == Il2Cpp.InputController.MG_ControlMapping.menu;

    internal static bool ReadyForPresentation => ReadyForStartup || TitleMenuReady;

    // The player can live in DontDestroyOnLoad after a level transition, and
    // pause disables the camera. Neither changes which camera owns the view.
    internal static Camera? Current
    {
        get
        {
            if (!Il2Cpp.GameManager.IsInitialized || Il2Cpp.LevelLoader.IsOnTitleScreen) return null;
            var player = Il2Cpp.GameManager.PlayerController;
            if (player != null && player.myCamera != null) return player.myCamera;
            return null;
        }
    }
}