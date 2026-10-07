using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;
using GameInput = Il2Cpp.InputController;

namespace ManifoldProbe;

internal static class QuestInput
{
    static readonly ControllerInputPolicy policy = new();
    static int sampledFrame = -1, turnedFrame = -1, cameraId;
    static ControllerContext context;
    static bool installed, failed;
    static HandInput left, right;
    static int levelSelectFrame = -1;

    internal static bool LevelSelectPressed
    {
        get
        {
            Sample();
            if ((policy.Down & ControllerButtons.LevelSelect) == 0 || levelSelectFrame == Time.frameCount)
                return false;
            levelSelectFrame = Time.frameCount;
            return true;
        }
    }

    // Read cached state without sampling devices or consuming a gameplay query.
    internal static string MovementState =>
        $"inputContext={context} inputFrame={sampledFrame} vrStick=({policy.MoveX:F4},{policy.MoveY:F4})";

    internal static bool RayMenuActive => VrStartup.Enabled && OpenXRSessionProbe.Running &&
        GameInput.IsSetUp && GameInput.currentControlMapping == GameInput.MG_ControlMapping.menu;

    internal static (bool valid, bool held, bool down, bool up) MenuPointerState
    {
        get
        {
            Sample();
            return (context == ControllerContext.Menu && right.Valid,
                (policy.Held & ControllerButtons.MenuClick) != 0,
                (policy.Down & ControllerButtons.MenuClick) != 0,
                (policy.Up & ControllerButtons.MenuClick) != 0);
        }
    }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        if (installed) return;
        var patches = new List<(MethodInfo original, MethodInfo patch)>();
        try
        {
            void Patch(Type type, string method, string callback, bool prefix = false, Type[]? parameters = null)
            {
                var original = AccessTools.Method(type, method, parameters) ??
                               throw new MissingMethodException(type.Name, method);
                var patch = AccessTools.Method(typeof(QuestInput), callback);
                patches.Add((original, patch));
                harmony.Patch(original, prefix: prefix ? new HarmonyMethod(patch) : null,
                    postfix: prefix ? null : new HarmonyMethod(patch));
            }

            Patch(typeof(GameInput), "GetButton", nameof(Button));
            Patch(typeof(GameInput), "GetButtonDown", nameof(ButtonDown));
            Patch(typeof(GameInput), "GetButtonUp", nameof(ButtonUp));
            Patch(typeof(GameInput), "GetAxis", nameof(Axis));
            Patch(typeof(GameInput), "get_PlayerIsUsingMouse", nameof(MenuMouse));
            Patch(typeof(Il2Cpp.RigidbodyController), "GetMovementVector", nameof(Movement));
            Patch(typeof(Il2Cpp.RigidbodyController), "HorizontalLook", nameof(Turn), true);
            // Rewired's menu module bypasses InputController and calls these int overloads directly.
            Patch(typeof(Il2CppRewired.Player), "GetAxis", nameof(MenuAxis), parameters: new[] { typeof(int) });
            Patch(typeof(Il2CppRewired.Player), "GetButton", nameof(MenuButton), parameters: new[] { typeof(int) });
            Patch(typeof(Il2CppRewired.Player), "GetButtonDown", nameof(MenuButtonDown),
                parameters: new[] { typeof(int) });
            Patch(typeof(Il2CppRewired.Player), "GetNegativeButton", nameof(MenuNegative),
                parameters: new[] { typeof(int) });
            Patch(typeof(Il2CppRewired.Player), "GetNegativeButtonDown", nameof(MenuNegativeDown),
                parameters: new[] { typeof(int) });
            installed = true;
            Probe.Write("QUEST_INPUT_HOOKS_READY count=" + patches.Count);
        }
        catch (Exception ex)
        {
            foreach (var patch in patches) harmony.Unpatch(patch.original, patch.patch);
            failed = true;
            Probe.Write("QUEST_INPUT_HOOK_ERROR " + ex);
        }
    }

    internal static void Reset()
    {
        policy.Reset();
        sampledFrame = turnedFrame = -1;
        cameraId = 0;
        left = right = default;
        context = ControllerContext.Blocked;
    }

    static ControllerContext GetContext()
    {
        if (!installed || failed || !VrStartup.Settings.ControllersEnabled || !VrStartup.Enabled ||
            !OpenXRControllerActions.Ready || !OpenXRSessionProbe.Running || !OpenXRSessionProbe.Focused ||
            !OpenXRRenderer.Calibrated || !GameInput.IsSetUp || GameInput.Instance == null ||
            GameInput.Instance.DisableInput)
            return ControllerContext.Blocked;
        if (GameInput.currentControlMapping == GameInput.MG_ControlMapping.menu) return ControllerContext.Menu;
        if (GameInput.currentControlMapping != GameInput.MG_ControlMapping.gameplay || !Probe.HasWorldCamera ||
            !Il2Cpp.GameManager.DoUpdate || Il2Cpp.GameManager.PlayerController == null ||
            Il2Cpp.GameManager.PlayerController.IsFrozen) return ControllerContext.Blocked;
        return ControllerContext.Gameplay;
    }

    static HandInput ReadHand(XRNode node)
    {
        ulong device = InputTracking.GetDeviceIdAtXRNode(node);
        if (!InputDevices.IsDeviceValid(device)) return default;
        // Non-generic bindings avoid unavailable IL2CPP AOT generic instantiations.
        if (!InputDevices.TryGetFeatureValue_bool(device, "IsTracked", out var tracked) || !tracked ||
            !InputDevices.TryGetFeatureValue_Vector2f(device, "Primary2DAxis", out var stick)) return default;
        // A missing bound feature invalidates this hand; it must not look like a release edge.
        if (!InputDevices.TryGetFeatureValue_float(device, "Trigger", out var trigger) ||
            !InputDevices.TryGetFeatureValue_bool(device, "PrimaryButton", out var primary) ||
            !InputDevices.TryGetFeatureValue_bool(device, "SecondaryButton", out var secondary) ||
            !InputDevices.TryGetFeatureValue_float(device, "Grip", out var grip)) return default;
        bool menu = false;
        if (node == XRNode.LeftHand && !InputDevices.TryGetFeatureValue_bool(device, "MenuButton", out menu))
            return default;
        return new HandInput(device, true, stick.x, stick.y, trigger, primary, secondary, menu, Grip: grip);
    }

    // Sample lazily on the first game query, once per Unity frame. Check gates on every
    // query so a pause/disable in the middle of a frame cannot leak gameplay input into UI.
    static void Sample()
    {
        try
        {
            var next = GetContext();
            var camera = next == ControllerContext.Blocked ? null : VrCamera.Current;
            int nextCamera = camera == null ? 0 : camera.GetInstanceID();
            if (next != context || nextCamera != cameraId)
            {
                Reset();
                context = next;
                cameraId = nextCamera;
            }

            if (sampledFrame == Time.frameCount) return;
            sampledFrame = Time.frameCount;
            left = next == ControllerContext.Blocked ? default : ReadHand(XRNode.LeftHand);
            right = next == ControllerContext.Blocked ? default : ReadHand(XRNode.RightHand);
            policy.Step(Time.realtimeSinceStartupAsDouble, next, left, right,
                VrStartup.Settings.ControllerDeadzone, VrStartup.Settings.SnapTurnDegrees, VrStartup.Settings);
            if ((policy.Down & ControllerButtons.Interact) != 0) VrInteraction.ReportInteract();
            if (policy.Recenter)
            {
                OpenXRRenderer.RequestRecenter();
                Probe.Write("QUEST_RECENTER_REQUESTED");
            }
        }
        catch (Exception ex)
        {
            failed = true;
            Reset();
            Probe.Write("QUEST_INPUT_DISABLED_ERROR " + ex);
        }
    }

    static void Button(string __0, ref bool __result)
    {
        Sample();
        if (MenuNavigation(__0, policy.Held, ref __result)) return;
        __result |= (policy.Held & ControllerInputPolicy.Action(__0)) != 0;
    }

    static void ButtonDown(string __0, ref bool __result)
    {
        Sample();
        if (MenuNavigation(__0, policy.Down, ref __result)) return;
        __result |= (policy.Down & ControllerInputPolicy.Action(__0)) != 0;
    }

    static void ButtonUp(string __0, ref bool __result)
    {
        Sample();
        if (MenuNavigation(__0, policy.Up, ref __result)) return;
        var action = ControllerInputPolicy.Action(__0);
        if ((action & (policy.Held | policy.Up)) == 0) return;
        __result = (__result || (policy.Up & action) != 0) && !GameInput.GetButton(__0);
    }

    static bool MenuNavigation(string key, ControllerButtons state, ref bool result)
    {
        if (!RayMenuActive || !key.StartsWith("UI ", StringComparison.Ordinal)) return false;
        // Suppress native emulated gamepad/keyboard navigation as well as VR
        // navigation. The menu button remains the only controller shortcut.
        if (key is not ("UI Confirm" or "UI Cancel" or "UI Tab Left" or "UI Tab Right" or
            "UI Return To Game")) return false;
        result = key == "UI Return To Game" && (state & ControllerButtons.ReturnToGame) != 0;
        return true;
    }

    internal static int MenuTabDirection
    {
        get
        {
            return 0;
        }
    }

    static void Axis(string __0, ref float __result)
    {
        if (VrCamera.OwnsLook && (__0 == "Look Horizontal" || __0 == "Look Vertical"))
        {
            __result = 0;
            return;
        }

        Sample();
        if (RayMenuActive && __0 is "UI Horizontal" or "UI Vertical") __result = 0;
    }

    static int menuHorizontal = -1, menuVertical = -1, menuConfirm = -1, menuCancel = -1;
    static int menuTabLeft = -1, menuTabRight = -1;

    static bool MenuReady()
    {
        Sample();
        if (context != ControllerContext.Menu) return false;
        try
        {
            if (menuHorizontal < 0)
            {
                var mapping = Il2CppRewired.ReInput.mapping;
                menuHorizontal = mapping.GetActionId("UI Horizontal");
                menuVertical = mapping.GetActionId("UI Vertical");
                menuConfirm = mapping.GetActionId("UI Confirm");
                menuCancel = mapping.GetActionId("UI Cancel");
                menuTabLeft = mapping.GetActionId("UI Tab Left");
                menuTabRight = mapping.GetActionId("UI Tab Right");
                if (menuHorizontal < 0 || menuVertical < 0 || menuConfirm < 0 || menuCancel < 0)
                    throw new InvalidOperationException("Missing Rewired menu actions");
                Probe.Write(
                    $"QUEST_MENU_ACTIONS_READY horizontal={menuHorizontal} vertical={menuVertical} confirm={menuConfirm} cancel={menuCancel}");
            }

            return true;
        }
        catch (Exception ex)
        {
            failed = true;
            Reset();
            Probe.Write("QUEST_MENU_INPUT_ERROR " + ex);
            return false;
        }
    }

    static void MenuMouse(ref bool __result)
    {
        Sample();
        // Native cycle arrows and tabs use their mouse interaction path.
        if (RayMenuActive) __result = true;
    }

    static ControllerButtons MenuAction(int id, bool negative)
    {
        if (id == menuHorizontal) return negative ? ControllerButtons.Left : ControllerButtons.Right;
        if (id == menuVertical) return negative ? ControllerButtons.Down : ControllerButtons.Up;
        if (!negative && id == menuConfirm) return ControllerButtons.Confirm;
        if (!negative && id == menuCancel) return ControllerButtons.Cancel;
        if (!negative && menuTabLeft >= 0 && id == menuTabLeft) return ControllerButtons.TabLeft;
        if (!negative && menuTabRight >= 0 && id == menuTabRight) return ControllerButtons.TabRight;
        return ControllerButtons.None;
    }

    static void MenuAxis(int __0, ref float __result)
    {
        if (!MenuReady()) return;
        if (__0 == menuHorizontal || __0 == menuVertical) __result = 0;
    }

    static void MenuButton(int __0, ref bool __result)
    {
        if (!MenuReady()) return;
        var action = MenuAction(__0, false);
        if (action != ControllerButtons.None) __result = false;
    }

    static void MenuButtonDown(int __0, ref bool __result)
    {
        if (!MenuReady()) return;
        var action = MenuAction(__0, false);
        if (action != ControllerButtons.None) __result = false;
    }

    static void MenuNegative(int __0, ref bool __result)
    {
        if (MenuReady() && MenuAction(__0, false) != ControllerButtons.None) __result = false;
    }

    static void MenuNegativeDown(int __0, ref bool __result)
    {
        if (MenuReady() && MenuAction(__0, false) != ControllerButtons.None) __result = false;
    }

    static void Movement(ref Vector2 __result)
    {
        Sample();
        if (context != ControllerContext.Gameplay || (policy.MoveX == 0 && policy.MoveY == 0)) return;
        // Merge after the game's keyboard/controller shaping; do not change keyboard speed.
        __result = Vector2.ClampMagnitude(__result + new Vector2(policy.MoveX, policy.MoveY), 1);
    }

    static void Turn(Il2Cpp.RigidbodyController __instance)
    {
        Sample();
        if (context != ControllerContext.Gameplay || policy.TurnDegrees == 0 || turnedFrame == Time.frameCount ||
            !__instance.canRotateX || __instance.IsFrozen || __instance != Il2Cpp.GameManager.PlayerController) return;
        turnedFrame = Time.frameCount;
        // HorizontalLook applies this accumulator to bodyTransform.localEulerAngles.y.
        // Retain its current gravity frame and mouse input, and avoid sensitivity/deltaTime scaling.
        __instance.bodyXRotation += policy.TurnDegrees;
    }

    internal static void Report()
    {
        Sample();
        Probe.Write(
            $"QUEST_INPUT_STATUS hooks={installed} failed={failed} bindings={OpenXRControllerActions.Ready} enabled={VrStartup.Settings.ControllersEnabled} context={context} left={left.Device}/{left.Valid} right={right.Device}/{right.Valid} move=({policy.MoveX:F2},{policy.MoveY:F2}) held={policy.Held} deadzone={VrStartup.Settings.ControllerDeadzone} snap={VrStartup.Settings.SnapTurnDegrees}");
    }
}
