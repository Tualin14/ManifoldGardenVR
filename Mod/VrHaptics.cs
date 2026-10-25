using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace ManifoldProbe;

// Observe native vibration requests without changing any game's return values,
// input or state. Native event scopes distinguish right-hand object feedback
// from body/world feedback; no vibration is generated from button presses.
internal static class VrHaptics
{
    static readonly HapticPolicy policy = new();
    [ThreadStatic] static HapticKind source;
    static bool installed, failed, leftActive, rightActive;
    static int level;
    static double nextOutput;
    static long objectScopes, bodyScopes, received, accepted, leftOutputs, rightOutputs;
    static double nextEventReport, nextStatusReport, nextGateReport;
    static string reportedGate = "";
    static bool pending;

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        if (installed) return;
        var patches = new List<(MethodInfo method, MethodInfo callback)>();
        try
        {
            void Patch(Type type, string method, string? prefix = null, string? postfix = null,
                string? finalizer = null, Type[]? arguments = null)
            {
                var original = AccessTools.Method(type, method, arguments) ??
                               throw new MissingMethodException(type.FullName, method);

                HarmonyMethod? Callback(string? name)
                {
                    if (name == null) return null;
                    var callback = AccessTools.Method(typeof(VrHaptics), name);
                    patches.Add((original, callback));
                    return new HarmonyMethod(callback);
                }

                harmony.Patch(original, prefix: Callback(prefix), postfix: Callback(postfix),
                    finalizer: Callback(finalizer));
            }

            foreach (var item in new[]
                     {
                         (typeof(Il2Cpp.CarryRigidbody), "Update"),
                         (typeof(Il2Cpp.GravityCube), "HandleCollision"),
                         (typeof(Il2Cpp.CubeReceiverController), "StartCubeAnimation")
                     }) Patch(item.Item1, item.Item2, nameof(ObjectScope), finalizer: nameof(EndScope));
            // Resolve the generated iterator by its stable routine name rather than
            // tying the mod to the compiler's numeric suffix in one game build.
            var rotation = typeof(Il2Cpp.CarryRigidbody).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Single(t => t.Name.StartsWith("_RotateCubeRoutine_d__", StringComparison.Ordinal));
            Patch(rotation, "MoveNext", nameof(ObjectScope), finalizer: nameof(EndScope));
            Patch(typeof(Il2Cpp.RigidbodyController), "CalculateFalling", nameof(BodyScope),
                finalizer: nameof(EndScope));
            Patch(typeof(Il2Cpp.InputController), "Vibrate", postfix: nameof(Impulse),
                arguments: new[] { typeof(float), typeof(float) });
            Patch(typeof(Il2Cpp.InputController), "VibrateUntilStopped", postfix: nameof(Continuous),
                arguments: new[] { typeof(float) });
            Patch(typeof(Il2Cpp.InputController), "StopAllVibration", postfix: nameof(Stop));
            installed = true;
            Probe.Write("VR_HAPTICS_HOOKS_READY nativeEvents=true object=right bodyWorld=both");
        }
        catch (Exception ex)
        {
            foreach (var patch in patches) harmony.Unpatch(patch.method, patch.callback);
            failed = true;
            Probe.Write("VR_HAPTICS_HOOK_ERROR " + ex);
        }
    }

    static void ObjectScope(out HapticKind __state)
    {
        objectScopes++;
        __state = source;
        source = HapticKind.Object;
    }

    static void BodyScope(out HapticKind __state)
    {
        bodyScopes++;
        __state = source;
        source = HapticKind.Body;
    }

    static void EndScope(HapticKind __state) => source = __state;
    static void Impulse(float __0, float __1) => Capture(__0, __1, false);
    static void Continuous(float __0) => Capture(__0, 0, true);

    // Name the first blocked stage instead of silently discarding every request.
    static string Gate()
    {
        if (!installed) return "hooks-not-installed";
        if (failed) return "failed";
        if (!VrStartup.LaunchedInVr || !VrStartup.Enabled) return "vr-disabled";
        var settings = VrStartup.Settings;
        if (!settings.ControllersEnabled) return "controllers-disabled";
        if (!settings.HapticsEnabled) return "vr-vibration-off";
        if (!settings.ValidHaptics || settings.HapticsStrength <= 0) return "strength-zero-or-invalid";
        if (!OpenXRControllerActions.HapticsReady) return "actions-not-ready";
        if (!OpenXRSessionProbe.Running) return "session-not-running";
        if (!OpenXRSessionProbe.Focused) return "session-not-focused";
        if (Il2Cpp.GameManager.IsPaused) return "game-paused";
        if (Il2Cpp.GameSettings.Instance == null) return "game-settings-not-ready";
        if (Il2Cpp.GameSettings.ReadInt(Il2Cpp.SettingKey.vibration) != 1) return "game-vibration-off";
        return "ready";
    }

    static void SyncLevel()
    {
        int current = Il2Cpp.LevelLoader.CurrentLevelSystems?.GetInstanceID() ?? 0;
        if (current == level) return;
        ClearEffects("level-changed");
        level = current;
    }

    static void Capture(float strength, float duration, bool continuous)
    {
        try
        {
            received++;
            string gate = Gate();
            double now = Time.realtimeSinceStartupAsDouble;
            // Keep diagnostic output bounded even for repeated collision events.
            if (received <= 8 || now >= nextEventReport)
            {
                Probe.Write(
                    $"VR_HAPTICS_EVENT count={received} source={source} strength={strength:F3} duration={duration:F3} continuous={continuous} gate={gate}");
                nextEventReport = now + 1;
            }

            if (gate != "ready") return;
            SyncLevel();
            policy.Request(source, strength, duration, now, continuous);
            accepted++;
            pending = true;
            nextOutput = 0; // A short object impulse takes over promptly.
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    static bool Tracked(XRNode hand)
    {
        ulong device = InputTracking.GetDeviceIdAtXRNode(hand);
        return InputDevices.IsDeviceValid(device) &&
               InputDevices.TryGetFeatureValue_bool(device, "IsTracked", out var tracked) && tracked;
    }

    internal static void Tick()
    {
        try
        {
            SyncLevel();
            double now = Time.realtimeSinceStartupAsDouble;
            string gate = Gate();
            if (gate == "ready" && !Tracked(XRNode.LeftHand)) gate = "left-not-tracked";
            if (gate == "ready" && !Tracked(XRNode.RightHand)) gate = "right-not-tracked";
            if (gate != reportedGate && now >= nextGateReport)
            {
                Probe.Write("VR_HAPTICS_GATE " + gate);
                reportedGate = gate;
                nextGateReport = now + 1;
            }

            if (now >= nextStatusReport)
            {
                Report();
                nextStatusReport = now + 15;
            }

            if (gate != "ready")
            {
                ClearEffects(gate);
                return;
            }

            if (now < nextOutput) return;
            nextOutput = now + .05;

            void Send(bool right, ref bool active)
            {
                var output = policy.Sample(right, now, VrStartup.Settings.HapticsStrength);
                if (output.Amplitude > 0)
                {
                    OpenXRControllerActions.HapticImpulse(right, output.Amplitude, output.Duration);
                    long count = right ? ++rightOutputs : ++leftOutputs;
                    if (count <= 8)
                        Probe.Write(
                            $"VR_HAPTICS_OUTPUT hand={(right ? "right" : "left")} count={count} strength={output.Amplitude:F3} duration={output.Duration:F3}");
                    active = true;
                }
                else if (active)
                {
                    OpenXRControllerActions.StopHaptic(right);
                    active = false;
                }
            }

            Send(false, ref leftActive);
            Send(true, ref rightActive);
            pending = false;
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    internal static void Stop()
        => ClearEffects("explicit-stop");

    static void ClearEffects(string reason)
    {
        if (pending || leftActive || rightActive)
            Probe.Write($"VR_HAPTICS_CLEAR reason={reason} beforeFirstOutput={pending}");
        pending = false;
        policy.Clear();
        nextOutput = 0;
        try
        {
            if (leftActive) OpenXRControllerActions.StopHaptic(false);
            if (rightActive) OpenXRControllerActions.StopHaptic(true);
        }
        catch (Exception ex)
        {
            Probe.Write("VR_HAPTICS_STOP_ERROR " + ex.Message);
        }
        finally
        {
            leftActive = rightActive = false;
        }
    }

    internal static void Report()
    {
        try
        {
            ulong left = InputTracking.GetDeviceIdAtXRNode(XRNode.LeftHand);
            ulong right = InputTracking.GetDeviceIdAtXRNode(XRNode.RightHand);
            Probe.Write(
                $"VR_HAPTICS_STATUS gate={Gate()} left={left}/{Tracked(XRNode.LeftHand)} right={right}/{Tracked(XRNode.RightHand)} gain={VrStartup.Settings.HapticsStrength:F2} objectScopes={objectScopes} bodyScopes={bodyScopes} received={received} accepted={accepted} outputs={leftOutputs}/{rightOutputs}");
        }
        catch (Exception ex)
        {
            Probe.Write("VR_HAPTICS_STATUS_ERROR " + ex.Message);
        }
    }

    // Explicit diagnostic command only. Never runs on startup or menu clicks.
    // Bypasses native event capture but retains all settings/session/tracking gates.
    internal static void TestOutput()
    {
        try
        {
            string gate = Gate();
            if (gate == "ready" && (!Tracked(XRNode.LeftHand) || !Tracked(XRNode.RightHand)))
                gate = "controller-not-tracked";
            if (gate != "ready")
            {
                Probe.Write("VR_HAPTICS_TEST_REJECTED gate=" + gate);
                return;
            }

            SyncLevel();
            policy.Request(HapticKind.Object, .5f, .35f, Time.realtimeSinceStartupAsDouble);
            pending = true;
            nextOutput = 0;
            Probe.Write("VR_HAPTICS_TEST right strength=0.5 duration=0.35 userGainApplied=true");
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    static void Fail(Exception ex)
    {
        Stop();
        if (!failed) Probe.Write("VR_HAPTICS_DISABLED " + ex);
        failed = true;
    }
}