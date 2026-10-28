namespace ManifoldProbe;

internal readonly record struct SequenceProgress(bool Visible, float Fraction, float RemainingSeconds);

// Read-only presentation of the native AV counter. Input, timing and completion
// stay owned by AudioVisualLevelData.Update, including progress decay on release.
internal static class SequenceProgressPolicy
{
    internal static SequenceProgress Read(float heldTime, float duration, float initialWait)
    {
        if (!float.IsFinite(heldTime) || !float.IsFinite(duration) || !float.IsFinite(initialWait) ||
            duration <= 0 || initialWait > 0) return default;
        double elapsed = Math.Clamp((double)heldTime, 0, duration);
        double remaining = duration - elapsed;
        // Never display 0.0 while the native counter still needs time to finish.
        double displayed = remaining > 0 ? Math.Max(.1, Math.Ceiling(remaining * 10 - .00001) / 10) : 0;
        return new SequenceProgress(true, (float)(elapsed / duration), (float)displayed);
    }
}
