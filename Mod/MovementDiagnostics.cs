using UnityEngine;

namespace ManifoldProbe;

// Opt-in observation only. Never casts, moves colliders, changes input or recenters XR.
internal static class MovementDiagnostics
{
    static string? path;
    static double until, next;
    static int samples;
    internal static bool Due => path != null && Time.realtimeSinceStartupAsDouble >= next;

    internal static void Start()
    {
        Stop();
        path = Path.Combine(Probe.Output, $"movement-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
        until = Time.realtimeSinceStartupAsDouble + 30;
        next = 0;
        samples = 0;
        File.WriteAllText(path, "MGVR movement trace: 30 seconds, up to 10 samples/second; positions are world space except headOffset and input.\n");
        Probe.Write("MOVEMENT_TRACE_STARTED path=" + path);
    }

    internal static void Tick()
    {
        if (path != null && Time.realtimeSinceStartupAsDouble >= until) Stop();
    }

    internal static void Stop()
    {
        if (path == null) return;
        string finished = path;
        path = null;
        Probe.Write($"MOVEMENT_TRACE_STOPPED samples={samples} path={finished}");
    }

    internal static void Capture(Camera camera, Vector3 basePosition, Quaternion baseRotation,
        Vector3 headOffset, Vector3 leftEye, Vector3 rightEye)
    {
        if (path == null || Time.realtimeSinceStartupAsDouble < next) return;
        try
        {
            Tick();
            if (path == null) return;
            next = Time.realtimeSinceStartupAsDouble + .1;
            var player = Il2Cpp.GameManager.PlayerController;
            if (player == null) return;
            var rb = player.Rigidbody;
            var capsule = player.capsuleColl;
            var body = player.bodyTransform;
            string physics = rb == null ? "rigidbody=null" :
                $"rb={rb.position:F4} velocity={rb.velocity:F4} kinematic={rb.isKinematic} detectCollisions={rb.detectCollisions} collisionMode={rb.collisionDetectionMode}";
            string collider = capsule == null ? "capsule=null" :
                $"capsuleEnabled={capsule.enabled} trigger={capsule.isTrigger} layer={capsule.gameObject.layer} radius={capsule.radius:F4} height={capsule.height:F4} direction={capsule.direction} center={capsule.transform.TransformPoint(capsule.center):F4} scale={capsule.transform.lossyScale:F4}";
            var eyeCenter = (leftEye + rightEye) * .5f;
            string line = FormattableString.Invariant(
                $"t={Time.realtimeSinceStartupAsDouble:F3} frame={Time.frameCount} scene={camera.gameObject.scene.name} player={player.GetInstanceID()} gravity={player.gravityDirection} up={(body == null ? Vector3.zero : body.up):F4} movement={player.movement:F4} speed={player.moveSpeed:F3} running={player.isRunning} grounded={player.isGrounded} stairs={player.onStairs} stairsEnabled={player.debugHandleStairs} stairsDelta={player.stairsDelta:F4} maxStep={player.maxStepUp:F4} frozen={player.IsFrozen} noClip={player.InNoClipMode} {physics} {collider} base={basePosition:F4} baseRotation={baseRotation:F4} headOffset={headOffset:F4} tracked={camera.transform.position:F4} eyeL={leftEye:F4} eyeR={rightEye:F4} eyeDelta={(eyeCenter - basePosition):F4} {QuestInput.MovementState}\n");
            File.AppendAllText(path, line);
            samples++;
        }
        catch (Exception ex)
        {
            path = null;
            Probe.Write("MOVEMENT_TRACE_ERROR " + ex.Message);
        }
    }
}
