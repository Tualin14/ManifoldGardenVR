using System.Globalization;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ManifoldProbe;

// A small stereo HUD for the native post-God-Cube hold prompt. Its text renderer
// stays disabled: only these eye command buffers draw it, never the desktop scene.
internal static class VrSequenceProgress
{
    static GameObject? root;
    static TextMeshPro? label;
    static MeshRenderer? textRenderer;
    static TMP_FontAsset? font;
    static Mesh? quad;
    static Material? background, track, fill, textMaterial;
    static int gameLanguage = -1, displayLanguage, preparedFrame = -1;
    static Matrix4x4 panel;
    static SequenceProgress progress;
    static bool reported, errorReported;
    static double retryAt;

    internal static void Prepare()
    {
        preparedFrame = -1;
        if (Time.realtimeSinceStartupAsDouble < retryAt) return;
        try
        {
            if (!QuestInput.AudioVisualContinueActive) return;
            var sequence = Il2Cpp.GameManager.DarkModeController?.CurrentAudioVisualLevel;
            if (sequence == null) return;
            progress = SequenceProgressPolicy.Read(sequence.currentButtonHeldDownTime,
                sequence.fadeOutOfLevelTime, sequence.initialWaitTime);
            if (!progress.Visible) return;
            var camera = VrCamera.Current;
            if (camera == null || !OpenXRRenderer.TryGameplayPose(camera, out var head, out var rotation)) return;

            EnsureGraphics();
            var button = VrStartup.Settings.InteractBinding switch
            {
                VrButton.LeftTrigger => VrMenuText.Get(displayLanguage, VrText.LeftTrigger),
                VrButton.LeftGrip => VrMenuText.Get(displayLanguage, VrText.LeftGrip),
                VrButton.RightTrigger => VrMenuText.Get(displayLanguage, VrText.RightTrigger),
                VrButton.RightGrip => VrMenuText.Get(displayLanguage, VrText.RightGrip),
                VrButton.A => "A",
                VrButton.B => "B",
                _ => "X"
            };
            string instruction = string.Format(CultureInfo.InvariantCulture,
                VrMenuText.Get(displayLanguage, VrText.HoldInput), button);
            string caption = string.Format(CultureInfo.InvariantCulture, "{0}  {1:0.0} {2}",
                instruction, progress.RemainingSeconds, VrMenuText.Get(displayLanguage, VrText.PerSecond));
            bool repair = label!.font != font || label.fontSharedMaterial != textMaterial ||
                          label.isRightToLeftText || label.richText;
            if (repair || label.text != caption || label.mesh == null || label.mesh.vertexCount == 0)
            {
                label.font = font!;
                label.fontSharedMaterial = textMaterial!;
                label.isRightToLeftText = false;
                label.richText = false;
                label.text = caption;
                label.ForceMeshUpdate(true, false);
            }
            textRenderer!.enabled = false;
            if (label.mesh == null || label.mesh.vertexCount == 0)
                throw new InvalidOperationException("Sequence prompt text mesh is empty");
            label.mesh.hideFlags = HideFlags.HideAndDontSave;
            panel = Matrix4x4.TRS(head + rotation * new Vector3(0, -.21f, 1.1f), rotation, Vector3.one);
            preparedFrame = Time.frameCount;
            if (!reported)
            {
                reported = true;
                Probe.Write("VR_AV_PROGRESS_READY source=native-timer display=bar-and-seconds");
            }
            if (errorReported)
            {
                errorReported = false;
                Probe.Write("VR_AV_PROGRESS_RECOVERED");
            }
        }
        catch (Exception ex) { Retry("PREPARE", ex); }
    }

    internal static void Render(ScriptableRenderContext context, Camera view, RenderTexture target)
    {
        if (preparedFrame != Time.frameCount) return;
        CommandBuffer? command = null;
        try
        {
            command = new CommandBuffer { name = "MGVR sequence progress eye" };
            command.SetRenderTarget(new RenderTargetIdentifier(target));
            command.SetViewport(new Rect(0, 0, target.width, target.height));
            command.SetViewProjectionMatrices(view.worldToCameraMatrix,
                GL.GetGPUProjectionMatrix(view.projectionMatrix, false));
            command.DrawMesh(quad!, Local(0, 0, .62f, .13f), background!, 0, 0);
            command.DrawMesh(quad!, Local(0, -.035f, .52f, .014f), track!, 0, 0);
            float width = .512f * progress.Fraction;
            if (width > 0)
                command.DrawMesh(quad!, Local(-.256f + width / 2, -.035f, width, .008f), fill!, 0, 0);
            command.DrawMesh(label!.mesh, Local(0, .017f, .001f, .001f), textMaterial!, 0, 0);
            context.ExecuteCommandBuffer(command);
            context.Submit();
        }
        catch (Exception ex) { Retry("RENDER", ex); }
        finally { command?.Release(); }
    }

    static Matrix4x4 Local(float x, float y, float width, float height) =>
        panel * Matrix4x4.TRS(new Vector3(x, y, 0), Quaternion.identity, new Vector3(width, height, 1));

    static Material PanelMaterial(string name, Color color)
    {
        var shader = Shader.Find("UI/Default") ?? throw new InvalidOperationException("HUD shader unavailable");
        var material = new Material(shader) { name = name, color = color, hideFlags = HideFlags.HideAndDontSave };
        material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        return material;
    }

    static void EnsureGraphics()
    {
        if (root == null || label == null || textRenderer == null || quad == null ||
            background == null || track == null || fill == null)
        {
            DestroyGraphics();
            root = new GameObject("MGVR sequence progress",
                new Il2CppReferenceArray<Il2CppSystem.Type>(new[] { Il2CppType.Of<RectTransform>() }))
                { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(root);
            label = root.AddComponent(Il2CppType.Of<TextMeshPro>()).Cast<TextMeshPro>();
            textRenderer = root.GetComponent(Il2CppType.Of<MeshRenderer>()).Cast<MeshRenderer>();
            textRenderer.enabled = false;
            label.rectTransform.sizeDelta = new Vector2(560, 60);
            label.rectTransform.pivot = new Vector2(.5f, .5f);
            label.fontSize = 28;
            label.isOrthographic = true;
            label.enableAutoSizing = false;
            label.enableWordWrapping = false;
            label.alignment = TextAlignmentOptions.Midline;
            label.color = new Color(.98f, .93f, .83f, 1);

            quad = new Mesh { name = "MGVR progress quad", hideFlags = HideFlags.HideAndDontSave };
            quad.vertices = new Vector3[] { new(-.5f, -.5f, 0), new(-.5f, .5f, 0),
                new(.5f, .5f, 0), new(.5f, -.5f, 0) };
            quad.uv = new Vector2[] { new(0, 0), new(0, 1), new(1, 1), new(1, 0) };
            quad.colors = Enumerable.Repeat(Color.white, 4).ToArray();
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            quad.RecalculateBounds();
            background = PanelMaterial("MGVR progress background", new Color(.07f, .065f, .05f, .94f));
            track = PanelMaterial("MGVR progress track", new Color(.4f, .37f, .31f, 1));
            fill = PanelMaterial("MGVR progress fill", new Color(.95f, .8f, .51f, 1));
        }

        int nextLanguage = (int)(Il2Cpp.LocalizationManager.Instance?.currentLanguage ??
                                 Il2Cpp.LocalizationLanguage.English);
        if (gameLanguage == nextLanguage && font != null && textMaterial != null) return;
        var latin = Il2Cpp.LocalizationManager.Instance?.fontSettings?.latin?.font ??
                    throw new InvalidOperationException("Game Latin font is not ready");
        font = VrMenuFont.Resolve(nextLanguage, latin, out displayLanguage);
        if (textMaterial != null) Object.Destroy(textMaterial);
        textMaterial = new Material(font.material) { name = "MGVR progress text", hideFlags = HideFlags.HideAndDontSave };
        textMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        if (textMaterial.HasProperty("_ZTestMode")) textMaterial.SetInt("_ZTestMode", (int)CompareFunction.Always);
        gameLanguage = nextLanguage;
    }

    static void Retry(string stage, Exception ex)
    {
        preparedFrame = -1;
        DestroyGraphics();
        retryAt = Time.realtimeSinceStartupAsDouble + 1;
        if (errorReported) return;
        errorReported = true;
        Probe.Write("VR_AV_PROGRESS_" + stage + "_ERROR retry=1s " + ex);
    }

    static void DestroyGraphics()
    {
        if (root != null) Object.Destroy(root);
        if (quad != null) Object.Destroy(quad);
        foreach (var material in new[] { background, track, fill, textMaterial })
            if (material != null) Object.Destroy(material);
        root = null;
        label = null;
        textRenderer = null;
        quad = null;
        font = null;
        background = track = fill = textMaterial = null;
        gameLanguage = -1;
    }

    internal static void Release()
    {
        preparedFrame = -1;
        DestroyGraphics();
        reported = errorReported = false;
        retryAt = 0;
    }
}
