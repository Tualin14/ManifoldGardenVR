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

    internal static bool Ready { get; private set; }
    internal static void Reset() => Ready = false;

    internal static void Register(IntPtr module)
    {
        Ready = false;

        T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(
            NativeLibrary.GetExport(module, "OpenXRInputProvider_" + name));

        var register = Export<RegisterDevice>("RegisterDeviceDefinition");
        var createSet = Export<CreateSet>("CreateActionSet");
        var createAction = Export<CreateAction>("CreateAction");
        var suggest = Export<Suggest>("SuggestBindings");
        var attach = Export<Attach>("AttachActionSets");
        var common = InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.TrackedDevice |
                     InputDeviceCharacteristics.Controller;
        if (register(Left, Profile, (uint)(common | InputDeviceCharacteristics.Left), "Oculus Touch Controller OpenXR",
                "Oculus", "") == 0 ||
            register(Right, Profile, (uint)(common | InputDeviceCharacteristics.Right),
                "Oculus Touch Controller OpenXR", "Oculus", "") == 0)
            throw new InvalidOperationException("OpenXR controller device registration failed");
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
        if (!suggest(Profile, bindings.ToArray(), (uint)bindings.Count) || !attach())
            throw new InvalidOperationException("OpenXR controller bindings/attachment failed");
        Ready = true;
        Probe.Write("OPENXR_CONTROLLER_ACTIONS_READY profile=" + Profile);
    }
}