namespace ManifoldProbe;

// Stick clicks and the system/menu buttons deliberately cannot be assigned.
public enum VrButton
{
    LeftTrigger,
    LeftGrip,
    RightTrigger,
    RightGrip,
    A,
    B,
    X
}

public enum VrTurnMode
{
    Snap,
    Smooth
}

internal static class VrBindings
{
    internal static readonly ControllerButtons[] Actions =
    {
        ControllerButtons.Interact, ControllerButtons.Gravity, ControllerButtons.Run,
        ControllerButtons.Clockwise, ControllerButtons.Counterclockwise
    };

    internal static VrButton Get(VrSettings settings, ControllerButtons action) => action switch
    {
        ControllerButtons.Interact => settings.InteractBinding,
        ControllerButtons.Gravity => settings.GravityBinding,
        ControllerButtons.Run => settings.RunBinding,
        ControllerButtons.Clockwise => settings.ClockwiseBinding,
        ControllerButtons.Counterclockwise => settings.CounterclockwiseBinding,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    static void Set(VrSettings settings, ControllerButtons action, VrButton button)
    {
        switch (action)
        {
            case ControllerButtons.Interact: settings.InteractBinding = button; break;
            case ControllerButtons.Gravity: settings.GravityBinding = button; break;
            case ControllerButtons.Run: settings.RunBinding = button; break;
            case ControllerButtons.Clockwise: settings.ClockwiseBinding = button; break;
            case ControllerButtons.Counterclockwise: settings.CounterclockwiseBinding = button; break;
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    internal static bool Valid(VrSettings settings)
    {
        var used = new HashSet<VrButton>();
        foreach (var action in Actions)
        {
            var button = Get(settings, action);
            if (!Enum.IsDefined(typeof(VrButton), button) || !used.Add(button)) return false;
        }

        return true;
    }

    // Assigning an occupied button swaps the two actions, so none becomes inaccessible.
    internal static void Rebind(VrSettings settings, ControllerButtons action, VrButton button)
    {
        if (!Actions.Contains(action) || !Enum.IsDefined(typeof(VrButton), button))
            throw new ArgumentOutOfRangeException();
        var previous = Get(settings, action);
        foreach (var other in Actions)
            if (other != action && Get(settings, other) == button)
                Set(settings, other, previous);
        Set(settings, action, button);
    }

    internal static bool IsLeft(VrButton button) =>
        button is VrButton.LeftTrigger or VrButton.LeftGrip or VrButton.X;

    internal static void Reset(VrSettings settings)
    {
        ResetTurning(settings);
        ResetControls(settings);
    }

    internal static void ResetTurning(VrSettings settings)
    {
        settings.TurnMode = VrTurnMode.Snap;
        settings.SnapTurnDegrees = 45;
        settings.SmoothTurnDegreesPerSecond = 90;
    }

    internal static void ResetControls(VrSettings settings)
    {
        settings.InteractBinding = VrButton.RightTrigger;
        settings.GravityBinding = VrButton.RightGrip;
        settings.RunBinding = VrButton.LeftGrip;
        settings.ClockwiseBinding = VrButton.A;
        settings.CounterclockwiseBinding = VrButton.B;
    }
}