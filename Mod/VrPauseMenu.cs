namespace ManifoldProbe;

internal static class VrPauseMenu
{
    internal static void Tick()
    {
        if (!VrStartup.LaunchedInVr || !Il2Cpp.GameManager.IsInitialized) return;
        var ui = Il2Cpp.GameManager.UI;
        if (ui == null) return;
        var panel = ui.pausePanelNew;
        if (panel == null) return;
        var button = panel.photoModeButton;
        if (button == null || !button.gameObject.activeSelf) return;

        // The native opening coroutine can reactivate this button. Apply after
        // native menu updates and before ray input/rendering, including after
        // a scene change creates a new pause panel. Hide the whole selectable.
        button.gameObject.SetActive(false);
        Probe.Write("VR_PHOTO_MODE_BUTTON_HIDDEN");
    }
}
