using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ManifoldProbe;

// Capture the existing overlay at desktop resolution, then draw that same
// surface with each eye's view/projection. The desktop canvas stays intact.
internal static class VrMenu
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void DrawOverlayFn(ref ScriptableRenderContext context, IntPtr camera);

    static DrawOverlayFn? drawOverlay;
    static Camera? uiCamera;
    static RenderTexture? texture;
    static Mesh? quad;
    static Material? material;
    static bool anchored;
    static Matrix4x4 panelMatrix;
    static double nextReport;

    internal static Camera FallbackCamera
    {
        get
        {
            if (uiCamera != null) return uiCamera;
            var go = new GameObject("MGVR UI Camera");
            Object.DontDestroyOnLoad(go);
            uiCamera = go.AddComponent(Il2CppType.Of<Camera>()).Cast<Camera>();
            uiCamera.enabled = false;
            uiCamera.stereoTargetEye = StereoTargetEyeMask.None;
            uiCamera.nearClipPlane = 0.01f;
            uiCamera.farClipPlane = 100;
            uiCamera.cullingMask = 0;
            return uiCamera;
        }
    }

    internal static bool Prepare(ScriptableRenderContext context, Camera view, Quaternion bodyRotation)
    {
        bool menu = Il2Cpp.InputController.IsSetUp &&
                    Il2Cpp.InputController.currentControlMapping == Il2Cpp.InputController.MG_ControlMapping.menu;
        if (!menu)
        {
            anchored = false;
            return false;
        }

        int width = Math.Max(1, Screen.width), height = Math.Max(1, Screen.height);
        if (texture == null || texture.width != width || texture.height != height)
        {
            ReleaseTexture();
            texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                { name = "MGVR menu surface", hideFlags = HideFlags.HideAndDontSave };
            texture.Create();
            anchored = false;
        }

        if (!anchored)
        {
            float panelWidth = Math.Min(1.7f, 1.0f * width / height);
            float panelHeight = panelWidth * height / width;
            // Keep the menu upright in the player's current gravity frame.
            // Looking down at the controller while opening must not put it at
            // floor height for the remainder of the pause.
            var player = Il2Cpp.GameManager.PlayerController;
            var up = !Il2Cpp.LevelLoader.IsOnTitleScreen && player != null && player.bodyTransform != null
                ? player.bodyTransform.up
                : bodyRotation * Vector3.up;
            var forward = view.transform.forward - up * Vector3.Dot(view.transform.forward, up);
            if (forward.sqrMagnitude < .001f) forward = bodyRotation * Vector3.forward;
            forward = forward.normalized;
            // Anchor once per opening in the current gravity frame, below eye level.
            panelMatrix = Matrix4x4.TRS(view.transform.position + forward * 1.3f - up * .08f,
                Quaternion.LookRotation(forward, up), new Vector3(panelWidth, panelHeight, 1));
            anchored = true;
        }

        if (drawOverlay == null)
        {
            // UnityPlayer contains this binding; the managed API was stripped.
            var address = IL2CPP.il2cpp_resolve_icall(
                "UnityEngine.Rendering.ScriptableRenderContext::DrawUIOverlay_Internal_Injected");
            if (address == IntPtr.Zero) throw new InvalidOperationException("Unity UI overlay binding unavailable");
            drawOverlay = Marshal.GetDelegateForFunctionPointer<DrawOverlayFn>(address);
            Probe.Write($"VR_MENU_OVERLAY_BINDING address=0x{address.ToInt64():X}");
        }

        if (material == null || quad == null)
        {
            if (material != null) Object.Destroy(material);
            if (quad != null) Object.Destroy(quad);
            var shader = Shader.Find("UI/Default");
            if (shader == null) throw new InvalidOperationException("UI/Default shader unavailable");
            material = new Material(shader) { name = "MGVR menu panel", hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            quad = new Mesh { name = "MGVR menu quad", hideFlags = HideFlags.HideAndDontSave };
            quad.vertices = new Vector3[]
                { new(-.5f, -.5f, 0), new(-.5f, .5f, 0), new(.5f, .5f, 0), new(.5f, -.5f, 0) };
            quad.uv = new Vector2[] { new(0, 0), new(0, 1), new(1, 1), new(1, 0) };
            quad.colors = new Color[] { Color.white, Color.white, Color.white, Color.white };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            quad.RecalculateBounds();
        }

        material.mainTexture = texture;
        Canvas.ForceUpdateCanvases();
        var camera = FallbackCamera;
        camera.targetTexture = texture;
        camera.aspect = (float)width / height;
        var cmd = new CommandBuffer { name = "MGVR capture menu" };
        try
        {
            context.SetupCameraProperties(camera, false);
            cmd.SetRenderTarget(new RenderTargetIdentifier(texture));
            cmd.SetViewport(new Rect(0, 0, width, height));
            cmd.ClearRenderTarget(true, true, new Color(.025f, .025f, .025f, 1));
            context.ExecuteCommandBuffer(cmd);
            drawOverlay(ref context, camera.Pointer);
            context.Submit();
            if (Time.realtimeSinceStartupAsDouble >= nextReport)
            {
                nextReport = Time.realtimeSinceStartupAsDouble + 5;
                Probe.Write($"VR_MENU_SURFACE size={width}x{height} center={panelMatrix.GetColumn(3)}");
            }
        }
        finally
        {
            camera.targetTexture = null;
            cmd.Release();
        }

        return true;
    }

    internal static void Render(ScriptableRenderContext context, Camera view, RenderTexture target, bool clearColor,
        bool menu)
    {
        var cmd = new CommandBuffer { name = "MGVR menu eye" };
        try
        {
            cmd.SetRenderTarget(new RenderTargetIdentifier(target));
            cmd.SetViewport(new Rect(0, 0, target.width, target.height));
            cmd.ClearRenderTarget(true, clearColor, Color.black);
            if (menu)
            {
                // XR eye targets use the display orientation. The ordinary
                // RenderTexture Y flip also mirrors the panel's world height;
                // compensating only the UV made text upright but left it low.
                cmd.SetViewProjectionMatrices(view.worldToCameraMatrix,
                    GL.GetGPUProjectionMatrix(view.projectionMatrix, false));
                cmd.DrawMesh(quad!, panelMatrix, material!, 0, 0);
                VrMenuPointer.Draw(cmd);
            }

            context.ExecuteCommandBuffer(cmd);
            context.Submit();
        }
        finally
        {
            cmd.Release();
        }
    }

    internal static void RestoreDesktopTarget(ScriptableRenderContext context)
    {
        // Empty outer camera arrays cannot rebind the desktop backbuffer.
        // Leaving the right eye bound puts later desktop overlays into it.
        var cmd = new CommandBuffer { name = "MGVR restore desktop target" };
        try
        {
            cmd.SetRenderTarget(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));
            cmd.SetViewport(new Rect(0, 0, Screen.width, Screen.height));
            context.ExecuteCommandBuffer(cmd);
            context.Submit();
        }
        finally
        {
            cmd.Release();
        }
    }

    internal static void Report()
    {
        foreach (var obj in Object.FindObjectsOfType(Il2CppType.Of<Canvas>()))
        {
            var canvas = obj.Cast<Canvas>();
            if (canvas.isRootCanvas && canvas.isActiveAndEnabled)
                Probe.Write($"VR_CANVAS name={canvas.name} mode={canvas.renderMode}");
        }
    }

    internal static void ResetAnchor() => anchored = false;

    internal static bool Project(Ray ray, out Vector2 screen, out Vector3 hit, out bool inside)
    {
        screen = default; hit = default; inside = false;
        if (!anchored || texture == null) return false;
        var inverse = panelMatrix.inverse;
        var origin = inverse.MultiplyPoint3x4(ray.origin);
        var direction = inverse.MultiplyVector(ray.direction);
        if (!MenuRayProjection.Project(origin.x, origin.y, origin.z, direction.x, direction.y, direction.z,
                texture.width, texture.height, out var x, out var y, out var distance, true)) return false;
        screen = new Vector2(x, y);
        hit = ray.origin + ray.direction * distance;
        inside = x >= 0 && x <= texture.width && y >= 0 && y <= texture.height;
        return true;
    }

    static void ReleaseTexture()
    {
        if (texture != null)
        {
            texture.Release();
            Object.Destroy(texture);
        }

        texture = null;
    }

    internal static void Release()
    {
        VrMenuPointer.Release();
        ReleaseTexture();
        if (uiCamera != null) Object.Destroy(uiCamera.gameObject);
        if (quad != null) Object.Destroy(quad);
        if (material != null) Object.Destroy(material);
        uiCamera = null;
        quad = null;
        material = null;
        anchored = false;
    }
}
