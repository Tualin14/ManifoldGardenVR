using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ManifoldProbe;

// One synthetic mouse pointer, projected from the VR panel into the original
// overlay. Native controls keep their own callbacks, popup rules and values.
internal static class VrMenuPointer
{
    static readonly MenuPointerCapture capture = new();
    static readonly List<GameObject> hoverChain = new();
    static EventSystem? owner;
    static PointerEventData? data;
    static Il2CppSystem.Collections.Generic.List<RaycastResult>? results;
    static GameObject? pressed, dragged;
    static Mesh? beam, dot;
    static Material? material;
    static Matrix4x4 beamMatrix, dotMatrix;
    static bool visible, hitPanel, hot, held;
    static int lastFrame = -1;
    static double retryAt;

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var patches = new List<MethodInfo>();
        var prefix = AccessTools.Method(typeof(VrMenuPointer), nameof(NativeProcess));
        try
        {
            foreach (var type in new[] { typeof(Il2Cpp.ControllerInputModule),
                         typeof(Il2CppRewired.Integration.UnityUI.RewiredStandaloneInputModule),
                         typeof(StandaloneInputModule) })
            {
                var method = AccessTools.DeclaredMethod(type, "Process") ??
                             throw new MissingMethodException(type.Name, "Process");
                harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                patches.Add(method);
            }
            Probe.Write("VR_MENU_RAY_HOOKS_READY modules=3 source=right-controller");
        }
        catch
        {
            foreach (var method in patches) harmony.Unpatch(method, prefix);
            throw;
        }
    }

    // No focus flags or input-map changes. Flat mode never installs this hook.
    static bool NativeProcess() => !QuestInput.RayMenuActive;

    internal static void Tick()
    {
        visible = hitPanel = hot = held = false;
        try
        {
            var state = QuestInput.MenuPointerState;
            if (Time.realtimeSinceStartupAsDouble < retryAt || !state.valid ||
                !OpenXRRenderer.TryMenuControllerPose(out var origin, out var rotation) ||
                EventSystem.current == null)
            {
                Cancel();
                return;
            }
            if (owner != EventSystem.current)
            {
                Cancel();
                owner = EventSystem.current;
                data = new PointerEventData(owner) { pointerId = -1, button = PointerEventData.InputButton.Left,
                    useDragThreshold = true };
                results = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
            }
            lastFrame = Time.frameCount;
            var ray = new Ray(origin, rotation * Vector3.forward);
            bool projected = VrMenu.Project(ray, out var screen, out var hit, out var inside);
            float length = projected && inside ? Vector3.Distance(origin, hit) : 2;
            beamMatrix = Matrix4x4.TRS(origin, rotation, new Vector3(1, 1, length));
            visible = true;
            hitPanel = projected && inside;
            if (hitPanel)
                dotMatrix = Matrix4x4.TRS(hit - ray.direction * .004f, rotation, Vector3.one * .009f);

            var previousPosition = data!.position;
            if (projected) data.position = screen;
            data.delta = data.position - previousPosition;
            results!.Clear();
            if (hitPanel) owner!.RaycastAll(data, results);
            var result = new RaycastResult();
            for (int i = 0; i < results.Count; i++)
            {
                var candidate = results[i];
                if (candidate.gameObject != null && candidate.module != null &&
                    candidate.module.TryCast<GraphicRaycaster>() != null)
                {
                    result = candidate;
                    break; // Sorted by Unity: topmost popup/canvas blocks controls below it.
                }
            }
            data.pointerCurrentRaycast = result;
            var target = result.gameObject;
            Hover(target);
            var click = Handler<IPointerClickHandler>(target);
            var down = Handler<IPointerDownHandler>(target) ?? click;
            var drag = Handler<IDragHandler>(target);
            hot = click != null || drag != null || down != null;
            held = state.held;

            // An inactive/destroyed control cannot keep a press across a page transition.
            if (pressed != null && !pressed.activeInHierarchy || dragged != null && !dragged.activeInHierarchy)
                CancelPress();
            var events = capture.Step(true, state.held, state.down, state.up,
                state.down ? Id(down) : Id(click), Id(drag), data.position.x, data.position.y,
                !data.useDragThreshold, owner!.pixelDragThreshold);
            if (events.Press)
            {
                pressed = down;
                dragged = drag;
                data.pressPosition = data.position;
                data.pointerPressRaycast = result;
                data.pointerPress = pressed;
                data.rawPointerPress = target;
                data.pointerDrag = dragged;
                data.eligibleForClick = true;
                data.dragging = false;
                data.useDragThreshold = true;
                data.clickCount = 1;
                data.clickTime = Time.unscaledTime;
                if (pressed != null) owner.SetSelectedGameObject(pressed, data);
                Dispatch<IPointerDownHandler>(pressed, h => h.OnPointerDown(data));
                Dispatch<IInitializePotentialDragHandler>(dragged, h => h.OnInitializePotentialDrag(data));
            }
            if (events.BeginDrag)
            {
                data.dragging = true;
                data.eligibleForClick = false;
                Dispatch<IBeginDragHandler>(dragged, h => h.OnBeginDrag(data));
            }
            if (events.Drag) Dispatch<IDragHandler>(dragged, h => h.OnDrag(data));
            if (state.up)
            {
                Dispatch<IPointerUpHandler>(pressed, h => h.OnPointerUp(data));
                if (events.Click && data.eligibleForClick)
                {
                    Probe.Write("VR_MENU_RAY_CLICK target=" + pressed?.name);
                    Dispatch<IPointerClickHandler>(pressed, h => h.OnPointerClick(data));
                }
                if (events.EndDrag) Dispatch<IEndDragHandler>(dragged, h => h.OnEndDrag(data));
                ResetPress();
            }
        }
        catch (Exception ex)
        {
            visible = false;
            retryAt = Time.realtimeSinceStartupAsDouble + 1;
            try { Cancel(); } catch { ResetPress(); hoverChain.Clear(); }
            Probe.Write("VR_MENU_RAY_ERROR retry=1s " + ex);
        }
    }

    static int Id(GameObject? go) => go == null ? 0 : go.GetInstanceID();

    static GameObject? Handler<T>(GameObject? go) where T : Il2CppObjectBase
    {
        for (var current = go == null ? null : go.transform; current != null; current = current.parent)
            foreach (var component in current.gameObject.GetComponents(Il2CppType.Of<Component>()))
            {
                if (component == null || component.TryCast<Behaviour>() is { } b && !b.isActiveAndEnabled) continue;
                if (component.TryCast<Selectable>() is { } selectable && !selectable.IsInteractable()) continue;
                if (component.TryCast<T>() != null) return current.gameObject;
            }
        return null;
    }

    // Avoid stripped ExecuteEvents<T> AOT instantiations; call the game's native
    // interface implementations directly, including its custom button subclasses.
    static void Dispatch<T>(GameObject? go, Action<T> action) where T : Il2CppObjectBase
    {
        if (go == null || !go.activeInHierarchy) return;
        foreach (var component in go.GetComponents(Il2CppType.Of<Component>()))
        {
            if (component == null || component.TryCast<Behaviour>() is { } b && !b.isActiveAndEnabled) continue;
            var handler = component.TryCast<T>();
            if (handler != null) action(handler);
        }
    }

    static void Hover(GameObject? target)
    {
        var next = new List<GameObject>();
        for (var current = target == null ? null : target.transform; current != null; current = current.parent)
            next.Add(current.gameObject);
        foreach (var go in hoverChain)
            if (!next.Any(n => n == go)) Dispatch<IPointerExitHandler>(go, h => h.OnPointerExit(data!));
        data!.pointerEnter = target;
        foreach (var go in next)
            if (!hoverChain.Any(n => n == go)) Dispatch<IPointerEnterHandler>(go, h => h.OnPointerEnter(data));
        hoverChain.Clear();
        hoverChain.AddRange(next);
    }

    static void ResetPress()
    {
        capture.Reset();
        pressed = dragged = null;
        if (data == null) return;
        data.pointerPress = data.rawPointerPress = data.pointerDrag = null;
        data.eligibleForClick = data.dragging = false;
    }

    static void CancelPress()
    {
        if (data != null)
        {
            Dispatch<IPointerUpHandler>(pressed, h => h.OnPointerUp(data));
            if (capture.Dragging) Dispatch<IEndDragHandler>(dragged, h => h.OnEndDrag(data));
        }
        ResetPress();
    }

    static void Cancel()
    {
        CancelPress();
        if (data != null) Hover(null);
    }

    internal static void Draw(CommandBuffer cmd)
    {
        if (!visible || !QuestInput.RayMenuActive || Time.frameCount - lastFrame > 1) return;
        try { DrawGraphics(cmd); }
        catch (Exception ex)
        {
            visible = false;
            retryAt = Time.realtimeSinceStartupAsDouble + 1;
            ReleaseGraphics();
            Probe.Write("VR_MENU_RAY_DRAW_ERROR retry=1s " + ex);
        }
    }

    static void DrawGraphics(CommandBuffer cmd)
    {
        if (material == null || beam == null || dot == null)
        {
            ReleaseGraphics();
            var shader = Shader.Find("UI/Default") ?? throw new InvalidOperationException("Menu ray shader unavailable");
            material = new Material(shader) { name = "MGVR menu ray", hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            beam = new Mesh { name = "MGVR menu beam", hideFlags = HideFlags.HideAndDontSave };
            beam.vertices = new Vector3[] { new(-.0015f, 0, 0), new(.0015f, 0, 0), new(.0015f, 0, 1), new(-.0015f, 0, 1),
                new(0, -.0015f, 0), new(0, .0015f, 0), new(0, .0015f, 1), new(0, -.0015f, 1) };
            beam.colors = Enumerable.Repeat(Color.white, 8).ToArray();
            beam.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
            beam.RecalculateBounds();
            dot = new Mesh { name = "MGVR menu dot", hideFlags = HideFlags.HideAndDontSave };
            var vertices = new Vector3[17]; var triangles = new int[48];
            for (int i = 0; i < 16; i++)
            {
                double angle = i * Math.PI / 8;
                vertices[i + 1] = new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), 0);
                triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = (i + 1) % 16 + 1;
            }
            dot.vertices = vertices; dot.triangles = triangles;
            dot.colors = Enumerable.Repeat(Color.white, 17).ToArray();
            dot.RecalculateBounds();
        }
        material!.color = held ? new Color(.9f, .36f, .08f, 1) :
            hot ? new Color(.05f, .5f, .55f, 1) : new Color(.25f, .23f, .2f, .75f);
        cmd.DrawMesh(beam!, beamMatrix, material, 0, 0);
        if (hitPanel) cmd.DrawMesh(dot!, dotMatrix, material, 0, 0);
    }

    static void ReleaseGraphics()
    {
        if (beam != null) Object.Destroy(beam);
        if (dot != null) Object.Destroy(dot);
        if (material != null) Object.Destroy(material);
        beam = dot = null; material = null;
    }

    internal static void Release()
    {
        Cancel();
        owner = null; data = null; results = null;
        visible = false; retryAt = 0;
        ReleaseGraphics();
    }
}
