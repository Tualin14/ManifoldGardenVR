using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ManifoldProbe;

// Native uGUI controls share the game's overlay, so they appear in both eyes through VrMenu.
internal static partial class VrSettingsMenu
{
    sealed class Page
    {
        internal Il2Cpp.SettingsPanel Owner = null!;
        internal Il2Cpp.MenuSubPanel Panel = null!;
        internal Il2Cpp.MgTabBarButton Tab = null!;
        internal TMP_Text FontSource = null!;
        internal readonly List<TMP_Text> Texts = new(), Titles = new();
        internal TMP_Text FixedControls = null!, ResetLabel = null!, BackLabel = null!;
        internal RawImage Guide = null!;
        internal int Language = -1;
        internal int GameLanguage = -1;
        internal TMP_FontAsset DisplayFont = null!;
        internal bool? Saved;
        internal readonly List<Button> Rows = new();
        internal readonly List<TMP_Text> Values = new();
        internal TMP_Text Message = null!;
        internal Button Reset = null!, Back = null!;
        internal int LayoutFrame, LayoutAttempts;
        internal bool LayoutReady;
    }

    static readonly Dictionary<int, Page> pages = new();
    static readonly float[] angles = { 15, 30, 45, 60, 90 };
    static readonly float[] speeds = { 30, 45, 60, 90, 120, 180, 240 };

    static readonly VrText[] titles =
    {
        VrText.TurnMode, VrText.Angle, VrText.Speed
    };

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        void Patch(Type type, string method, string callback, bool prefix = false)
        {
            var original = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.Name, method);
            var patch = new HarmonyMethod(AccessTools.Method(typeof(VrSettingsMenu), callback));
            harmony.Patch(original, prefix: prefix ? patch : null, postfix: prefix ? null : patch);
        }

        Patch(typeof(Il2Cpp.SettingsPanel), "Start", nameof(Build));
        Patch(typeof(Il2Cpp.SettingsPanel), "SelectTab", nameof(SelectTab), true);
        Patch(typeof(Il2Cpp.SettingsPanelBase), "get_TotalTabCount", nameof(TabCount));
        Probe.Write("VR_SETTINGS_HOOKS_READY");
    }

    static T Add<T>(GameObject go) where T : Component => go.AddComponent(Il2CppType.Of<T>()).Cast<T>();

    static GameObject RectObject(string name, Transform parent)
    {
        var go = new GameObject(name,
            new Il2CppReferenceArray<Il2CppSystem.Type>(new[] { Il2CppType.Of<RectTransform>() }));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return go;
    }

    static void Place(GameObject go, float x0, float y0, float x1, float y1)
    {
        var rect = go.transform.Cast<RectTransform>();
        rect.anchorMin = new Vector2(x0, y0);
        rect.anchorMax = new Vector2(x1, y1);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    static TMP_Text Label(Page page, Transform parent, string name, string value,
        float x0, float y0, float x1, float y1, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var go = RectObject(name, parent);
        Place(go, x0, y0, x1, y1);
        var text = Add<TextMeshProUGUI>(go);
        text.font = page.FontSource.font;
        text.fontSharedMaterial = page.FontSource.fontSharedMaterial;
        text.fontSize = page.FontSource.fontSize;
        text.characterSpacing = page.FontSource.characterSpacing;
        text.lineSpacing = page.FontSource.lineSpacing;
        text.enableAutoSizing = true;
        text.fontSizeMin = page.FontSource.fontSize * .65f;
        text.fontSizeMax = page.FontSource.fontSize;
        text.color = page.Owner.normalTabFontColor;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        text.text = value;
        page.Texts.Add(text);
        return text;
    }

    static Button Button(Page page, Transform parent, string name, string value,
        float x0, float y0, float x1, float y1, Action click, bool framed = true)
    {
        var go = RectObject(name, parent);
        Place(go, x0, y0, x1, y1);
        var image = Add<Image>(go);
        var template = page.Owner.controlsButton.targetGraphic.TryCast<Image>();
        if (template != null)
        {
            image.sprite = template.sprite;
            image.type = template.type;
            image.material = template.material;
        }

        image.color = Color.white;
        var button = Add<Button>(go);
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = button.colors;
        colors.normalColor = new Color(1, 1, 1, 0);
        colors.highlightedColor = new Color(0, 0, 0, .08f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0, 0, 0, .18f);
        button.colors = colors;
        button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(click));
        Label(page, go.transform, "Label", value, .04f, 0, .96f, 1, TextAlignmentOptions.Center);

        if (framed) Frame(page, go.transform);
        return button;
    }

    static void Frame(Page page, Transform parent)
    {
        // Canvas units keep the border equally thick on every edge.
        void Edge(string edge, Vector2 min, Vector2 max, Vector2 size)
        {
            var line = RectObject(edge, parent);
            var rect = line.transform.Cast<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = (min + max) * .5f;
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            var stroke = Add<Image>(line);
            stroke.color = page.Owner.normalTabFontColor;
            stroke.raycastTarget = false;
        }

        Edge("Top", new Vector2(0, 1), Vector2.one, new Vector2(0, 1));
        Edge("Bottom", Vector2.zero, new Vector2(1, 0), new Vector2(0, 1));
        Edge("Left", Vector2.zero, new Vector2(0, 1), new Vector2(1, 0));
        Edge("Right", new Vector2(1, 0), Vector2.one, new Vector2(1, 0));
    }

    static void Arrow(Page page, Button row, int index, bool right)
    {
        var native = page.Owner.gameplaySubPanel.Cast<Il2Cpp.SettingsGameplaySubPanel>().showReticleButton;
        var template = (right ? native.rightButton : native.leftButton).targetGraphic.Cast<Image>();
        var go = RectObject(right ? "Next" : "Previous", row.transform);
        Place(go, right ? .88f : 0, 0, right ? 1 : .12f, 1);
        var hit = Add<Image>(go);
        hit.color = new Color(1, 1, 1, 0);
        var button = Add<Button>(go);
        button.targetGraphic = hit;
        var nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;
        button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((Action)(() =>
        {
            Change(page, index, right ? 1 : -1);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(row.gameObject);
        })));
        var icon = RectObject("Arrow", go.transform);
        var rect = icon.transform.Cast<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        var nativeRect = template.transform.Cast<RectTransform>();
        rect.sizeDelta = nativeRect.sizeDelta;
        rect.localRotation = nativeRect.localRotation;
        rect.localScale = nativeRect.localScale;
        var image = Add<Image>(icon);
        image.sprite = template.sprite;
        image.type = template.type;
        image.material = template.material;
        image.color = page.Owner.normalTabFontColor;
        image.raycastTarget = false;
    }

    static void Build(Il2Cpp.SettingsPanel __instance)
    {
        if (pages.ContainsKey(__instance.GetInstanceID())) return;
        Page? page = null;
        try
        {
            var source = __instance.controlsButton.Cast<Il2Cpp.MgTabBarButton>();
            var nativeRow = __instance.gameplaySubPanel.Cast<Il2Cpp.SettingsGameplaySubPanel>().showReticleButton;
            page = new Page
                { Owner = __instance, FontSource = nativeRow.button_Value ?? nativeRow.captionText ?? source.text };
            var root = RectObject("MGVR Settings", __instance.controlsSubPanel.transform.parent);
            root.SetActive(false);
            var rect = root.transform.Cast<RectTransform>();
            var original = __instance.controlsSubPanel.transform.Cast<RectTransform>();
            rect.anchorMin = original.anchorMin;
            rect.anchorMax = original.anchorMax;
            rect.pivot = original.pivot;
            rect.sizeDelta = original.sizeDelta;
            rect.anchoredPosition = original.anchoredPosition;
            Add<CanvasGroup>(root);
            page.Panel = Add<Il2Cpp.MenuSubPanel>(root);
            page.Panel.settingsPanel = __instance;
            page.Panel.canvas = Add<Canvas>(root);
            Add<GraphicRaycaster>(root);
            page.Panel.canvas.additionalShaderChannels =
                AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal |
                AdditionalCanvasShaderChannels.Tangent;

            var captured = page;
            var clone = Object.Instantiate(source.gameObject, source.transform.parent, false).Cast<GameObject>();
            clone.name = "MGVR Settings Tab";
            page.Tab = clone.GetComponent(Il2CppType.Of<Il2Cpp.MgTabBarButton>()).Cast<Il2Cpp.MgTabBarButton>();
            page.Tab.onClick = new Button.ButtonClickedEvent();
            page.Tab.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((Action)(() => Open(captured))));

            for (int row = 0; row < titles.Length; row++)
            {
                int index = row;
                float top = .805f - row * .053f;
                page.Titles.Add(Label(page, root.transform, "Title " + row, "", .16f, top - .035f, .39f, top));
                var button = Button(page, root.transform, "VR Option " + row, "", .42f,
                    top - .035f, .58f, top,
                    () => Change(captured, index, 1), false);
                page.Rows.Add(button);
                page.Values.Add(button.GetComponentInChildren(Il2CppType.Of<TextMeshProUGUI>()).Cast<TMP_Text>());
                Arrow(page, button, index, false);
                Arrow(page, button, index, true);
            }

            var rule = RectObject("Section divider", root.transform);
            Place(rule, .155f, .65f, .85f, .65f);
            rule.transform.Cast<RectTransform>().sizeDelta = new Vector2(0, 1);
            var ruleImage = Add<Image>(rule);
            ruleImage.color = page.Owner.normalTabFontColor;
            ruleImage.raycastTarget = false;

            page.FixedControls = Label(page, root.transform, "Fixed controls", "",
                .16f, .15f, .85f, .63f);
            page.FixedControls.alignment = TextAlignmentOptions.TopLeft;
            page.FixedControls.enableWordWrapping = true;
            var guideArea = RectObject("Controls guide area", root.transform);
            // One wide guide replaces the text table. Preserve its aspect ratio
            // within the whole lower section, clear of status and bottom buttons.
            Place(guideArea, .155f, .14f, .85f, .64f);
            var guide = RectObject("Localized controls guide", guideArea.transform);
            page.Guide = Add<RawImage>(guide);
            page.Guide.raycastTarget = false;
            var fit = Add<AspectRatioFitter>(guide);
            fit.aspectRatio = 1;
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            page.Message = Label(page, root.transform, "Status", "",
                .16f, .105f, .85f, .13f);
            page.Reset = Button(page, root.transform, "VR Reset", "Reset", .155f, .04f, .2175f, .08f,
                () =>
                {
                    VrBindings.ResetTurning(VrStartup.Settings);
                    Save(captured);
                });
            page.Back = Button(page, root.transform, "VR Back", "Back", .7875f, .04f, .85f, .08f,
                () => __instance.GoBackButCheckIfResolutionApplied());
            page.ResetLabel = page.Reset.GetComponentInChildren(Il2CppType.Of<TextMeshProUGUI>()).Cast<TMP_Text>();
            page.BackLabel = page.Back.GetComponentInChildren(Il2CppType.Of<TextMeshProUGUI>()).Cast<TMP_Text>();
            page.Panel.startSelected = page.Rows[0].gameObject;
            root.SetActive(true);
            page.Panel.CloseNow();
            Refresh(page);
            var tabs = new Il2CppReferenceArray<Button>(5);
            for (int i = 0; i < 4; i++) tabs[i] = __instance.tabButtons[i];
            tabs[4] = page.Tab;
            __instance.tabButtons = tabs;
            page.LayoutFrame = Time.frameCount + 2;
            pages[__instance.GetInstanceID()] = page;
            Probe.Write(
                $"VR_SETTINGS_PAGE_READY tabs=5 rows={page.Rows.Count} guide=wideImage bindings=fixed panel={root.name} font={page.FontSource.font.name}");
        }
        catch (Exception ex)
        {
            if (page?.Panel != null) Object.Destroy(page.Panel.gameObject);
            if (page?.Tab != null) Object.Destroy(page.Tab.gameObject);
            Probe.Write("VR_SETTINGS_BUILD_ERROR " + ex);
        }
    }

    static void LayoutTabs(Il2CppReferenceArray<Button> tabs)
    {
        var parent = tabs[0].transform.parent.Cast<RectTransform>();
        float min = float.MaxValue, max = float.MinValue, y = 0, height = 0;
        var corners = new Il2CppStructArray<Vector3>(4);
        for (int i = 0; i < 4; i++)
        {
            var rect = tabs[i].transform.Cast<RectTransform>();
            rect.GetWorldCorners(corners);
            min = Math.Min(min, parent.InverseTransformPoint(corners[0]).x);
            max = Math.Max(max, parent.InverseTransformPoint(corners[2]).x);
            if (i == 0)
            {
                y = parent.InverseTransformPoint(rect.TransformPoint(rect.rect.center)).y;
                height = rect.rect.height;
            }
        }

        float width = (max - min) / 5;
        if (!float.IsFinite(width) || width < 20 || height < 5)
            throw new InvalidOperationException("Settings tab geometry is not ready");
        var layout = parent.GetComponent(Il2CppType.Of<HorizontalLayoutGroup>())?.TryCast<HorizontalLayoutGroup>();
        if (layout != null) layout.enabled = false;
        for (int i = 0; i < 5; i++)
        {
            var rect = tabs[i].transform.Cast<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(min + width * (i + .5f) - parent.rect.center.x,
                y - parent.rect.center.y);
            var nav = tabs[i].navigation;
            nav.mode = Navigation.Mode.None;
            tabs[i].navigation = nav;
        }

        Probe.Write($"VR_SETTINGS_TAB_LAYOUT count=5 width={width:0.0}");
    }

    internal static void Tick()
    {
        foreach (var key in pages.Where(p => p.Value.Owner == null).Select(p => p.Key).ToArray())
        {
            var page = pages[key];
            if (page.Panel != null) Object.Destroy(page.Panel.gameObject);
            if (page.Tab != null) Object.Destroy(page.Tab.gameObject);
            pages.Remove(key);
        }

        foreach (var page in pages.Values)
        {
            if (!page.LayoutReady && page.Owner.PanelActive && Time.frameCount >= page.LayoutFrame &&
                page.LayoutAttempts < 3)
            {
                try
                {
                    Canvas.ForceUpdateCanvases();
                    LayoutTabs(page.Owner.tabButtons);
                    page.LayoutReady = true;
                }
                catch (Exception ex)
                {
                    page.LayoutAttempts++;
                    page.LayoutFrame = Time.frameCount + 2;
                    Probe.Write("VR_SETTINGS_TAB_LAYOUT_RETRY " + ex.Message);
                }
            }

            if (page.GameLanguage != GameLanguage || page.Tab.text.font != page.DisplayFont)
                Refresh(page);
            page.Tab.text.text = VrMenuText.Get(page.Language, VrText.Tab);
            if (page.Panel.SubPanelActive && page.Owner.PanelActive)
            {
                page.Tab.SetButtonIsSelected();
                page.Tab.targetGraphic.color = page.Owner.selectedTabButtonColor;
                page.Tab.text.color = page.Owner.selectedTabFontColor;
            }
        }
    }

    static void Open(Page page)
    {
        var owner = page.Owner;
        owner.SetAllTabButtonsToNormalState();
        owner.currentTabIndex = 4;
        page.Tab.SetButtonIsSelected();
        page.Tab.targetGraphic.color = owner.selectedTabButtonColor;
        page.Tab.text.color = owner.selectedTabFontColor;
        Refresh(page);
        owner.GoToSubPanelNow(page.Panel);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(page.Rows[0].gameObject);
        Probe.Write("VR_SETTINGS_OPEN tab=4");
    }

    static bool SelectTab(Il2Cpp.SettingsPanel __instance, int __0, int __1)
    {
        if (__0 != 4 || !pages.TryGetValue(__instance.GetInstanceID(), out var page)) return true;
        Open(page);
        return false;
    }

    static void TabCount(Il2Cpp.SettingsPanelBase __instance, ref int __result)
    {
        if (pages.ContainsKey(__instance.GetInstanceID())) __result = 5;
    }

    static float Cycle(float[] values, float current, int direction)
    {
        int nearest = 0;
        for (int i = 1; i < values.Length; i++)
            if (Math.Abs(values[i] - current) < Math.Abs(values[nearest] - current))
                nearest = i;
        return values[(nearest + direction + values.Length) % values.Length];
    }

    static void Change(Page page, int row, int direction)
    {
        var settings = VrStartup.Settings;
        if (row == 0) settings.TurnMode = settings.TurnMode == VrTurnMode.Snap ? VrTurnMode.Smooth : VrTurnMode.Snap;
        else if (row == 1) settings.SnapTurnDegrees = Cycle(angles, settings.SnapTurnDegrees, direction);
        else if (row == 2)
            settings.SmoothTurnDegreesPerSecond = Cycle(speeds, settings.SmoothTurnDegreesPerSecond, direction);
        else return;

        Save(page);
    }

    static void Save(Page page)
    {
        page.Saved = VrStartup.SaveInputSettings();
        Refresh(page);
    }

    static int GameLanguage =>
        (int)(Il2Cpp.LocalizationManager.Instance?.currentLanguage ?? Il2Cpp.LocalizationLanguage.English);

    static int CurrentLanguage => VrMenuText.Language(GameLanguage);

    static void Refresh(Page page)
    {
        var settings = VrStartup.Settings;
        page.Language = CurrentLanguage;
        page.GameLanguage = GameLanguage;
        string T(VrText key) => VrMenuText.Get(page.Language, key);
        var font = Il2Cpp.LocalizationManager.Instance?.fontSettings?.latin?.font ?? page.FontSource.font;
        page.DisplayFont = font;
        foreach (var text in page.Texts.Append(page.Tab.text))
        {
            text.font = font;
            text.fontSharedMaterial = font.material;
            text.isRightToLeftText = false;
        }

        for (int i = 0; i < titles.Length; i++) page.Titles[i].text = T(titles[i]);
        page.Tab.text.text = T(VrText.Tab);
        page.FixedControls.richText = false;
        page.FixedControls.text = T(VrText.FixedControls);
        page.Guide.texture = VrGuideImage.Get(page.GameLanguage);
        if (page.Guide.texture != null)
            page.Guide.GetComponent(Il2CppType.Of<AspectRatioFitter>()).Cast<AspectRatioFitter>().aspectRatio =
                (float)page.Guide.texture.width / page.Guide.texture.height;
        page.Guide.gameObject.SetActive(page.Guide.texture != null);
        page.FixedControls.gameObject.SetActive(page.Guide.texture == null);
        page.ResetLabel.text = T(VrText.Reset);
        page.BackLabel.text = T(VrText.Back);
        page.Message.text = T(page.Saved switch { true => VrText.Saved, false => VrText.SaveFailed, _ => VrText.Hint }) +
                            "  Test build: X opens level selection.";
        page.Values[0].text = T(settings.TurnMode == VrTurnMode.Snap ? VrText.Snap : VrText.Smooth);
        page.Values[1].text = $"{settings.SnapTurnDegrees:0}°";
        page.Values[2].text = $"{settings.SmoothTurnDegreesPerSecond:0}° / {T(VrText.PerSecond)}";
    }

    internal static void Report(bool open = false)
    {
        foreach (var page in pages.Values.Where(p => p.Owner != null))
        {
            Probe.Write(
                $"VR_SETTINGS_STATUS active={page.Owner.PanelActive} tab={page.Owner.CurrentTabIndex} count={page.Owner.TotalTabCount} vrActive={page.Panel.SubPanelActive}");
            if (open && page.Owner.PanelActive) Open(page);
        }
    }
}
