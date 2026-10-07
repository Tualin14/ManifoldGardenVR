using UnityEngine;

namespace ManifoldProbe;

// Runtime-only comfort experiment. Do not modify material assets or game saves.
internal static class DistortionControl
{
    internal static bool Suppressed { get; private set; }
    static readonly List<Material> materials = new();
    static readonly string[] properties = { "_Distortion", "_DistortionSolid", "_UVSpaceDistortionAmount", "_UVSpaceDistortionAmountSolid", "_1WiggleUvStrength", "_ChromaticAbberationStrength" };
    static double nextScan;
    internal static void SetSuppressed(bool value)
    {
        Suppressed = value;
        nextScan = 0;
        Probe.Write("OPENXR_DISTORTION_SUPPRESSED " + value);
    }
    internal static IDisposable? ApplyIfEnabled() => Suppressed ? Apply() : null;
    internal static IDisposable Apply()
    {
        if (Time.realtimeSinceStartupAsDouble >= nextScan)
        {
            nextScan = Time.realtimeSinceStartupAsDouble + 5;
            materials.Clear();
            var all = Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < all.Length; i++)
            {
                var material = all[i];
                if (material == null || material.shader == null) continue;
                var shader = material.shader.name;
                if (shader == "Mobius/Post/CollapsedCube" || shader == "Mobius/Post/VoronoiDistortion") materials.Add(material);
            }
            Probe.Write("OPENXR_DISTORTION_MATERIALS " + materials.Count);
        }
        var scope = new RestoreScope();
        try
        {
            foreach (var material in materials)
            {
                if (material == null) continue;
                foreach (var property in properties)
                {
                    if (!material.HasProperty(property)) continue;
                    scope.Values.Add((material, property, material.GetFloat(property)));
                    material.SetFloat(property, 0);
                }
            }
            return scope;
        }
        catch { scope.Dispose(); throw; }
    }
    sealed class RestoreScope : IDisposable
    {
        internal readonly List<(Material material, string property, float value)> Values = new();
        public void Dispose()
        {
            foreach (var item in Values) if (item.material != null) item.material.SetFloat(item.property, item.value);
            Values.Clear();
        }
    }
}
