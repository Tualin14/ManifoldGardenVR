using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ManifoldProbe;

// Reuse the original reticle sprites and eligibility flags at the controller
// ray endpoint. Do not move the desktop canvas, run gameplay updates or
// render the world again. Both eyes draw the same world-space marker.
internal static class VrInteractionFeedback
{
    sealed record Visual(int SpriteId, Mesh Mesh, Material Material);
    static readonly Dictionary<int, Visual> visuals = new();
    static Il2Cpp.ReticleController? reticle;
    static InteractionFeedback state;
    static Vector3 position;
    static Quaternion rotation;
    static float diameter, referenceWidth;
    static int preparedFrame = -1, ownerId;
    static bool reported, errorReported;
    static double retryAt;

    internal static void Clear()
    {
        state = InteractionFeedback.None;
        preparedFrame = -1;
        reticle = null;
    }

    internal static void Prepare(Vector3 endpoint, Vector3 headPosition, Quaternion headRotation)
    {
        Clear();
        if (Time.realtimeSinceStartupAsDouble < retryAt) return;
        try
        {
            var ui = Il2Cpp.GameManager.UI;
            reticle = ui == null ? null : ui.reticleController;
            if (reticle == null) return;
            if (ownerId != reticle.GetInstanceID())
            {
                ReleaseVisuals();
                ownerId = reticle.GetInstanceID();
            }
            var carry = Il2Cpp.GameManager.PlayerCarry;
            state = InteractionFeedbackPolicy.Select(true, true, carry != null && carry.CurrentCube != null,
                reticle.withinDistanceState, reticle.ableToPickupState,
                reticle.operateWithinDistanceState, reticle.operateAbleToPickupState);
            if (state == InteractionFeedback.None) return;

            // Keep stereo depth at the ray endpoint, with a small offset toward
            // the head so opaque geometry cannot clip the icon at the surface.
            var direction = endpoint - headPosition;
            float distance = direction.magnitude;
            if (distance < .05f || !float.IsFinite(distance)) { Clear(); return; }
            position = endpoint - direction.normalized * .015f;
            rotation = Quaternion.LookRotation(direction, headRotation * Vector3.up);
            diameter = Math.Clamp(distance * .035f, .025f, .12f);
            referenceWidth = reticle.outerCircleImage != null
                ? Math.Abs(reticle.outerCircleImage.rectTransform.rect.width) : 0;
            if (referenceWidth < 1) referenceWidth = 64;
            preparedFrame = Time.frameCount;
            if (!reported)
            {
                reported = true;
                Probe.Write("VR_INTERACTION_FEEDBACK_READY source=native-reticle sprites=original anchor=right-ray");
            }
        }
        catch (Exception ex)
        {
            RetryAfterFailure("PREPARE", ex);
        }
    }

    internal static void Draw(CommandBuffer command)
    {
        if (reticle == null || preparedFrame != Time.frameCount || state == InteractionFeedback.None)
            return;
        try
        {
            DrawImage(command, reticle.innerCircleImage, .85f);
            if ((state & InteractionFeedback.CubeInRange) != 0)
                DrawImage(command, reticle.outerWithinDistance, .55f);
            if ((state & InteractionFeedback.CubeAvailable) != 0)
                DrawImage(command, reticle.outerAbleToPickUp, 1);
            if ((state & InteractionFeedback.ObjectInRange) != 0)
                DrawImage(command, reticle.operateWithinDistance, .55f);
            if ((state & InteractionFeedback.ObjectAvailable) != 0)
                DrawImage(command, reticle.operateAvaliable, 1);
            if ((state & InteractionFeedback.Holding) != 0)
                DrawImage(command, reticle.outerCurrentlyHolding, 1);
            if (errorReported)
            {
                errorReported = false;
                Probe.Write("VR_INTERACTION_FEEDBACK_RECOVERED");
            }
        }
        catch (Exception ex)
        {
            RetryAfterFailure("DRAW", ex);
        }
    }

    static void DrawImage(CommandBuffer command, Image? image, float alpha)
    {
        if (image == null) return;
        var sprite = image.overrideSprite ?? image.sprite;
        if (sprite == null || sprite.texture == null) return;
        int id = image.GetInstanceID();
        // A managed cache entry may outlive its native Unity objects after a
        // menu/save unload. Check Unity object lifetime, not just sprite identity.
        if (visuals.TryGetValue(id, out var existing) &&
            (existing.SpriteId != sprite.GetInstanceID() || existing.Mesh == null || existing.Material == null))
        {
            if (existing.Mesh != null) Object.Destroy(existing.Mesh);
            if (existing.Material != null) Object.Destroy(existing.Material);
            visuals.Remove(id);
        }
        if (!visuals.TryGetValue(id, out var visual))
        {
            var shader = Shader.Find("UI/Default") ?? throw new InvalidOperationException("Reticle shader unavailable");
            // These resources are referenced by command buffers, not scene
            // renderers; retain them across Resources.UnloadUnusedAssets.
            var mesh = new Mesh { name = "MGVR original reticle " + sprite.name,
                hideFlags = HideFlags.HideAndDontSave };
            var material = new Material(shader) { name = "MGVR original reticle " + sprite.name,
                hideFlags = HideFlags.HideAndDontSave };
            try
            {
                // Sprite UVs include atlas packing. Normalizing against the
                // untrimmed rect preserves the original padding and aspect.
                float width = sprite.rect.width, height = sprite.rect.height;
                if (width <= 0 || height <= 0) throw new InvalidOperationException("Empty reticle sprite");
                mesh.vertices = new Il2CppStructArray<Vector3>(sprite.vertices.Select(vertex =>
                    new Vector3(vertex.x * sprite.pixelsPerUnit / width,
                        vertex.y * sprite.pixelsPerUnit / height, 0)).ToArray());
                mesh.uv = sprite.uv;
                mesh.colors = new Il2CppStructArray<Color>(Enumerable.Repeat(Color.white, mesh.vertexCount).ToArray());
                mesh.triangles = new Il2CppStructArray<int>(sprite.triangles.Select(index => (int)index).ToArray());
                mesh.RecalculateBounds();
                material.SetTexture("_MainTex", sprite.texture);
                // Original reticles are overlay UI. Eligibility comes from
                // the game, so a wall cannot produce an available marker.
                material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
                visual = new Visual(sprite.GetInstanceID(), mesh, material);
                visuals.Add(id, visual);
            }
            catch { Object.Destroy(mesh); Object.Destroy(material); throw; }
        }

        var tint = image.color;
        // Native desktop fade is coupled to the flat reticle display mode.
        // The VR marker follows eligibility directly, including when that
        // desktop animation has faded out; keep its original RGB palette.
        tint.a = alpha;
        visual.Material.color = tint;
        var rect = image.rectTransform.rect;
        float widthScale = rect.width > 0 ? rect.width / referenceWidth : 1;
        float heightScale = rect.height > 0 ? rect.height / referenceWidth : 1;
        var matrix = Matrix4x4.TRS(position, rotation,
            new Vector3(diameter * widthScale, diameter * heightScale, 1));
        command.DrawMesh(visual.Mesh, matrix, visual.Material, 0, 0);
    }

    internal static void Release()
    {
        Clear();
        ReleaseVisuals();
        ownerId = 0;
        reported = errorReported = false;
        retryAt = 0;
    }

    static void RetryAfterFailure(string stage, Exception ex)
    {
        Clear();
        ReleaseVisuals();
        retryAt = Time.realtimeSinceStartupAsDouble + 1;
        if (errorReported) return;
        errorReported = true;
        Probe.Write("VR_INTERACTION_FEEDBACK_" + stage + "_ERROR retry=1s " + ex);
    }

    static void ReleaseVisuals()
    {
        foreach (var visual in visuals.Values)
        {
            if (visual.Mesh != null) Object.Destroy(visual.Mesh);
            if (visual.Material != null) Object.Destroy(visual.Material);
        }
        visuals.Clear();
    }
}
