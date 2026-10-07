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
    PerSecond
}

internal static class VrMenuText
{
    // The shipped Chinese atlas is a subset. Keep this added page in English.
    internal static int Language(int gameLanguage) => 0;

    // The game's LocalizationLanguage.Chinese_Simplified is 7. Traditional
    // Chinese and all other languages intentionally use the English image.
    internal static string GuideResource(int gameLanguage) =>
        gameLanguage == 7 ? "MGVR.Assets.VrGuide.zh-CN.png" : "MGVR.Assets.VrGuide.en.png";

    static readonly string[] english =
    {
        "VR Settings", "Turning", "Snap angle", "Smooth turn speed", "Pick up / Drop / Interact", "Change gravity",
        "Run", "Rotate item clockwise", "Rotate item counterclockwise", "Snap", "Smooth",
        "Left trigger", "Left grip", "Right trigger", "Right grip", "Reset", "Back",
        "Changes apply and save immediately.",
        "Saved. Return to the game to use these settings.",
        "Could not save. These settings apply for this session.",
        "Left stick: move\nRight stick: turn view\n" +
        "Left grip: run\nRight grip: change gravity\n" +
        "Right trigger: pick up / drop / interact using the right-hand ray\n" +
        "A: rotate item clockwise\nB: rotate item counterclockwise\n" +
        "Y: hold 1s to recenter height and heading; center sticks first, release Y to rearm\n" +
        "Menu button: menu\nMeta button: Quest system menu\n" +
        "X: test level selection\nLeft trigger / stick clicks: no action\nHeadset controls look.\n" +
        "Menu: point with right hand; right trigger clicks / drags. Click Back to return.",
        "s"
    };

    internal static string Get(int language, VrText key) => english[(int)key];
}
