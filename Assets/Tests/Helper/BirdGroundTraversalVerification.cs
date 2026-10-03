#if UNITY_EDITOR
using System.Text;
using UnityEngine;

// Opt-in only. Call Run in Play mode; temporary collision probes are removed in finally.
// Does not move the player, change input devices, save scenes or touch progression.
public static class BirdGroundTraversalVerification
{
    public static string Run()
    {
        if (!Application.isPlaying) return "Run in Play mode.";
        var terrain = Object.FindFirstObjectByType<Terrain>();
        if (terrain == null) return "Missing farm terrain.";
        var ground = terrain.GetComponent<TerrainCollider>();
        var report = new StringBuilder();
        GameObject probe = null;
        int failures = 0;
        try
        {
            probe = new GameObject("BirdGroundTraversalProbe_TEMP");
            var body = probe.AddComponent<CharacterController>();
            foreach (var other in Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
                if (other != body) Physics.IgnoreCollision(body, other);
            body.enabled = false;
            body.slopeLimit = 45f;
            for (int form = 0; form < 2; form++)
            for (int mode = 0; mode < 2; mode++)
            {
                body.height = form == 0 ? .22f : .51f;
                body.radius = form == 0 ? .09f : .1785f;
                body.center = Vector3.up * (body.height * .5f);
                if (mode == 0)
                {
                    body.skinWidth = .001f;
                    body.stepOffset = 0f;
                    body.minMoveDistance = .001f;
                }
                else BirdGroundTraversal.Configure(body);
                int stalls = 0, paths = 0, frames = 0;
                for (int x = 0; x < 5; x++)
                for (int z = 0; z < 5; z++)
                for (int directionIndex = 0; directionIndex < 8; directionIndex++)
                {
                    var origin = new Vector3(103f + x * 5f, 30f, 10f + z * 5f);
                    if (!ground.Raycast(new Ray(origin, Vector3.down), out var hit, 80f)) continue;
                    Vector3 start = hit.point + Vector3.up * .02f;
                    Vector3 direction = Quaternion.Euler(0f, directionIndex * 45f, 0f) * Vector3.forward;
                    bool obstructed = false;
                    foreach (var block in Physics.CapsuleCastAll(start + Vector3.up * body.radius,
                        start + Vector3.up * (body.height - body.radius), body.radius, direction, 2f,
                        ~(1 << 9), QueryTriggerInteraction.Ignore))
                        if (block.collider != body && !(block.collider is TerrainCollider) &&
                            !(block.collider is CharacterController)) obstructed = true;
                    if (obstructed) continue; // Test bare terrain, not walking through scenery.
                    body.enabled = false;
                    probe.transform.position = start;
                    body.enabled = true;
                    for (int i = 0; i < 10; i++) body.Move(Vector3.down * .01f);
                    float verticalSpeed = -1.5f;
                    paths++;
                    const float dt = 1f / 120f;
                    for (int frame = 0; frame < 240; frame++)
                    {
                        Vector3 before = probe.transform.position;
                        if (body.isGrounded && verticalSpeed < 0f) verticalSpeed = -1.5f;
                        verticalSpeed -= 18f * dt;
                        body.Move((direction + Vector3.up * verticalSpeed) * dt);
                        float progress = Vector3.Dot(probe.transform.position - before, direction) / dt;
                        if (progress < .2f) stalls++;
                        frames++;
                    }
                }
                bool passed = paths > 100 && (mode == 0 || stalls == 0);
                if (!passed) failures++;
                report.AppendLine((passed ? "PASS " : "FAIL ") + (form == 0 ? "chick" : "chicken") +
                    (mode == 0 ? " old baseline" : " fixed traversal") +
                    " open paths=" + paths + " stalls=" + stalls + "/" + frames);
                body.enabled = false;
            }
        }
        finally
        {
            if (probe != null) Object.DestroyImmediate(probe);
        }
        return (failures == 0 ? "PASS\n" : "FAIL\n") + report;
    }
}
#endif
