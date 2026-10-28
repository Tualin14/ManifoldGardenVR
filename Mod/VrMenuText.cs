using System.Reflection;
using System.Text.Json;

namespace ManifoldProbe;

internal enum VrText
{
    Tab,
    TurnMode,
    Angle,
    Speed,
    Interact,
    Gravity,
    Run,
    Clockwise,
    Counterclockwise,
    Snap,
    Smooth,
    LeftTrigger,
    LeftGrip,
    RightTrigger,
    RightGrip,
    Reset,
    Back,
    Hint,
    Saved,
    SaveFailed,
    FixedControls,
    PerSecond,
    Haptics,
    HapticsStrength,
    On,
    Off,
    TestBuildHint, TestLevels, LevelPointerHint, LevelNotice, Previous, Next,
    LevelBusy, LoadingLevel, LevelStartFailed, LevelLoadFailed, HoldInput
}

internal static class VrMenuText
{
    // Only Simplified Chinese opts into this translation. Traditional Chinese,
    // unsupported and unknown languages retain English.
    internal static int Language(int gameLanguage) => gameLanguage == 7 ? 7 : 0;

    // The game's LocalizationLanguage.Chinese_Simplified is 7. Traditional
    // Chinese and all other languages intentionally use the English image.
    internal static string GuideResource(int gameLanguage) =>
        gameLanguage == 7 ? "MGVR.Assets.VrGuide.zh-CN.png" : "MGVR.Assets.VrGuide.en.png";

    static readonly string[][] texts = Load();

    static string[][] Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MGVR.Assets.MenuText.json")
                           ?? throw new FileNotFoundException("MGVR.Assets.MenuText.json");
        var catalog = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)
                      ?? throw new InvalidDataException("Empty VR menu translation catalog");
        // Build indexed arrays once. Per-frame tab updates never parse JSON,
        // allocate enum names or search a translation dictionary.
        return new[] { "en", "zh-CN" }.Select(locale => Enum.GetValues<VrText>()
            .Select(key => catalog[locale][key.ToString()]).ToArray()).ToArray();
    }

    internal static string Get(int language, VrText key) =>
        texts[Language(language) == 7 ? 1 : 0][(int)key];
}
