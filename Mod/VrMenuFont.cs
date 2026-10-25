using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

namespace ManifoldProbe;

// This font belongs only to MGVR labels. No game asset, global fallback or OS
// font is changed. The static, pre-baked atlas never grows or creates glyphs.
internal static class VrMenuFont
{
    static TMP_FontAsset? chinese;
    static bool attempted;

    internal static TMP_FontAsset Resolve(int gameLanguage, TMP_FontAsset latin, out int language)
    {
        language = 0;
        if (!VrStartup.LaunchedInVr || VrMenuText.Language(gameLanguage) != 7) return latin;
        if (!attempted)
        {
            attempted = true;
            Load(latin);
        }
        if (chinese == null) return latin; // A bad optional asset falls back to readable English.
        language = 7;
        return chinese;
    }

    static Stream Resource(string suffix) => Assembly.GetExecutingAssembly()
        .GetManifestResourceStream("MGVR.Assets.MenuFont." + suffix)
        ?? throw new FileNotFoundException("MGVR menu font " + suffix);

    static void Load(TMP_FontAsset latin)
    {
        TMP_FontAsset? font = null;
        Texture2D? texture = null;
        Material? material = null;
        try
        {
            using var info = Resource("zh-CN.json");
            var data = JsonSerializer.Deserialize<VrMenuFontData>(info)
                       ?? throw new InvalidDataException("Empty menu font metrics");
            using var packed = Resource("zh-CN.alpha");
            using var unpack = new GZipStream(packed, CompressionMode.Decompress);
            using var pixels = new MemoryStream();
            unpack.CopyTo(pixels);
            if (data.Version != 1 || pixels.Length != data.Width * data.Height)
                throw new InvalidDataException("Invalid menu font atlas");

            texture = new Texture2D(data.Width, data.Height, TextureFormat.Alpha8, false, true)
            {
                name = "MGVR Menu SC SDF Atlas", filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.LoadRawTextureData(pixels.ToArray());
            texture.Apply(false, true);
            material = new Material(latin.material) { name = "MGVR Menu SC SDF Material" };
            if (!material.HasProperty("_GradientScale"))
                throw new InvalidOperationException("Game menu material does not use an SDF shader");
            material.mainTexture = texture;
            material.SetFloat("_TextureWidth", data.Width);
            material.SetFloat("_TextureHeight", data.Height);
            material.SetFloat("_GradientScale", data.Padding + 1);
            material.SetFloat("_WeightNormal", 0);
            material.SetFloat("_WeightBold", .5f);

            font = ScriptableObject.CreateInstance(Il2CppType.Of<TMP_FontAsset>()).Cast<TMP_FontAsset>();
            font.name = data.Family;
            font.version = "1.1.0";
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.sourceFontFile = null;
            font.atlasTextures = new Il2CppReferenceArray<Texture2D>(new[] { texture });
            font.atlasWidth = data.Width;
            font.atlasHeight = data.Height;
            font.atlasPadding = data.Padding;
            font.atlasRenderMode = GlyphRenderMode.SDFAA;
            font.material = material;
            font.faceInfo = new FaceInfo
            {
                familyName = data.Family, styleName = "Regular", pointSize = data.PointSize,
                scale = 1, lineHeight = data.LineHeight, ascentLine = data.Ascent,
                capLine = data.CapHeight, meanLine = data.MeanLine, baseline = 0,
                descentLine = data.Descent, superscriptOffset = data.Ascent,
                superscriptSize = .5f, subscriptOffset = data.Descent, subscriptSize = .5f,
                underlineOffset = -data.PointSize * .12f, underlineThickness = data.PointSize * .05f,
                strikethroughOffset = data.CapHeight * .4f, strikethroughThickness = data.PointSize * .05f,
                tabWidth = data.PointSize * 2
            };
            foreach (var item in data.Glyphs)
            {
                var glyph = new Glyph(item.Unicode,
                    new GlyphMetrics(item.Width, item.Height, item.BearingX, item.BearingY, item.Advance),
                    new GlyphRect(item.X, item.Y, item.Width, item.Height), 1, 0);
                font.glyphTable.Add(glyph);
                font.characterTable.Add(new TMP_Character(item.Unicode, font, glyph));
            }
            font.ReadFontAssetDefinition();
            if (!font.HasCharacters(data.Characters))
                throw new InvalidDataException("Menu font character table is incomplete");
            Object.DontDestroyOnLoad(texture);
            Object.DontDestroyOnLoad(material);
            Object.DontDestroyOnLoad(font);
            chinese = font;
            Probe.Write($"VR_MENU_FONT_READY glyphs={data.Glyphs.Length} atlas={data.Width}x{data.Height} static=true");
        }
        catch (Exception ex)
        {
            if (font != null) Object.Destroy(font);
            if (material != null) Object.Destroy(material);
            if (texture != null) Object.Destroy(texture);
            Probe.Write("VR_MENU_FONT_ERROR " + ex.Message);
        }
    }
}
