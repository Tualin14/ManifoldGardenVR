using System.Runtime.InteropServices;
using UnityEngine;
using Valve.VR;

namespace ManifoldProbe;

internal static class VrDisplay
{
    [DllImport("MGVR.Native", CallingConvention=CallingConvention.Cdecl)] static extern int MGVR_Enable();
    [DllImport("MGVR.Native", CallingConvention=CallingConvention.Cdecl)] static extern void MGVR_Disable();
    [DllImport("MGVR.Native", CallingConvention=CallingConvention.Cdecl)] static extern IntPtr MGVR_GetRenderEvent();
    [DllImport("MGVR.Native", CallingConvention=CallingConvention.Cdecl)] static extern int MGVR_Queue(IntPtr left, IntPtr right, ref HmdMatrix34_t pose, int flip);
    [DllImport("MGVR.Native", CallingConvention=CallingConvention.Cdecl)] static extern int MGVR_Status(int which);
    static IntPtr callback;
    static double nextLog;
    static bool oldBackground;
    static int oldVsync, oldRate;
    public static bool Enabled { get; private set; }
    public static bool Flip { get; private set; } = true;

    public static void Start()
    {
        if(Enabled) return;
        try
        {
            if(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Direct3D11) throw new NotSupportedException("Display bridge requires D3D11");
            HeadTracking.Start(true);
            if(!HeadTracking.Enabled) return;
            if(MGVR_Enable()==0) throw new InvalidOperationException("OpenVR compositor interface unavailable");
            callback=MGVR_GetRenderEvent();
            oldBackground=Application.runInBackground;
            oldVsync=QualitySettings.vSyncCount;
            oldRate=Application.targetFrameRate;
            Application.runInBackground=true;
            QualitySettings.vSyncCount=0;
            Application.targetFrameRate=-1;
            Enabled=true;
            nextLog=0;
            Probe.Write("VR_DISPLAY_ENABLED eyeSize=768x768; direct3d11; renderThreadSubmit; explicitRenderPose; flip="+Flip);
        }
        catch(Exception ex) { Probe.Write("VR_DISPLAY_START_ERROR "+ex); HeadTracking.Shutdown(); }
    }
    public static void Stop()
    {
        if(!Enabled) return;
        MGVR_Disable();
        Enabled=false;
        Application.runInBackground=oldBackground;
        QualitySettings.vSyncCount=oldVsync;
        Application.targetFrameRate=oldRate;
        HeadTracking.Shutdown();
        Probe.Write("VR_DISPLAY_DISABLED");
    }
    public static void ToggleFlip() { Flip=!Flip; Probe.Write("VR_DISPLAY_FLIP="+Flip); }
    public static Matrix4x4 Projection(int eye, float near, float far)
    {
        var m=HeadTracking.System!.GetProjectionMatrix((EVREye)eye, near, far);
        var p=new Matrix4x4();
        p.m00=m.m0; p.m01=m.m1; p.m02=m.m2; p.m03=m.m3;
        p.m10=m.m4; p.m11=m.m5; p.m12=m.m6; p.m13=m.m7;
        p.m20=m.m8; p.m21=m.m9; p.m22=m.m10; p.m23=m.m11;
        p.m30=m.m12; p.m31=m.m13; p.m32=m.m14; p.m33=m.m15;
        return p;
    }
    public static Vector3 EyeOffset(int eye)
    {
        var m=HeadTracking.System!.GetEyeToHeadTransform((EVREye)eye);
        return new Vector3(m.m3,m.m7,-m.m11);
    }
    public static void Submit(RenderTexture left,RenderTexture right)
    {
        var pose=HeadTracking.RenderPose;
        if(MGVR_Queue(left.GetNativeTexturePtr(),right.GetNativeTexturePtr(),ref pose,Flip?1:0)!=0)
            GL.IssuePluginEvent(callback,1);
    }
    public static void Update()
    {
        if(!Enabled || Time.realtimeSinceStartupAsDouble<nextLog) return;
        nextLog=Time.realtimeSinceStartupAsDouble+3;
        Probe.Write($"VR_SUBMIT_STATUS completed={MGVR_Status(0)} leftError={MGVR_Status(1)} rightError={MGVR_Status(2)} waitError={MGVR_Status(3)}");
    }
}
