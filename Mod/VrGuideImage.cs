using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ManifoldProbe;

// Shared across settings panels and scene changes. Decode once per language,
// with no mipmaps or retained CPU pixels. Only the two language images are cached.
internal static class VrGuideImage
{
    static readonly Dictionary<string, Texture2D?> textures = new();

    internal static Texture2D? Get(int gameLanguage)
    {
        string resource = VrMenuText.GuideResource(gameLanguage);
        if (textures.TryGetValue(resource, out var cached)) return cached;
        Texture2D? texture = null;
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                               ?? throw new FileNotFoundException(resource);
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = resource, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            if (!ImageConversion.LoadImage(texture, bytes.ToArray(), true))
                throw new InvalidOperationException("Could not decode VR guide");
            Object.DontDestroyOnLoad(texture);
            Probe.Write($"VR_GUIDE_LOADED resource={resource} size={texture.width}x{texture.height}");
        }
        catch (Exception ex)
        {
            if (texture != null) Object.Destroy(texture);
            texture = null;
            Probe.Write("VR_GUIDE_IMAGE_ERROR " + ex.Message);
        }

        // Cache failures too, so a bad asset cannot cause a decoding loop.
        textures[resource] = texture;
        return texture;
    }
}