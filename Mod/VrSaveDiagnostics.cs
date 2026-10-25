using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ManifoldProbe;

// Read-only save tracing. Do not bypass native eligibility, invoke saving, alter
// coroutines/results, or change screenshot/render state while diagnosing failure.
internal static class VrSaveDiagnostics
{
    static bool installed;
    static double nextSample;
    static string previous = "";
    static IntPtr lastSaveIterator, lastScreenshotIterator;
    static int lastSaveStep = int.MinValue, lastScreenshotStep = int.MinValue;

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var patches = new List<(MethodInfo method, MethodInfo callback)>();
        try
        {
            void Patch(Type type, string name, string callback, bool postfix = false, Type[]? args = null)
            {
                var original = AccessTools.Method(type, name, args) ??
                               throw new MissingMethodException(type.FullName, name);
                // The native->managed bridge for WriteSaveNow(bool, ref float, bool)
                // throws before its prefix in this game. Never instrument by-ref
                // save entry points; observe the no-ref overload and iterator instead.
                if (original.GetParameters().Any(p => p.ParameterType.IsByRef))
                    throw new NotSupportedException("Save tracing must not intercept by-reference parameters.");
                var trace = AccessTools.Method(typeof(VrSaveDiagnostics), callback);
                patches.Add((original, trace));
                harmony.Patch(original, prefix: postfix ? null : new HarmonyMethod(trace),
                    postfix: postfix ? new HarmonyMethod(trace) : null);
            }

            Patch(typeof(Il2Cpp.PausePanel), "OpenSaveConfirmPopUpWindow", nameof(SaveClick));
            Patch(typeof(Il2Cpp.SaveGameManager), "WriteSaveNow", nameof(SaveRequest),
                args: new[] { typeof(bool), typeof(bool) });
            Patch(typeof(Il2Cpp.SaveGameManager), "SerializeSaveSlotWriteToDisk", nameof(SerializeRequest));
            Patch(typeof(Il2Cpp.MobiusPost), "CaptureScreenShot", nameof(ScreenshotRequest));

            Type Iterator(Type owner, string routine) => owner
                .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Single(t => t.Name.StartsWith("_" + routine + "_d__", StringComparison.Ordinal));

            Patch(Iterator(typeof(Il2Cpp.SaveGameManager), "WriteSaveNowRoutine"), "MoveNext", nameof(SaveStep), true);
            Patch(Iterator(typeof(Il2Cpp.MobiusPost), "SaveScreenshot"), "MoveNext", nameof(ScreenshotStep), true);
            installed = true;
            Probe.Write("VR_SAVE_DIAGNOSTICS_READY mode=observe-only hooks=" + patches.Count);
        }
        catch (Exception ex)
        {
            foreach (var patch in patches) harmony.Unpatch(patch.method, patch.callback);
            Probe.Write("VR_SAVE_DIAGNOSTICS_INSTALL_ERROR " + ex);
        }
    }

    static void SaveClick() => Event("pause-save-click");
    static void SaveRequest(bool __0) => Event("save-request dialogs=" + __0);
    static void SerializeRequest(int __0) => Event("serialize-request slot=" + __0);
    static void ScreenshotRequest() => Event("screenshot-request");

    static void SaveStep(Il2Cpp.SaveGameManager._WriteSaveNowRoutine_d__68 __instance, bool __result)
    {
        try
        {
            int step = __instance.__1__state;
            if (__instance.Pointer == lastSaveIterator && step == lastSaveStep && __result) return;
            lastSaveIterator = __instance.Pointer;
            lastSaveStep = step;
            Event($"save-coroutine step={step} running={__result} waitCount={__instance._whileCheck_5__2}");
        }
        catch (Exception ex)
        {
            Probe.Write("VR_SAVE_DIAGNOSTICS_ERROR save-step=" + ex.Message);
        }
    }

    static void ScreenshotStep(Il2Cpp.MobiusPost._SaveScreenshot_d__109 __instance, bool __result)
    {
        try
        {
            int step = __instance.__1__state;
            if (__instance.Pointer == lastScreenshotIterator && step == lastScreenshotStep && __result) return;
            lastScreenshotIterator = __instance.Pointer;
            lastScreenshotStep = step;
            Event($"screenshot-coroutine step={step} running={__result}");
        }
        catch (Exception ex)
        {
            Probe.Write("VR_SAVE_DIAGNOSTICS_ERROR screenshot-step=" + ex.Message);
        }
    }

    static string State()
    {
        if (!Il2Cpp.GameManager.IsInitialized || !Il2Cpp.SaveGameManager.HasInstance) return "not-initialized";
        var save = Il2Cpp.SaveGameManager.Instance;
        var player = Il2Cpp.GameManager.PlayerController;
        var dark = Il2Cpp.GameManager.DarkModeController;
        var mandala = Il2Cpp.GameManager.MandalaManager;
        var ui = Il2Cpp.GameManager.UI;
        var panel = ui?.pausePanelNew;
        var button = panel?.saveGameButton;
        var post = Il2Cpp.GameManager.Post;
        return $"scene={Il2Cpp.LevelLoader.CurrentLevelSystems?.gameObject.scene.name ?? "none"} " +
               $"canSave={Il2Cpp.SaveGameManager.CanSave} inGame={Il2Cpp.GameManager.PlayerIsInGame} " +
               $"ending={Il2Cpp.GameManager.isInGameEnd} dark={dark?.IsDarkMode} darkTransition={dark?.isTransitioningToDarkMode} " +
               $"avExit={dark?.currentlyTransitioningOutOfAudioVisualLevel} avLevel={dark != null && dark.CurrentAudioVisualLevel != null} " +
               $"gravityRoutine={player != null && player.rotateShiftRoutine != null} fastTravel={mandala?.IsFastTravelling} " +
               $"saving={save.currentlyMakingGameSave} lastSuccess={save.lastSaveSuccessful} autoSave={save.AutoSaveEnabled} " +
               $"slot={save.currentSaveSlotIndexZero} paused={Il2Cpp.GameManager.IsPaused} " +
               $"panel={ui?.currentPanel?.name ?? "none"} panelState={ui?.currentPanel?.panelState} " +
               $"saveVisible={button != null && button.gameObject.activeInHierarchy} saveEnabled={button?.enabled} " +
               $"saveInteractable={button?.IsInteractable()} listeners={button?.onClick.GetPersistentEventCount()} " +
               $"postEnabled={post?.enabled} screenshotPending={post != null && post.m_screenShotInfo != null}";
    }

    static void Event(string name)
    {
        try
        {
            Probe.Write("VR_SAVE_EVENT " + name + " " + State());
            if (!Il2Cpp.SaveGameManager.HasInstance) return;
            var file = new FileInfo(Il2Cpp.SaveGameManager.Instance.currentSaveFilePath);
            Probe.Write($"VR_SAVE_FILE name={file.Name} exists={file.Exists} " +
                        (file.Exists ? $"bytes={file.Length} modifiedUtc={file.LastWriteTimeUtc:O}" : ""));
        }
        catch (Exception ex)
        {
            Probe.Write("VR_SAVE_DIAGNOSTICS_ERROR event=" + name + " " + ex);
        }
    }

    internal static void Tick()
    {
        if (!installed || Time.realtimeSinceStartupAsDouble < nextSample) return;
        nextSample = Time.realtimeSinceStartupAsDouble + .5;
        try
        {
            if (!Il2Cpp.GameManager.IsInitialized || Il2Cpp.LevelLoader.IsOnTitleScreen ||
                Il2Cpp.LevelLoader.IsLoadingOrPendingLoad || !Il2Cpp.SaveGameManager.HasInstance) return;
            string state = State();
            if (state == previous) return;
            previous = state;
            Probe.Write("VR_SAVE_STATE " + state);
        }
        catch (Exception ex)
        {
            string error = ex.GetType().Name + ": " + ex.Message;
            if (previous != error) Probe.Write("VR_SAVE_DIAGNOSTICS_ERROR " + error);
            previous = error;
        }
    }

    internal static void RayPress(GameObject? target, GameObject? click, GameObject? down)
    {
        if (!installed) return;
        try
        {
            var ui = Il2Cpp.GameManager.UI;
            if (ui == null || ui.currentPanel != ui.pausePanelNew) return;
            Probe.Write($"VR_SAVE_RAY_PRESS target={target?.name ?? "none"} " +
                        $"click={click?.name ?? "none"} down={down?.name ?? "none"}");
            Event("pause-pointer-press");
        }
        catch (Exception ex)
        {
            Probe.Write("VR_SAVE_DIAGNOSTICS_ERROR ray=" + ex.Message);
        }
    }
}