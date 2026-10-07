using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SubsystemsImplementation;
using UnityEngine.XR;

namespace ManifoldProbe;

// Discovery only: never initializes a session or changes the render pipeline.
internal static class OpenXRProbe
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string name);

    internal static void Discover()
    {
        try
        {
            Probe.Write("OPENXR_DISCOVERY_BEGIN");
            // IL2CPP does not initialize this otherwise-unused Unity manager just
            // because its CLR interop wrapper is referenced. Its cctor builds the
            // native-to-managed XR descriptor class map.
            var managerClass = IL2CPP.GetIl2CppClass("UnityEngine.SubsystemsModule.dll", "UnityEngine", "SubsystemManager");
            if (managerClass == IntPtr.Zero) throw new InvalidOperationException("SubsystemManager IL2CPP class missing");
            IL2CPP.il2cpp_runtime_class_init(managerClass);
            var descriptors = SubsystemDescriptorStore.s_IntegratedDescriptors;
            Probe.Write("OPENXR_DESCRIPTOR_COUNT " + descriptors.Count);
            for (int i = 0; i < descriptors.Count; i++)
            {
                var descriptor = descriptors[i];
                Probe.Write($"OPENXR_DESCRIPTOR id={descriptor.id} type={descriptor.GetIl2CppType().FullName} native=0x{descriptor.m_Ptr.ToInt64():X}");
            }
            Probe.Write($"OPENXR_NATIVE_MODULE unity=0x{GetModuleHandle("UnityOpenXR.dll").ToInt64():X} loader=0x{GetModuleHandle("openxr_loader.dll").ToInt64():X}");
            foreach (var type in new[] { typeof(IntegratedSubsystem), typeof(IntegratedSubsystemDescriptor), typeof(XRDisplaySubsystem), typeof(XRInputSubsystem), typeof(SubsystemManager) })
            {
                var names = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(m => !m.Name.StartsWith("get_Native") && !m.Name.StartsWith("set_Native"))
                    .Select(m => m.Name).Distinct().OrderBy(n => n);
                Probe.Write($"OPENXR_MANAGED_API {type.Name}: {string.Join(",", names)}");
            }
            foreach (var name in new[] {
                "UnityEngine.SubsystemDescriptorBindings::Create",
                "UnityEngine.IntegratedSubsystem::Start",
                "UnityEngine.IntegratedSubsystem::Stop",
                "UnityEngine.IntegratedSubsystem::IsRunning",
                "UnityEngine.XR.XRDisplaySubsystem::GetRenderPassCount",
                "UnityEngine.XR.XRDisplaySubsystem::GetRenderPass",
                "UnityEngine.XR.XRDisplaySubsystem::GetCullingParameters",
                "UnityEngine.XR.XRInputSubsystem::TrySetTrackingOriginMode"
            })
            {
                var address = IL2CPP.il2cpp_resolve_icall(name);
                Probe.Write($"OPENXR_ICALL {name}=0x{address.ToInt64():X}");
            }
            Probe.Write("OPENXR_DISCOVERY_END");
        }
        catch (Exception ex) { Probe.Write("OPENXR_DISCOVERY_ERROR " + ex); }
    }
}