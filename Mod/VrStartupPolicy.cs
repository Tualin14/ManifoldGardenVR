namespace ManifoldProbe;

// Pure timing rules shared by the runtime and the standalone regression checks.
internal sealed class VrStartupPolicy
{
    internal double NextAttempt { get; private set; }
    internal int Failures { get; private set; }
    internal bool Calibrated { get; private set; }
    double stableSince = double.NaN;
    double lastSample = double.NaN;

    internal void RetryNow()
    {
        NextAttempt = 0;
        Failures = 0;
    }

    internal void Failed(double now)
    {
        Failures++;
        NextAttempt = now + Math.Min(30, 5 * Math.Pow(2, Math.Min(Failures - 1, 3)));
    }

    internal void ResetPose()
    {
        stableSince = lastSample = double.NaN;
    }

    internal bool PoseReady(double now, bool valid, bool movedFromAnchor)
    {
        if (!valid)
        {
            ResetPose();
            return false;
        }

        if (double.IsNaN(stableSince) || movedFromAnchor || now - lastSample > 0.25 || now < lastSample)
            stableSince = now;
        lastSample = now;
        return now - stableSince >= 0.75;
    }

    // Tracking loss resets stability, while an explicit recenter or new
    // session/origin also invalidates the comfortable height/yaw reference.
    internal void Recenter()
    {
        Calibrated = false;
        ResetPose();
    }

    internal void MarkCalibrated() => Calibrated = true;
}