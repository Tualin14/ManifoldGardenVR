using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;

namespace ManifoldProbe;

// Two eye renders inside one Unity frame, followed by the original desktop render.
internal static class StereoRenderer
{
    static bool requested;
    static readonly RenderTexture?[] eyes = new RenderTexture?[2];
    static int captureFrame = -1;
    public static bool Nested { get; private set; }
    public static int EyeIndex { get; private set; }
    public static Matrix4x4 EyeProjection { get; private set; }

    public static void Request() { requested = true; ProjectionProbe.Request(); Probe.Write("STEREO_FRAME_REQUESTED"); }

    public static void Render(Il2Cpp.MobiusRenderPipe pipe, ScriptableRenderContext context, Il2CppReferenceArray<Camera> cameras, Camera? camera)
    {
        if ((!requested && !VrDisplay.Enabled) || camera == null || !camera.gameObject.scene.name.StartsWith("World_")) return;
        bool capture = requested;
        var position = camera.transform.position;
        var rotation = camera.transform.rotation;
        var projection = camera.projectionMatrix;
        var target = camera.targetTexture;
        var aspect = camera.aspect;
        try
        {
            Nested = true;
            if (VrDisplay.Enabled && !HeadTracking.TryApply(camera)) return;
            requested = false;
            var headPosition = camera.transform.position;
            var headRotation = camera.transform.rotation;
            for (int eye = 0; eye < 2; eye++)
            {
                eyes[eye] ??= new RenderTexture(768, 768, 24, RenderTextureFormat.ARGB32);
                if (!eyes[eye]!.IsCreated() && !eyes[eye]!.Create()) throw new InvalidOperationException("Eye texture creation failed");
                camera.targetTexture = eyes[eye];
                camera.aspect = 1;
                var eyeProjection = VrDisplay.Enabled ? VrDisplay.Projection(eye,camera.nearClipPlane,camera.farClipPlane) : Matrix4x4.Perspective(camera.fieldOfView, 1, camera.nearClipPlane, camera.farClipPlane);
                camera.projectionMatrix = eyeProjection;
                EyeIndex=eye;
                EyeProjection=eyeProjection;
                camera.transform.rotation = headRotation;
                camera.transform.position = headPosition + headRotation * (VrDisplay.Enabled ? VrDisplay.EyeOffset(eye) : new Vector3(eye == 0 ? -0.032f : 0.032f, 0, 0));
                pipe.Render(context, cameras);
                if(capture) Probe.Write($"STEREO_EYE_RENDERED eye={eye} frame={Time.frameCount} target={camera.pixelWidth}x{camera.pixelHeight} position={camera.transform.position} projectionPreserved={camera.projectionMatrix.Equals(eyeProjection)}");
            }
            if(VrDisplay.Enabled) VrDisplay.Submit(eyes[0]!,eyes[1]!);
            if(capture) captureFrame = Time.frameCount;
        }
        catch (Exception ex) { Probe.Write("STEREO_FRAME_ERROR " + ex); VrDisplay.Stop(); }
        finally
        {
            camera.targetTexture = target;
            camera.aspect = aspect;
            camera.projectionMatrix = projection;
            camera.transform.SetPositionAndRotation(position, rotation);
            Nested = false;
            if(capture && captureFrame>=0) Probe.Write($"STEREO_CAMERA_RESTORED frame={Time.frameCount} targetRestored={camera.targetTexture == target} positionError={(camera.transform.position-position).magnitude}");
        }
    }

    public static void Update()
    {
        if (captureFrame < 0 || Time.frameCount <= captureFrame) return;
        var renderedFrame = captureFrame;
        captureFrame = -1;
        var active = RenderTexture.active;
        try
        {
            for (int eye = 0; eye < 2; eye++)
            {
                if (eyes[eye] == null) continue;
                Texture2D? cpu = null;
                try
                {
                    RenderTexture.active = eyes[eye];
                    cpu = new Texture2D(768, 768, TextureFormat.RGB24, false);
                    cpu.ReadPixels(new Rect(0, 0, 768, 768), 0, 0);
                    cpu.Apply();
                    var bytes = ImageConversion.EncodeToPNG(cpu);
                    var path = Path.Combine(Probe.Output, $"same-frame-{(eye==0 ? "left" : "right")}.png");
                    File.WriteAllBytes(path, bytes);
                    Probe.Write($"STEREO_EYE_SAVED eye={eye} renderFrame={renderedFrame} bytes={bytes.Length} path={path}");
                }
                finally { if (cpu != null) UnityEngine.Object.Destroy(cpu); }
            }
        }
        catch (Exception ex) { Probe.Write("STEREO_CAPTURE_ERROR " + ex); }
        finally
        {
            RenderTexture.active = active;
            if(!VrDisplay.Enabled) Release();
        }
    }
    public static void Release()
    {
        for (int eye=0;eye<2;eye++) { if(eyes[eye] != null) { eyes[eye]!.Release(); UnityEngine.Object.Destroy(eyes[eye]); eyes[eye]=null; } }
    }
}
