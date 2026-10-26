using HarmonyLib;
using UnityEngine;

namespace ManifoldProbe;

internal static class ProjectionProbe
{
    static int remaining;
    static Camera? stackCamera;
    static bool pendingProjection;
    static bool portalProjection;
    internal static bool Ready { get; private set; }

    public static void Install(HarmonyLib.Harmony harmony)
    {
        // Mobius resets its stack camera inside BindStackSettings. On the far
        // portal path, native oblique clipping precedes the first managed read.
        harmony.Patch(AccessTools.PropertyGetter(typeof(Camera), "projectionMatrix"),
            postfix: new HarmonyMethod(typeof(ProjectionProbe), nameof(AfterProjectionRead)));
        harmony.Patch(AccessTools.Method(typeof(Il2Cpp.MobiusPost), "BindStackSettings"),
            prefix: new HarmonyMethod(typeof(ProjectionProbe), nameof(BeforeBind)),
            postfix: new HarmonyMethod(typeof(ProjectionProbe), nameof(AfterBind)),
            finalizer: new HarmonyMethod(typeof(ProjectionProbe), nameof(FinishBind)));
        Ready = true;
        Probe.Write(
            "PROJECTION_HOOK_INSTALLED BindStackSettings + first stack projection read; portal clip tilt preserved");
    }

    static void BeforeBind(Il2Cpp.MobiusPost __instance, Il2Cpp.RenderPassStack __0)
    {
        if (!OpenXRRenderer.Nested) return;
        stackCamera = __instance.m_stackCam;
        pendingProjection = true;
        portalProjection = __0.Recurs > 0;
    }

    static void AfterProjectionRead(Camera __instance, ref Matrix4x4 __result)
    {
        if (!OpenXRRenderer.Nested || !pendingProjection || stackCamera == null ||
            __instance.Pointer != stackCamera.Pointer) return;
        pendingProjection = false;
        // Keep the original depth range and, for portals, the complete oblique
        // near plane. SetScissorRect's first read may already contain native
        // clipping; replacing m20/m21 would incorrectly flatten that plane.
        var lens = OpenXRRenderer.EyeProjection;
        if (portalProjection)
        {
            lens.m20 = __result.m20;
            lens.m21 = __result.m21;
        }

        lens.m22 = __result.m22;
        lens.m23 = __result.m23;
        __result = lens;
        __instance.projectionMatrix = lens;
    }

    static Exception? FinishBind(Exception? __exception)
    {
        stackCamera = null;
        pendingProjection = false;
        portalProjection = false;
        return __exception;
    }

    public static void Request()
    {
        remaining = 12;
    }

    static string Format(Matrix4x4 m) =>
        $"x=({m.m00:F5},{m.m01:F5},{m.m02:F5},{m.m03:F5}) y=({m.m10:F5},{m.m11:F5},{m.m12:F5},{m.m13:F5}) z=({m.m20:F5},{m.m21:F5},{m.m22:F5},{m.m23:F5})";

    static void AfterBind(Il2Cpp.MobiusPost __instance, Il2Cpp.RenderPassStack __0)
    {
        bool missed = OpenXRRenderer.Nested && pendingProjection;
        pendingProjection = false;
        stackCamera = null;
        portalProjection = false;
        if ((!StereoRenderer.Nested && !OpenXRRenderer.Nested) || remaining <= 0) return;
        remaining--;
        var c = __instance.m_stackCam;
        Probe.Write(
            $"PROJECTION_DIAGNOSTIC frame={Time.frameCount} eye={(OpenXRRenderer.Nested ? OpenXRRenderer.EyeIndex : StereoRenderer.EyeIndex)} recursion={__0.Recurs} missed={missed} expected=[{Format(OpenXRRenderer.Nested ? OpenXRRenderer.EyeProjection : StereoRenderer.EyeProjection)}] stack=[{Format(c.projectionMatrix)}] fov={c.fieldOfView} aspect={c.aspect}");
    }
}