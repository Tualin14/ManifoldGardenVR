namespace ManifoldProbe;

// Managed metrics shared by the loader and offline coverage checks.
internal sealed class VrMenuFontData
{
    public int Version { get; set; }
    public string Family { get; set; } = "";
    public int PointSize { get; set; }
    public int Padding { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public float Ascent { get; set; }
    public float Descent { get; set; }
    public float LineHeight { get; set; }
    public float CapHeight { get; set; }
    public float MeanLine { get; set; }
    public string Characters { get; set; } = "";
    public string AtlasSHA256 { get; set; } = "";
    public MenuGlyph[] Glyphs { get; set; } = Array.Empty<MenuGlyph>();
}

internal sealed class MenuGlyph
{
    public uint Unicode { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public float BearingX { get; set; }
    public float BearingY { get; set; }
    public float Advance { get; set; }
}
