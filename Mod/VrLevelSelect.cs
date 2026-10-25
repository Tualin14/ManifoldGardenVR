using System.Collections;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ManifoldProbe;

// A test-only native overlay panel. The game's loader owns scene/player setup.
internal static class VrLevelSelect
{
    static Il2Cpp.MenuPanel? panel, previous;
    static TMP_Text? heading, message, notice;
    static TestLevel[] levels = Array.Empty<TestLevel>();
    static readonly List<Button> buttons = new();
    static readonly List<TMP_Text> texts = new();
    static TMP_FontAsset? font;
    static int language;
    static Color ink;
    static int page;
    static bool wasPaused, loading, ready, saveBackupCreated;

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        try
        {
            var method = AccessTools.Method(typeof(Il2Cpp.MenuPanel), "PanelUpdate") ??
                throw new MissingMethodException(nameof(Il2Cpp.MenuPanel), "PanelUpdate");
            harmony.Patch(method, prefix: new HarmonyMethod(AccessTools.Method(typeof(VrLevelSelect), nameof(PanelUpdate))));
            ready = true;
            Probe.Write("VR_LEVEL_SELECT_HOOKS_READY saving=native");
        }
        catch (Exception ex)
        {
            Probe.Write("VR_LEVEL_SELECT_INSTALL_ERROR " + ex);
        }
    }

    static bool PanelUpdate(Il2Cpp.MenuPanel __instance)
    {
        if (panel == null || __instance != panel) return true;
        if (!panel.PanelActive || panel.panelState != Il2Cpp.MenuPanel.PANEL_STATE.active) return false;
        if (Il2Cpp.InputController.GetButtonDown("UI Cancel") ||
            Il2Cpp.InputController.GetButtonDown("UI Return To Game")) Cancel();
        return false;
    }

    internal static void Tick()
    {
        if (!ready) return;
        try
        {
            if (QuestInput.LevelSelectPressed)
            {
                if (panel != null && panel.PanelActive) Cancel();
                else if (!loading && Il2Cpp.GameManager.IsInitialized && !Il2Cpp.LevelLoader.IsOnTitleScreen &&
                         !Il2Cpp.LevelLoader.IsLoadingOrPendingLoad && Probe.HasWorldCamera)
                {
                    var ui = Il2Cpp.GameManager.UI;
                    // Never interrupt settings popups, save/load dialogs or other native transitions.
                    if (!Il2Cpp.GameManager.IsPaused || ui.currentPanel == ui.pausePanelNew &&
                        ui.pausePanelNew.panelState == Il2Cpp.MenuPanel.PANEL_STATE.active)
                        Open();
                }
            }
        }
        catch (Exception ex)
        {
            Probe.Write("VR_LEVEL_SELECT_ERROR " + ex);
            if (panel != null && panel.PanelActive) Cancel();
        }
    }

    static void Open()
    {
        var names = Il2Cpp.LevelLoader.GetFullGameSceneNames();
        var removed = Il2Cpp.LevelLoader.GetRemovedSceneNames();
        var removedNames = new List<string>();
        foreach (var name in removed) removedNames.Add(name);
        levels = LevelCatalog.Build(names, removedNames);
        if (levels.Length == 0) { Probe.Write("VR_LEVEL_CATALOG_EMPTY"); return; }
        File.WriteAllText(Path.Combine(Probe.Output, "test-levels.json"),
            System.Text.Json.JsonSerializer.Serialize(levels, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        page = 0;
        if (panel == null) Build();
        previous = Il2Cpp.GameManager.UI.currentPanel;
        wasPaused = Il2Cpp.GameManager.IsPaused;
        panel!.backPanel = null;
        Refresh();
        if (!wasPaused) Il2Cpp.GameManager.Pause(false);
        if (wasPaused && previous != null) previous.GoToPanel(panel, false);
        else Il2Cpp.GameManager.UI.SetActivePanel(panel, true);
        QuestInput.Reset();
        VrMenu.ResetAnchor();
        Probe.Write($"VR_LEVEL_SELECT_OPEN count={levels.Length}");
    }

    static T Add<T>(GameObject go) where T : Component => go.AddComponent(Il2CppType.Of<T>()).Cast<T>();
    static GameObject Rect(string name, Transform parent, float x0, float y0, float x1, float y1)
    {
        var go = new GameObject(name, new Il2CppReferenceArray<Il2CppSystem.Type>(new[] { Il2CppType.Of<RectTransform>() }));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var rect = go.transform.Cast<RectTransform>();
        rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return go;
    }

    static TMP_Text Text(Transform parent, string value, float x0, float y0, float x1, float y1)
    {
        var go = Rect("Label", parent, x0, y0, x1, y1);
        var text = Add<TextMeshProUGUI>(go);
        text.font = font!; text.fontSharedMaterial = font!.material;
        text.fontSize = 24; text.enableAutoSizing = true; text.fontSizeMin = 12; text.fontSizeMax = 24;
        text.color = ink; text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false; text.richText = false; text.raycastTarget = false;
        text.text = value;
        texts.Add(text);
        return text;
    }

    static Button Button(Transform parent, string value, float x0, float y0, float x1, float y1, Action click)
    {
        var go = Rect("MGVR Level " + value, parent, x0, y0, x1, y1);
        var graphic = Add<Image>(go); graphic.color = Color.white;
        var button = Add<Button>(go); button.targetGraphic = graphic;
        var colors = button.colors;
        colors.normalColor = new Color(1, 1, 1, 0);
        colors.highlightedColor = colors.selectedColor = new Color(0, 0, 0, .15f);
        colors.pressedColor = new Color(0, 0, 0, .25f); button.colors = colors;
        button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(click));
        Text(go.transform, value, .025f, 0, .975f, 1);
        void Edge(Vector2 min, Vector2 max, Vector2 size)
        {
            var line = Rect("Frame", go.transform, min.x, min.y, max.x, max.y);
            var rect = line.transform.Cast<RectTransform>(); rect.pivot = (min + max) * .5f;
            rect.sizeDelta = size; rect.anchoredPosition = Vector2.zero;
            var image = Add<Image>(line); image.color = ink; image.raycastTarget = false;
        }
        Edge(new(0, 1), new(1, 1), new(0, 1)); Edge(new(0, 0), new(1, 0), new(0, 1));
        Edge(new(0, 0), new(0, 1), new(1, 0)); Edge(new(1, 0), new(1, 1), new(1, 0));
        return button;
    }

    static void Build()
    {
        var ui = Il2Cpp.GameManager.UI;
        var template = ui.pausePanelNew;
        font = Il2Cpp.LocalizationManager.Instance.fontSettings.latin.font;
        ink = new Color(.22f, .19f, .15f, 1);
        var root = Rect("MGVR Test Levels", template.transform.parent, 0, 0, 1, 1);
        var background = Add<Image>(root); background.color = new Color(.98f, .91f, .80f, 1);
        Add<CanvasGroup>(root);
        var canvas = Add<Canvas>(root);
        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal |
                                          AdditionalCanvasShaderChannels.Tangent;
        Add<GraphicRaycaster>(root);
        panel = Add<Il2Cpp.MenuPanel>(root);
        panel.canvas = canvas; panel.menuBackground = template.menuBackground;
        panel.makeActiveWhenOpened = true;
        heading = Text(root.transform, "Test Levels", .1f, .86f, .9f, .94f);
        message = Text(root.transform, "", .08f, .81f, .92f, .86f);
        notice = Text(root.transform, "", .08f, .105f, .92f, .205f);
        notice.enableWordWrapping = true;
        notice.fontSize = notice.fontSizeMax = 18;
        notice.fontSizeMin = 14;
        for (int row = 0; row < 6; row++)
        for (int col = 0; col < 3; col++)
        {
            int slot = row * 3 + col;
            float x = .08f + col * .285f, y = .79f - row * .10f;
            buttons.Add(Button(root.transform, "", x, y - .065f, x + .27f, y,
                () => Choose(page * LevelCatalog.PageSize + slot)));
        }
        buttons.Add(Button(root.transform, "Previous", .08f, .035f, .23f, .085f, () => ChangePage(-1)));
        buttons.Add(Button(root.transform, "Next", .27f, .035f, .42f, .085f, () => ChangePage(1)));
        buttons.Add(Button(root.transform, "Back", .77f, .035f, .92f, .085f, Cancel));
        panel.startSelected = buttons[0].gameObject;
        root.SetActive(false);
    }

    static void Refresh()
    {
        var latin = Il2Cpp.LocalizationManager.Instance.fontSettings.latin.font;
        font = VrMenuFont.Resolve((int)Il2Cpp.LocalizationManager.Instance.currentLanguage, latin, out language);
        foreach (var text in texts)
        {
            text.font = font;
            text.fontSharedMaterial = font.material;
            text.isRightToLeftText = false;
        }
        heading!.text = $"{T(VrText.TestLevels)}  {page + 1} / {LevelCatalog.PageCount(levels.Length)}  ({levels.Length})";
        message!.text = T(VrText.LevelPointerHint);
        notice!.text = T(VrText.LevelNotice);
        for (int i = 0; i < 3; i++)
            buttons[LevelCatalog.PageSize + i].GetComponentInChildren(Il2CppType.Of<TextMeshProUGUI>())
                .Cast<TMP_Text>().text = T(i switch { 0 => VrText.Previous, 1 => VrText.Next, _ => VrText.Back });
        var active = new List<Button>();
        for (int i = 0; i < LevelCatalog.PageSize; i++)
        {
            int index = page * LevelCatalog.PageSize + i;
            buttons[i].gameObject.SetActive(index < levels.Length);
            if (index >= levels.Length) continue;
            var label = buttons[i].GetComponentInChildren(Il2CppType.Of<TextMeshProUGUI>()).Cast<TMP_Text>();
            // Internal scene names keep the existing Latin face and spelling.
            label.font = latin;
            label.fontSharedMaterial = latin.material;
            label.text = levels[index].Label;
            active.Add(buttons[i]);
        }
        active.AddRange(buttons.Skip(LevelCatalog.PageSize));
        foreach (var button in active)
            button.navigation = new Navigation { mode = Navigation.Mode.None };
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(active[0].gameObject);
    }

    static string T(VrText key) => VrMenuText.Get(language, key);

    static void ChangePage(int direction)
    {
        if (loading) return;
        page = LevelCatalog.ChangePage(page, direction, levels.Length); Refresh();
    }

    static void Cancel()
    {
        if (loading || panel == null || !panel.PanelActive) return;
        if (wasPaused && previous != null) panel.GoToPanel(previous, false);
        else
        {
            Il2Cpp.GameManager.UI.SetActivePanel(panel, false);
            Il2Cpp.GameManager.Unpause();
        }
        QuestInput.Reset();
        Probe.Write("VR_LEVEL_SELECT_CANCEL");
    }

    static void Choose(int index)
    {
        if (loading || index < 0 || index >= levels.Length) return;
        if (Il2Cpp.LevelLoader.IsLoadingOrPendingLoad || !Il2Cpp.SaveGameManager.HasInstance ||
            Il2Cpp.SaveGameManager.Instance.currentlyMakingGameSave)
        { message!.text = T(VrText.LevelBusy); return; }
        try
        {
            if (!saveBackupCreated)
            {
                string source = Il2Cpp.SaveGameManager.Instance.currentSaveFilePath;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                    throw new IOException("Current save does not exist; load a saved game before testing.");
                string backup = Path.Combine(Probe.Output, "TestSaveBackups", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                Directory.CreateDirectory(backup);
                foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(source)!))
                    File.Copy(file, Path.Combine(backup, Path.GetFileName(file)));
                Probe.Write("VR_TEST_SAVE_BACKUP " + backup);
                saveBackupCreated = true;
            }
            loading = true;
            message!.text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                T(VrText.LoadingLevel), levels[index].Label);
            foreach (var button in buttons) button.interactable = false;
            MelonCoroutines.Start(Load(levels[index].Scene));
        }
        catch (Exception ex)
        {
            loading = false;
            message!.text = T(VrText.LevelStartFailed);
            Probe.Write("VR_TEST_LOAD_ERROR " + ex);
        }
    }

    static IEnumerator Load(string scene)
    {
        // Same lifecycle as DebugPanel's loader: freeze, unload, load, spawn, unpause.
        // Drive the native iterators here so finally restores input even if a loader throws.
        bool success = false;
        try
        {
            Probe.Write("VR_TEST_LOAD_BEGIN scene=" + scene);
            Il2Cpp.GameManager.PlayerCarry?.DropGravityCube();
            Il2Cpp.GameManager.PlayerController.FreezePlayerMovement(true);
            Il2Cpp.GameManager.Instance.MovePlayerToDoNotDestroyScene();
            Il2Cpp.GameManager.Pause(false);
            var loader = Il2Cpp.LevelLoader.Instance;
            // Native Unity coroutines complete their own nested scene and neighbor initialization.
            yield return loader.UnloadAllGameplayLevels();
            Time.timeScale = 1;
            Il2Cpp.LevelLoader.SetLevelLoadingMode(Il2Cpp.LevelLoadingMode.Automatic);
            yield return loader.LoadLevelAndNeighborsThenMovePlayerIn(scene);
            var target = Il2Cpp.LevelLoader.GetLevelSystems(scene);
            if (target == null || Il2Cpp.LevelLoader.CurrentLevelSystems != target)
                throw new InvalidOperationException("Native loader did not make the requested level current: " + scene);
            Il2Cpp.GameManager.Instance.MovePlayerToSpawnPoint();
            Il2Cpp.LevelLoader.SetUpPlayerForGameplayLevel();
            success = true;
            Probe.Write("VR_TEST_LOAD_READY scene=" + scene);
        }
        finally
        {
            Il2Cpp.LevelLoader.SetLevelLoadingMode(Il2Cpp.LevelLoadingMode.Automatic);
            Il2Cpp.GameManager.PlayerController?.FreezePlayerMovement(false);
            loading = false;
            foreach (var button in buttons) if (button != null) button.interactable = true;
            QuestInput.Reset();
            VrMenu.ResetAnchor();
            if (success)
            {
                Il2Cpp.GameManager.UI.SetActivePanel(panel!, false);
                Il2Cpp.GameManager.Unpause();
            }
            else
            {
                message!.text = T(VrText.LevelLoadFailed);
                Probe.Write("VR_TEST_LOAD_FAILED scene=" + scene);
            }
        }
    }
}
