namespace ManifoldProbe;

// Screen projection and capture rules are shared with the offline replay checks.
internal static class MenuRayProjection
{
    internal static bool Project(float ox, float oy, float oz, float dx, float dy, float dz,
        int width, int height, out float x, out float y, out float distance, bool allowOutside = false)
    {
        x = y = distance = 0;
        if (width <= 0 || height <= 0 || !float.IsFinite(ox + oy + oz + dx + dy + dz) ||
            oz >= 0 || dz <= .0001f) return false;
        distance = -oz / dz;
        if (distance <= 0 || distance > 5) return false;
        float u = ox + dx * distance + .5f, v = oy + dy * distance + .5f;
        if (!allowOutside && (u < 0 || u > 1 || v < 0 || v > 1)) return false;
        x = u * width;
        y = v * height;
        return true;
    }
}

internal readonly record struct MenuPointerEvents(bool Press = false, bool Release = false,
    bool Click = false, bool BeginDrag = false, bool Drag = false, bool EndDrag = false);

internal sealed class MenuPointerCapture
{
    internal int PressOwner { get; private set; }
    internal int DragOwner { get; private set; }
    internal bool Dragging { get; private set; }
    float pressX, pressY;

    internal MenuPointerEvents Step(bool valid, bool held, bool down, bool up,
        int clickOwner, int dragOwner, float x, float y, bool noDragThreshold, float threshold)
    {
        if (!valid) return Cancel();
        if (down)
        {
            PressOwner = clickOwner;
            DragOwner = dragOwner;
            Dragging = false;
            pressX = x; pressY = y;
            return new(Press: PressOwner != 0 || DragOwner != 0);
        }
        bool begin = held && DragOwner != 0 && !Dragging &&
                     (noDragThreshold || (x - pressX) * (x - pressX) + (y - pressY) * (y - pressY) >=
                         threshold * threshold);
        if (begin) Dragging = true;
        if (up)
        {
            var events = new MenuPointerEvents(Release: PressOwner != 0,
                Click: !Dragging && PressOwner != 0 && PressOwner == clickOwner,
                EndDrag: Dragging);
            // Owners remain available until the adapter dispatches release events.
            return events;
        }
        return new(BeginDrag: begin, Drag: held && Dragging);
    }

    internal MenuPointerEvents Cancel() => new(Release: PressOwner != 0, EndDrag: Dragging);

    internal void Reset()
    {
        PressOwner = DragOwner = 0;
        Dragging = false;
    }
}
