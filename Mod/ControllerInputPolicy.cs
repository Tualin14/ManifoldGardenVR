namespace ManifoldProbe;

internal enum ControllerContext
{
    Blocked,
    Gameplay,
    Menu
}

internal readonly record struct HandInput(
    ulong Device,
    bool Valid,
    float X = 0,
    float Y = 0,
    float Trigger = 0,
    bool Primary = false,
    bool Secondary = false,
    bool Menu = false,
    bool StickClick = false,
    float Grip = 0);

[Flags]
internal enum ControllerButtons
{
    None = 0,
    Interact = 1,
    Gravity = 2,
    Run = 4,
    Pause = 8,
    Clockwise = 16,
    Counterclockwise = 32,
    Confirm = 64,
    Cancel = 128,
    ReturnToGame = 256,
    Right = 512,
    Left = 1024,
    Up = 2048,
    Down = 4096,
    TabLeft = 8192,
    TabRight = 16384,
    LevelSelect = 32768,
    MenuClick = 65536
}

// No Unity dependency: the runtime adapter and replay checks use the same state machine.
internal sealed class ControllerInputPolicy
{
    sealed class HandGate
    {
        ulong device;
        bool armed;

        internal bool Accept(HandInput hand, float deadzone)
        {
            if (!hand.Valid || hand.Device != device)
            {
                armed = false;
                device = hand.Device;
            }

            if (!hand.Valid) return false;
            if (!float.IsFinite(hand.X) || !float.IsFinite(hand.Y) || !float.IsFinite(hand.Trigger) ||
                !float.IsFinite(hand.Grip))
            {
                armed = false;
                return false;
            }

            if (!armed)
            {
                armed = hand.X * hand.X + hand.Y * hand.Y <= deadzone * deadzone && hand.Trigger < .2f &&
                        hand.Grip < .2f && !hand.Primary && !hand.Secondary && !hand.Menu;
                return false;
            }

            return true;
        }

        internal void Reset()
        {
            armed = false;
            device = 0;
        }
    }

    readonly HandGate leftGate = new(), rightGate = new();
    ControllerContext context;
    double lastTime = double.NaN;
    readonly bool[] buttonHeld = new bool[7];
    bool turnLatched;
    bool menuTriggerHeld;
    double recenterSince = double.NaN;
    bool recenterLatched;
    ControllerButtons held;
    internal ControllerButtons Held => held;
    internal ControllerButtons Down { get; private set; }
    internal ControllerButtons Up { get; private set; }
    internal float MoveX { get; private set; }
    internal float MoveY { get; private set; }
    internal float TurnDegrees { get; private set; }
    internal bool Recenter { get; private set; }

    internal void Reset()
    {
        leftGate.Reset();
        rightGate.Reset();
        held = Down = Up = 0;
        MoveX = MoveY = TurnDegrees = 0;
        Array.Clear(buttonHeld, 0, buttonHeld.Length);
        turnLatched = false;
        menuTriggerHeld = false;
        recenterSince = double.NaN;
        recenterLatched = Recenter = false;
        lastTime = double.NaN;
    }

    internal void Step(double now, ControllerContext nextContext, HandInput left, HandInput right,
        float deadzone, float snapDegrees, VrSettings? settings = null)
    {
        if (context != nextContext || !double.IsFinite(now) ||
            (double.IsFinite(lastTime) && (now < lastTime || now - lastTime > .25))) Reset();
        context = nextContext;
        double elapsed = double.IsFinite(lastTime) ? Math.Clamp(now - lastTime, 0, .05) : 0;
        lastTime = now;
        MoveX = MoveY = TurnDegrees = 0;
        Recenter = false;
        Down = Up = 0;
        if (context == ControllerContext.Blocked || !double.IsFinite(now))
        {
            Reset();
            return;
        }

        bool leftReady = leftGate.Accept(left, deadzone), rightReady = rightGate.Accept(right, deadzone);
        bool recenterChord = leftReady && rightReady && left.Secondary &&
                             left.X * left.X + left.Y * left.Y <= deadzone * deadzone &&
                             !left.Primary && !left.Menu && left.Trigger < .2f && left.Grip < .2f &&
                             right.X * right.X + right.Y * right.Y <= deadzone * deadzone &&
                             !right.Primary && !right.Secondary && right.Trigger < .2f && right.Grip < .2f;
        if (!recenterChord)
        {
            recenterSince = double.NaN;
        }
        else
        {
            if (double.IsNaN(recenterSince)) recenterSince = now;
            if (!recenterLatched && now - recenterSince >= 1)
                Recenter = recenterLatched = true;
        }

        var next = ControllerButtons.None;
        if (!leftReady || !left.Secondary) recenterLatched = false;
        if (leftReady)
        {
            if (left.Primary && context == ControllerContext.Gameplay)
            {
                bool assigned = false;
                foreach (var action in VrBindings.Actions)
                    assigned |= VrBindings.Get(settings ?? defaults, action) == VrButton.X;
                if (!assigned) next |= ControllerButtons.LevelSelect;
            }
            if (context == ControllerContext.Gameplay)
                (MoveX, MoveY) = Deadzone(left.X, left.Y, deadzone);

            if (left.Menu)
                next |= context == ControllerContext.Menu ? ControllerButtons.ReturnToGame : ControllerButtons.Pause;
        }

        if (rightReady)
        {
            if (context == ControllerContext.Menu)
            {
                menuTriggerHeld = right.Trigger >= (menuTriggerHeld ? .45f : .65f);
                if (menuTriggerHeld) next |= ControllerButtons.MenuClick;
            }
            else
            {
                if (settings?.TurnMode == VrTurnMode.Smooth)
                {
                    if (Math.Abs(right.X) > deadzone && Math.Abs(right.X) > Math.Abs(right.Y))
                        TurnDegrees = Math.Sign(right.X) * (Math.Min(Math.Abs(right.X), 1) - deadzone) /
                            (1 - deadzone) * settings.SmoothTurnDegreesPerSecond * (float)elapsed;
                }
                else
                {
                    if (Math.Abs(right.X) <= .25f) turnLatched = false;
                    if (!turnLatched && Math.Abs(right.X) >= .7f && Math.Abs(right.X) > Math.Abs(right.Y))
                    {
                        TurnDegrees = Math.Sign(right.X) * snapDegrees;
                        turnLatched = true;
                    }
                }
            }
        }
        else
        {
            turnLatched = false;
            menuTriggerHeld = false;
        }

        settings ??= defaults;
        ControllerButtons lost = ControllerButtons.None;
        foreach (var action in VrBindings.Actions)
        {
            var source = VrBindings.Get(settings, action);
            bool ready = VrBindings.IsLeft(source) ? leftReady : rightReady;
            if (!ready) lost |= action;
            int index = (int)source;
            var hand = VrBindings.IsLeft(source) ? left : right;
            float value = source switch
            {
                VrButton.LeftTrigger or VrButton.RightTrigger => hand.Trigger,
                VrButton.LeftGrip or VrButton.RightGrip => hand.Grip,
                VrButton.A or VrButton.X => hand.Primary ? 1 : 0,
                VrButton.B => hand.Secondary ? 1 : 0,
                _ => 0
            };
            buttonHeld[index] = ready && context == ControllerContext.Gameplay &&
                                value >= (buttonHeld[index] ? .45f : .65f);
            if (buttonHeld[index]) next |= action;
        }

        Down = next & ~held;
        Up = held & ~next;
        // Losing a source clears held actions, but must not synthesize click-on-release actions.
        if (!leftReady)
            Up &= ~(ControllerButtons.LevelSelect | ControllerButtons.Pause |
                    ControllerButtons.ReturnToGame | ControllerButtons.Right | ControllerButtons.Left |
                    ControllerButtons.Up | ControllerButtons.Down | ControllerButtons.TabLeft);
        if (!rightReady)
            Up &= ~(ControllerButtons.Confirm | ControllerButtons.Cancel | ControllerButtons.TabRight |
                    ControllerButtons.MenuClick);
        Up &= ~lost;
        held = next;
    }

    static readonly VrSettings defaults = new();

    internal static (float x, float y) Deadzone(float x, float y, float deadzone)
    {
        x = Math.Clamp(x, -1, 1);
        y = Math.Clamp(y, -1, 1);
        float magnitude = MathF.Sqrt(x * x + y * y);
        if (magnitude <= deadzone) return (0, 0);
        float scale = (Math.Min(magnitude, 1) - deadzone) / (1 - deadzone) / magnitude;
        return (x * scale, y * scale);
    }

    internal static ControllerButtons Action(string key) => key switch
    {
        "Interact" => ControllerButtons.Interact,
        "Change Gravity" => ControllerButtons.Gravity,
        "Run" => ControllerButtons.Run,
        "Pause" => ControllerButtons.Pause,
        "Rotate Item Clockwise" => ControllerButtons.Clockwise,
        "Rotate Item Counterclockwise" => ControllerButtons.Counterclockwise,
        "UI Confirm" => ControllerButtons.Confirm,
        "UI Cancel" => ControllerButtons.Cancel,
        "UI Return To Game" => ControllerButtons.ReturnToGame,
        "UI Tab Left" => ControllerButtons.TabLeft,
        "UI Tab Right" => ControllerButtons.TabRight,
        _ => ControllerButtons.None
    };
}
