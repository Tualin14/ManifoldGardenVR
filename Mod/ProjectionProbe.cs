using HarmonyLib;
using UnityEngine;

namespace ManifoldProbe;

internal static class ProjectionProbe
{
    static int remaining;
    static Camera? stackCamera;
    static bool pendingProjection;
    internal static bool Ready { get; private set; }
    public static void Install(HarmonyLib.Harmony harmony)
    {
        // Mobius resets its stack camera inside BindStackSettings. The first
        // projection read must use the eye lens before oblique/scissor clipping.
        harmony.Patch(AccessTools.PropertyGetter(typeof(Camera), "projectionMatrix"),
            postfix: new HarmonyMethod(typeof(ProjectionProbe), nameof(AfterProjectionRead)));
        harmony.Patch(AccessTools.Method(typeof(Il2Cpp.MobiusPost), "BindStackSettings"),
            prefix: new HarmonyMethod(typeof(ProjectionProbe), nameof(BeforeBind)),
            postfix: new HarmonyMethod(typeof(ProjectionProbe), nameof(AfterBind)),
            finalizer: new HarmonyMethod(typeof(ProjectionProbe), nameof(FinishBind)));
        Ready = true;
        Probe.Write("PROJECTION_HOOK_INSTALLED BindStackSettings + first stack projection read");
    }
    static void BeforeBind(Il2Cpp.MobiusPost __instance)
    {
        if (!OpenXRRenderer.Nested) return;
        stackCamera = __instance.m_stackCam;
        pendingProjection = true;
    }
    static void AfterProjectionRead(Camera __instance, ref Matrix4x4 __result)
    {
        if (!OpenXRRenderer.Nested || !pendingProjection || stackCamera == null || __instance.Pointer != stackCamera.Pointer) return;
        pendingProjection = false;
        // Retain this pass's depth range. Mobius subsequently applies its portal
        // clipping to this asymmetric projection rather than the desktop lens.
        var lens = OpenXRRenderer.EyeProjection;
        lens.m22 = __result.m22;
        lens.m23 = __result.m23;
        __result = lens;
        __instance.projectionMatrix = lens;
    }
    static Exception? FinishBind(Exception? __exception)
    {
        stackCamera = null;
        pendingProjection = false;
        return __exception;
    }
    public static void Request() { remaining = 12; }
    static string Format(Matrix4x4 m) => $"x=({m.m00:F5},{m.m01:F5},{m.m02:F5},{m.m03:F5}) y=({m.m10:F5},{m.m11:F5},{m.m12:F5},{m.m13:F5}) z=({m.m20:F5},{m.m21:F5},{m.m22:F5},{m.m23:F5})";
    static void AfterBind(Il2Cpp.MobiusPost __instance, Il2Cpp.RenderPassStack __0)
    {
        bool missed = OpenXRRenderer.Nested && pendingProjection;
        pendingProjection = false;
        stackCamera = null;
        if ((!StereoRenderer.Nested && !OpenXRRenderer.Nested) || remaining <= 0) return;
        remaining--;
        var c = __instance.m_stackCam;
        Probe.Write($"PROJECTION_DIAGNOSTIC frame={Time.frameCount} eye={(OpenXRRenderer.Nested ? OpenXRRenderer.EyeIndex : StereoRenderer.EyeIndex)} recursion={__0.Recurs} missed={missed} expected=[{Format(OpenXRRenderer.Nested ? OpenXRRenderer.EyeProjection : StereoRenderer.EyeProjection)}] stack=[{Format(c.projectionMatrix)}] fov={c.fieldOfView} aspect={c.aspect}");
    }
}
