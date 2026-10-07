#include <d3d11.h>
#include <atomic>
#include <deque>
#include <mutex>
#include "../ThirdParty/OpenVR/openvr.h"

// Small Unity rendering-event bridge. Never accesses Unity objects from native code.
struct Frame {
    ID3D11Texture2D* left;
    ID3D11Texture2D* right;
    vr::HmdMatrix34_t pose;
    bool flip;
};
static std::mutex gate;
static std::deque<Frame> frames;
static vr::IVRCompositor* compositor = nullptr;
static std::atomic<int> submitted{0}, leftError{-1}, rightError{-1}, waitError{-1};
static void Release(Frame& f) { f.left->Release(); f.right->Release(); }

extern "C" __declspec(dllexport) int __cdecl MGVR_Enable() {
    std::lock_guard<std::mutex> lock(gate);
    compositor = vr::VRCompositor();
    submitted=0; leftError=-1; rightError=-1; waitError=-1;
    if(compositor) compositor->SetTrackingSpace(vr::TrackingUniverseStanding);
    return compositor ? 1 : 0;
}
extern "C" __declspec(dllexport) void __cdecl MGVR_Disable() {
    std::lock_guard<std::mutex> lock(gate);
    if(compositor) compositor->ClearLastSubmittedFrame();
    compositor=nullptr;
    for(auto& f: frames) Release(f);
    frames.clear();
}
extern "C" __declspec(dllexport) int __cdecl MGVR_Queue(void* left,void* right,const vr::HmdMatrix34_t* pose,int flip) {
    std::lock_guard<std::mutex> lock(gate);
    if(!compositor || !left || !right || !pose || frames.size()>=2) return 0;
    Frame f{static_cast<ID3D11Texture2D*>(left),static_cast<ID3D11Texture2D*>(right),*pose,flip!=0};
    f.left->AddRef(); f.right->AddRef(); frames.push_back(f); return 1;
}
static void __stdcall OnRenderEvent(int) {
    std::lock_guard<std::mutex> lock(gate);
    if(!compositor || frames.empty()) return;
    Frame f=frames.front(); frames.pop_front();
    vr::TrackedDevicePose_t poses[vr::k_unMaxTrackedDeviceCount];
    auto result=compositor->WaitGetPoses(poses,vr::k_unMaxTrackedDeviceCount,nullptr,0);
    waitError=static_cast<int>(result);
    if(result==vr::VRCompositorError_None) {
        vr::VRTextureWithPose_t texture{};
        texture.eType=vr::TextureType_DirectX; texture.eColorSpace=vr::ColorSpace_Auto;
        texture.mDeviceToAbsoluteTracking=f.pose;
        vr::VRTextureBounds_t bounds{0, f.flip ? 1.f : 0.f, 1, f.flip ? 0.f : 1.f};
        texture.handle=f.left;
        leftError=static_cast<int>(compositor->Submit(vr::Eye_Left,&texture,&bounds,vr::Submit_TextureWithPose));
        texture.handle=f.right;
        rightError=static_cast<int>(compositor->Submit(vr::Eye_Right,&texture,&bounds,vr::Submit_TextureWithPose));
        if(leftError==0 && rightError==0) ++submitted;
    }
    Release(f);
}
extern "C" __declspec(dllexport) void* __cdecl MGVR_GetRenderEvent() { return reinterpret_cast<void*>(&OnRenderEvent); }
extern "C" __declspec(dllexport) int __cdecl MGVR_Status(int which) {
    switch(which){case 0:return submitted;case 1:return leftError;case 2:return rightError;case 3:return waitError;default:return -999;}
}
