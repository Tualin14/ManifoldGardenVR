using System.Runtime.InteropServices;
using UnityEngine.XR;

namespace ManifoldProbe;

// ABI and ordering match the installed com.unity.xr.openxr@1.3.1 package:
// OpenXRInput.AttachActionSets and OculusTouchControllerProfile. Do not use XrActionType values here.
internal static class OpenXRControllerActions
{
    internal const string Profile = "/interaction_profiles/oculus/touch_controller";
    internal const string Left = "/user/hand/left", Right = "/user/hand/right";

    [StructLayout(LayoutKind.Sequential)]
    internal struct ActionGuid
    {
        public ulong First, Second;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct Binding
    {
        public ulong Action;
        [MarshalAs(UnmanagedType.LPStr)] public string Path;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    delegate ulong RegisterDevice(string path, string profile, uint characteristics, string name, string manufacturer,
        string serial);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    delegate ulong CreateSet(string name, string label, ActionGuid guid);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    delegate ulong CreateAction(ulong set, string name, string label, uint type, ActionGuid guid,
        [In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)]
        string[] paths, uint pathCount,
        [In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)]
        string[] usages, uint usageCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.U1)]
    delegate bool Suggest(string profile, [In] Binding[] bindings, uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    delegate bool Attach();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void SendImpulse(uint device, ulong action, float amplitude, float frequency, float duration);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void StopImpulse(uint device, ulong action);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    delegate ulong GetActionHandle(uint device, string controlName);

    static SendImpulse? sendImpulse;
    static StopImpulse? stopImpulse;
    static GetActionHandle? getActionHandle;
    static uint leftDevice, rightDevice;
    static ulong leftHaptic, rightHaptic;

    internal static bool Ready { get; private set; }

    internal static bool HapticsReady
    {
        get
        {
            if (!Ready || sendImpulse == null || stopImpulse == null || getActionHandle == null)
                return false;
            if (leftHaptic != 0 && rightHaptic != 0) return true;
            // CreateAction returns a provider registration index for SuggestBindings,
            // not the XrAction required by SendHapticImpulse/StopHaptics. Resolve it
            // through the provider's device/control map, as OpenXRInput.cs does.
            // A device may become available after attachment; retry until resolved.
            if (leftHaptic == 0) leftHaptic = getActionHandle(leftDevice, "haptic_left");
            if (rightHaptic == 0) rightHaptic = getActionHandle(rightDevice, "haptic_right");
            if (leftHaptic == 0 || rightHaptic == 0) return false;
            Probe.Write($"OPENXR_HAPTIC_HANDLES_READY leftDevice={leftDevice} rightDevice={rightDevice} " +
                        $"left=0x{leftHaptic:X} right=0x{rightHaptic:X}");
            return true;
        }
    }

    internal static void HapticImpulse(bool right, float amplitude, float duration)
    {
        if (HapticsReady)
            sendImpulse!(right ? rightDevice : leftDevice,
                right ? rightHaptic : leftHaptic, amplitude, 0, duration);
    }

    internal static void StopHaptic(bool right)
    {
        // Teardown must not attempt to resolve a device that never became ready.
        ulong action = right ? rightHaptic : leftHaptic;
        if (Ready && stopImpulse != null && action != 0)
            stopImpulse(right ? rightDevice : leftDevice, action);
    }

    internal static void Reset()
    {
        StopHaptic(false);
        StopHaptic(true);
        ClearBindings();
    }

    static void ClearBindings()
    {
        Ready = false;
        leftDevice = rightDevice = 0;
        leftHaptic = rightHaptic = 0;
        sendImpulse = null;
        stopImpulse = null;
        getActionHandle = null;
    }

    internal static void Register(IntPtr module)
    {
        ClearBindings();

        T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(
            NativeLibrary.GetExport(module, "OpenXRInputProvider_" + name));

        var register = Export<RegisterDevice>("RegisterDeviceDefinition");
        var createSet = Export<CreateSet>("CreateActionSet");
        var createAction = Export<CreateAction>("CreateAction");
        var suggest = Export<Suggest>("SuggestBindings");
        var attach = Export<Attach>("AttachActionSets");
        var common = InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.TrackedDevice |
                     InputDeviceCharacteristics.Controller;

        uint Device(string path, InputDeviceCharacteristics hand)
        {
            // Provider device IDs differ from UnityEngine.XR InputDevice IDs.
            ulong id = register(path, Profile, (uint)(common | hand), "Oculus Touch Controller OpenXR",
                "Oculus", "");
            if (id == 0 || id > uint.MaxValue)
                throw new InvalidOperationException("OpenXR controller device registration failed");
            return (uint)id;
        }

        leftDevice = Device(Left, InputDeviceCharacteristics.Left);
        rightDevice = Device(Right, InputDeviceCharacteristics.Right);
        ulong set = createSet("mgvr_touch", "MGVR Touch", default);
        if (set == 0) throw new InvalidOperationException("OpenXR controller action set creation failed");
        var bindings = new List<Binding>();

        void Action(string name, uint type, string usage, string? left, string? right)
        {
            string[] paths = { Left, Right };
            ulong id = createAction(set, name, name, type, default, paths, 2, new[] { usage }, 1);
            if (id == 0) throw new InvalidOperationException("OpenXR controller action failed: " + name);
            if (left != null) bindings.Add(new Binding { Action = id, Path = Left + left });
            if (right != null) bindings.Add(new Binding { Action = id, Path = Right + right });
        }

        Action("thumbstick", 2, "Primary2DAxis", "/input/thumbstick", "/input/thumbstick");
        Action("trigger", 1, "Trigger", "/input/trigger/value", "/input/trigger/value");
        Action("primarybutton", 0, "PrimaryButton", "/input/x/click", "/input/a/click");
        Action("secondarybutton", 0, "SecondaryButton", "/input/y/click", "/input/b/click");
        Action("menubutton", 0, "MenuButton", "/input/menu/click", null);
        Action("grip", 1, "Grip", "/input/squeeze/value", "/input/squeeze/value");
        // No grip-pose hand models: expose OpenXR aim through the legacy XR device pose.
        Action("devicepose", 3, "Device", "/input/aim/pose", "/input/aim/pose");

        // Each output action binds only to its corresponding hand.
        ulong Haptic(string name, string hand)
        {
            ulong id = createAction(set, name, name, 4, default, new[] { hand }, 1, new[] { "Haptic" }, 1);
            if (id == 0) throw new InvalidOperationException("OpenXR haptic action failed: " + name);
            bindings.Add(new Binding { Action = id, Path = hand + "/output/haptic" });
            return id;
        }

        ulong leftRegistration = Haptic("haptic_left", Left);
        ulong rightRegistration = Haptic("haptic_right", Right);
        sendImpulse = Export<SendImpulse>("SendHapticImpulse");
        stopImpulse = Export<StopImpulse>("StopHaptics");
        getActionHandle = Export<GetActionHandle>("GetActionIdByControl");
        if (!suggest(Profile, bindings.ToArray(), (uint)bindings.Count) || !attach())
            throw new InvalidOperationException("OpenXR controller bindings/attachment failed");
        Ready = true;
        Probe.Write("OPENXR_CONTROLLER_ACTIONS_READY profile=" + Profile);
        Probe.Write($"OPENXR_HAPTIC_ACTIONS_REGISTERED left={leftRegistration} right={rightRegistration}");
        if (!HapticsReady) Probe.Write("OPENXR_HAPTIC_HANDLES_PENDING");
    }
}