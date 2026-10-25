namespace ManifoldProbe;

internal enum HapticKind { Environment, Body, Object }

// Keep one current effect per category. Foreground feedback temporarily overrides
// background effects, whose deadlines keep advancing rather than being queued.
internal sealed class HapticPolicy
{
    internal readonly record struct Output(float Amplitude, float Duration);
    readonly record struct Effect(float Strength, double End);
    readonly Effect?[] effects = new Effect?[3];

    internal void Clear() => Array.Clear(effects, 0, effects.Length);

    internal void Request(HapticKind kind, float strength, float duration, double now, bool continuous = false)
    {
        if (!double.IsFinite(now) || !float.IsFinite(strength) || strength <= 0 ||
            (!continuous && (!float.IsFinite(duration) || duration <= 0))) return;
        effects[(int)kind] = new Effect(Math.Clamp(strength, 0, 1),
            continuous ? double.PositiveInfinity : now + duration);
    }

    internal Output Sample(bool right, double now, float gain)
    {
        if (!double.IsFinite(now) || !float.IsFinite(gain) || gain <= 0) return default;
        for (int i = right ? 2 : 1; i >= 0; i--)
        {
            if (effects[i] is not { } effect || effect.End <= now) continue;
            float amplitude = effect.Strength * Math.Clamp(gain, 0, 1) * (i == 2 ? 1 : .5f);
            // Short renewable output bounds stale vibration if the game freezes or
            // the session disappears before its explicit stop callback is delivered.
            return new Output(amplitude, (float)Math.Min(.1, effect.End - now));
        }
        return default;
    }
}
