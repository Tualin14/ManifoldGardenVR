using UnityEngine;

namespace ManifoldProbe;

internal static class PointerColor
{
    internal static Color Current
    {
        get
        {
            var player = Il2Cpp.GameManager.PlayerController;
            var palette = Il2Cpp.RelativityUtils.instance;
            int index = player == null ? -1 : (int)player.gravityDirection;
            var colors = palette == null ? null : palette.reticleColors;
            return colors != null && index >= 0 && index < colors.Length ? colors[index] : Color.white;
        }
    }
}