namespace ManifoldProbe;

[Flags]
internal enum InteractionFeedback
{
    None = 0,
    CubeInRange = 1,
    CubeAvailable = 2,
    ObjectInRange = 4,
    ObjectAvailable = 8,
    Holding = 16
}

// These are the game's reticle states, not a second raycast eligibility test.
internal static class InteractionFeedbackPolicy
{
    internal static InteractionFeedback Select(bool gameplay, bool aimValid, bool holding,
        bool cubeInRange, bool cubeAvailable, bool objectInRange, bool objectAvailable)
    {
        if (!gameplay || !aimValid) return InteractionFeedback.None;
        if (holding) return InteractionFeedback.Holding;
        var result = InteractionFeedback.None;
        if (cubeInRange) result |= InteractionFeedback.CubeInRange;
        if (cubeAvailable) result |= InteractionFeedback.CubeAvailable;
        if (objectInRange) result |= InteractionFeedback.ObjectInRange;
        if (objectAvailable) result |= InteractionFeedback.ObjectAvailable;
        return result;
    }
}
