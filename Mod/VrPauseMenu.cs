using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ManifoldProbe;

internal static class VrPauseMenu
{
    static Il2Cpp.PausePanel? owner;
    static Button? levels;
    static TMP_Text? label;
    static TMP_FontAsset? displayFont;
    static Material? displayMaterial;
    static string displayText = "";
    static int language = -1;
    static int blockedFontUpdates, labelRepairs;
    static double retryAt;

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var method = AccessTools.Method(typeof(Il2Cpp.LocalizationManager), "UpdateFont",
                         new[] { typeof(TextMeshProUGUI) }) ??
                     throw new MissingMethodException(nameof(Il2Cpp.LocalizationManager), "UpdateFont");
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(VrPauseMenu), nameof(BeforeFontUpdate)));
        Probe.Write("VR_PAUSE_FONT_GUARD_READY");
    }

    static bool BeforeFontUpdate(TextMeshProUGUI __0)
    {
        // Native global font refreshes include cloned menu controls, even when
        // their LocalizedText component is disabled or ignores translation.
        // MGVR owns only this label; all native labels retain their own policy.
        if (label == null || __0 == null || __0.Pointer != label.Pointer) return true;
        if (blockedFontUpdates++ < 3) Probe.Write("VR_PAUSE_LEVEL_FONT_UPDATE_BLOCKED");
        return false;
    }

    internal static void Tick()
    {
        if (!VrStartup.LaunchedInVr || !Il2Cpp.GameManager.IsInitialized) return;
        var ui = Il2Cpp.GameManager.UI;
        if (ui == null) return;
        var panel = ui.pausePanelNew;
        if (panel == null) return;
        var button = panel.photoModeButton;
        // The native opening coroutine can reactivate this button. Apply after
        // native menu updates and before ray input/rendering, including after
        // a scene change creates a new pause panel. Its slot hosts a separate
        // MGVR button, with its own click event rather than the photo callback.
        if (button != null && button.gameObject.activeSelf)
        {
            button.gameObject.SetActive(false);
            Probe.Write("VR_PHOTO_MODE_BUTTON_HIDDEN");
        }

        if (owner != panel)
        {
            owner = panel;
            levels = null;
            label = null;
            displayFont = null;
            displayMaterial = null;
            language = -1;
            blockedFontUpdates = labelRepairs = 0;
            retryAt = 0;
        }

        if (!panel.PanelActive || panel.panelState != Il2Cpp.MenuPanel.PANEL_STATE.active ||
            Time.realtimeSinceStartupAsDouble < retryAt) return;
        try
        {
            if (levels == null)
            {
                if (button == null) return;
                Build(button);
            }

            levels!.interactable = VrLevelSelect.CanOpenFromPauseMenu;
            int nextLanguage = (int)(Il2Cpp.LocalizationManager.Instance?.currentLanguage ??
                                     Il2Cpp.LocalizationLanguage.English);
            bool languageChanged = language != nextLanguage;
            if (languageChanged || displayFont == null || displayMaterial == null)
            {
                var latin = Il2Cpp.LocalizationManager.Instance?.fontSettings?.latin?.font ?? label!.font;
                displayFont = VrMenuFont.Resolve(nextLanguage, latin, out int displayLanguage);
                displayMaterial = displayFont.material;
                displayText = VrMenuText.Get(displayLanguage, VrText.TestLevels);
                language = nextLanguage;
            }

            // OnEnable and native UI rebuilds can replace the font/material
            // without changing the language. Repair only when actual state drifts.
            if (label!.font != displayFont || label.fontSharedMaterial != displayMaterial ||
                label.text != displayText || label.isRightToLeftText || label.richText)
            {
                label.font = displayFont;
                label.fontSharedMaterial = displayMaterial;
                label.isRightToLeftText = false;
                label.richText = false;
                label.text = displayText;
                if (!languageChanged && labelRepairs++ < 3) Probe.Write("VR_PAUSE_LEVEL_LABEL_RESTORED");
            }
        }
        catch (Exception ex)
        {
            retryAt = Time.realtimeSinceStartupAsDouble + 2;
            Probe.Write("VR_PAUSE_LEVEL_BUTTON_ERROR " + ex);
        }
    }

    static void Build(Button template)
    {
        GameObject? clone = null;
        try
        {
            clone = Object.Instantiate(template.gameObject, template.transform.parent, false).Cast<GameObject>();
            clone.name = "MGVR Test Levels";
            clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            foreach (var component in clone.GetComponentsInChildren(Il2CppType.Of<Il2Cpp.LocalizedText>(), true))
            {
                var localized = component.Cast<Il2Cpp.LocalizedText>();
                localized.ignoreTranslation = true;
                localized.enabled = false;
            }

            var created = clone.GetComponent(Il2CppType.Of<Button>()).Cast<Button>();
            var text = clone.GetComponentInChildren(Il2CppType.Of<TextMeshProUGUI>(), true).Cast<TMP_Text>();
            created.onClick = new Button.ButtonClickedEvent();
            created.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((Action)VrLevelSelect.OpenFromPauseMenu));
            created.navigation = new Navigation { mode = Navigation.Mode.None };
            created.enabled = true;
            clone.SetActive(true);
            levels = created;
            label = text;
            displayFont = null;
            displayMaterial = null;
            language = -1;
            Probe.Write("VR_PAUSE_LEVEL_BUTTON_READY");
        }
        catch
        {
            if (clone != null) Object.Destroy(clone);
            throw;
        }
    }
}
