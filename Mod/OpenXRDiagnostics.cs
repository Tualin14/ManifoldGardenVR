using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;

namespace ManifoldProbe;

// Opt-in, one-frame comparison before the image reaches the VR compositor.
internal static class OpenXRDiagnostics
{
    static bool requested;
    static string captureId = "";
    internal static void Request()
    {
        requested = true;
        Probe.Write("OPENXR_COMPARE_REQUESTED");
    }

    internal static bool BeginFrame()
    {
        if (!requested) return false;
        requested = false;
        captureId = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-f" + Time.frameCount;
        return true;
    }

    internal static void CaptureEye(Il2Cpp.MobiusRenderPipe pipe, ScriptableRenderContext context,
        Il2CppReferenceArray<Camera> cameras, Camera camera, RenderTexture target, int eye)
    {
        try
        {
            Save(target, eye, "normal");
            var post = camera.GetComponent<Il2Cpp.MobiusPost>();
            if (post == null)
            {
                Probe.Write("OPENXR_COMPARE_NO_MOBIUS_POST");
                return;
            }

            bool enabled = post.EffectsEnabled;
            Probe.Write(
                $"OPENXR_COMPARE_STATE eye={eye} effects={enabled} intense={post.IntenseSceneMode} intensity={post.IntensePercent} maxPortalOffset={post.MaxPortalOffset} portals={post.reachedPortals}");
            try
            {
                using var distortion = DistortionControl.Apply();
                pipe.Render(context, cameras);
                Save(target, eye, "no-distortion");
            }
            finally
            {
                post.EffectsEnabled = enabled;
                // Restore the ordinary eye image for this submitted frame.
                pipe.Render(context, cameras);
            }
        }
        catch (Exception ex)
        {
            Probe.Write("OPENXR_COMPARE_ERROR " + ex);
        }
    }

    internal static void SaveMenu(RenderTexture target, int eye) => Save(target, eye, "menu");

    static void Save(RenderTexture target, int eye, string mode)
    {
        var active = RenderTexture.active;
        Texture2D? cpu = null;
        try
        {
            RenderTexture.active = target;
            cpu = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            cpu.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            cpu.Apply();
            var path = Path.Combine(Probe.Output, $"xr-{captureId}-{mode}-eye{eye}.png");
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(cpu));
            Probe.Write("OPENXR_RAW_EYE_SAVED " + path);
        }
        finally
        {
            RenderTexture.active = active;
            if (cpu != null) UnityEngine.Object.Destroy(cpu);
        }
    }
}
