using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ManifoldProbe;

internal static class VrPointer
{
    static Material? material;
    static Mesh? beam;
    static bool visible, failed;
    static Matrix4x4 matrix;
    static Color color;

    internal static void Prepare()
    {
        visible = false;
        VrInteractionFeedback.Clear();
        if (failed) return;
        try
        {
            if (!VrInteraction.TryPointerRay(out var ray)) return;
            var carry = Il2Cpp.GameManager.PlayerCarry;
            float range = carry != null ? carry.detectionRayLength : 3;
            if (!float.IsFinite(range) || range <= 0) return;
            // Visual clipping only; gameplay keeps the original portal-aware raycast.
            float length = Physics.Raycast(ray, out var hit, range, -1, QueryTriggerInteraction.Ignore)
                ? Math.Max(.01f, hit.distance)
                : range;
            matrix = Matrix4x4.TRS(ray.origin, Quaternion.LookRotation(ray.direction), new Vector3(1, 1, length));
            color = PointerColor.Current;
            color.a = .85f;
            visible = true;
            var camera = VrCamera.Current;
            if (camera != null && OpenXRRenderer.TryGameplayPose(camera, out var headPosition, out var headRotation))
                VrInteractionFeedback.Prepare(ray.origin + ray.direction * length, headPosition, headRotation);
        }
        catch (Exception ex)
        {
            failed = true;
            Probe.Write("VR_POINTER_DISABLED_ERROR " + ex);
        }
    }

    internal static void Render(ScriptableRenderContext context, Camera view, RenderTexture target)
    {
        if (!visible || failed) return;
        CommandBuffer? cmd = null;
        try
        {
            if (material == null || beam == null)
            {
                if (beam != null) Object.Destroy(beam);
                if (material != null) Object.Destroy(material);
                var shader = Shader.Find("UI/Default") ??
                             throw new InvalidOperationException("Pointer shader unavailable");
                material = new Material(shader) { name = "MGVR right controller ray",
                    hideFlags = HideFlags.HideAndDontSave };
                material.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
                beam = new Mesh { name = "MGVR controller ray", hideFlags = HideFlags.HideAndDontSave };
                beam.vertices = new Il2CppStructArray<Vector3>(new[]
                {
                    new Vector3(-.003f, 0, 0), new Vector3(.003f, 0, 0), new Vector3(.003f, 0, 1),
                    new Vector3(-.003f, 0, 1),
                    new Vector3(0, -.003f, 0), new Vector3(0, .003f, 0), new Vector3(0, .003f, 1),
                    new Vector3(0, -.003f, 1)
                });
                beam.uv = new Il2CppStructArray<Vector2>(new[]
                {
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1)
                });
                beam.colors = new Il2CppStructArray<Color>(Enumerable.Repeat(Color.white, 8).ToArray());
                beam.triangles = new Il2CppStructArray<int>(new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 });
                beam.RecalculateBounds();
                Probe.Write("VR_POINTER_RENDER_READY source=right-controller color=game-reticle-palette");
            }

            material.color = color;
            cmd = new CommandBuffer { name = "MGVR controller ray eye" };
            cmd.SetRenderTarget(new RenderTargetIdentifier(target));
            cmd.SetViewport(new Rect(0, 0, target.width, target.height));
            cmd.SetViewProjectionMatrices(view.worldToCameraMatrix,
                GL.GetGPUProjectionMatrix(view.projectionMatrix, false));
            cmd.DrawMesh(beam!, matrix, material!, 0, 0);
            VrInteractionFeedback.Draw(cmd);
            context.ExecuteCommandBuffer(cmd);
            context.Submit();
        }
        catch (Exception ex)
        {
            failed = true;
            Probe.Write("VR_POINTER_RENDER_ERROR " + ex);
        }
        finally
        {
            cmd?.Release();
        }
    }

    internal static void Release()
    {
        VrInteractionFeedback.Release();
        if (beam != null) Object.Destroy(beam);
        if (material != null) Object.Destroy(material);
        beam = null;
        material = null;
        visible = failed = false;
    }
}
