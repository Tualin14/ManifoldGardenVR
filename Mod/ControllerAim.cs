using UnityEngine;
using UnityEngine.XR;

namespace ManifoldProbe;

internal static class ControllerAim
{
    internal static bool TryRead(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        if (!OpenXRControllerActions.Ready || !OpenXRSessionProbe.Focused) return false;
        var device = InputTracking.GetDeviceIdAtXRNode(XRNode.RightHand);
        if (!InputDevices.IsDeviceValid(device) ||
            !InputDevices.TryGetFeatureValue_bool(device, "IsTracked", out var tracked) || !tracked ||
            !InputDevices.TryGetFeatureValue_UInt32(device, "TrackingState", out var state) || (state & 3) != 3)
            return false;
        position = InputTracking.GetLocalPosition(XRNode.RightHand);
        rotation = InputTracking.GetLocalRotation(XRNode.RightHand);
        float norm = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z +
                     rotation.w * rotation.w;
        return float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z) &&
               float.IsFinite(norm) && norm >= .9f && norm <= 1.1f;
    }
}