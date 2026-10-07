using UnityEngine;

namespace ManifoldProbe;

// Single-frame targetTexture diagnostic. No compositor submission.
internal static class TextureProbe
{
    static bool requested;
    static Camera? camera;
    static RenderTexture? previous;
    static RenderTexture? target;
    static int readFrame = -1;

    public static void Request() { requested = true; Probe.Write("TEXTURE_PROBE_REQUESTED"); }

    public static void Begin(Camera? candidate)
    {
        if (!requested || candidate == null || !candidate.gameObject.scene.name.StartsWith("World_")) return;
        requested = false;
        try
        {
            target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
            target.name = "VR diagnostic target";
            if (!target.Create()) throw new InvalidOperationException("RenderTexture.Create returned false");
            var active = RenderTexture.active;
            try { RenderTexture.active = target; GL.Clear(true, true, Color.magenta); }
            finally { RenderTexture.active = active; }
            camera = candidate;
            previous = candidate.targetTexture;
            candidate.targetTexture = target;
            Probe.Write("TEXTURE_PROBE_BOUND frame=" + Time.frameCount);
        }
        catch (Exception ex) { Probe.Write("TEXTURE_PROBE_BEGIN_ERROR " + ex); End(); }
    }

    public static void End()
    {
        if (camera == null) return;
        camera.targetTexture = previous;
        camera = null;
        previous = null;
        readFrame = Time.frameCount + 1;
        Probe.Write("TEXTURE_PROBE_RESTORED frame=" + Time.frameCount);
    }

    public static void Update()
    {
        if (readFrame < 0 || Time.frameCount < readFrame || target == null) return;
        readFrame = -1;
        var active = RenderTexture.active;
        Texture2D? cpu = null;
        try
        {
            RenderTexture.active = target;
            cpu = new Texture2D(512, 512, TextureFormat.RGB24, false);
            cpu.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            cpu.Apply();
            var bytes = ImageConversion.EncodeToPNG(cpu);
            var path = Path.Combine(Probe.Output, "target-texture.png");
            File.WriteAllBytes(path, bytes);
            Probe.Write("TEXTURE_PROBE_SAVED " + path + "; bytes=" + bytes.Length);
            try
            {
                var method = typeof(Texture).GetMethod("GetNativeTexturePtr", Type.EmptyTypes);
                Probe.Write("TEXTURE_NATIVE_POINTER " + (method == null ? "method unavailable" : method.Invoke(target, null)));
            }
            catch (Exception ex) { Probe.Write("TEXTURE_NATIVE_POINTER_ERROR " + ex); }
        }
        catch (Exception ex) { Probe.Write("TEXTURE_PROBE_READ_ERROR " + ex); }
        finally
        {
            RenderTexture.active = active;
            if (cpu != null) UnityEngine.Object.Destroy(cpu);
            target.Release();
            UnityEngine.Object.Destroy(target);
            target = null;
        }
    }
}
