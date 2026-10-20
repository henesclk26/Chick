using System.Collections.Generic;
using UnityEngine;

// A frog living on the pond's lily pads. Everything it does is a hop, with squash and stretch: it crouches, springs
// (stretching long), lands (squashing flat) and settles back. Sitting, its throat pumps quickly and its body sways a
// little. Now and then it hops round to face a new way, or jumps in an arc to another free lily pad nearby; it watches
// the chick when the chick is near and, if the chick comes too close, leaps to the pad farthest from it. Nothing eats
// it. The root moves and turns; the visual child is squashed, stretched and tilted.
[DisallowMultipleComponent]
public sealed class FrogIdle : MonoBehaviour
{
    [SerializeField, Tooltip("The model. Defaults to the first child.")]
    private Transform visual;
    [SerializeField, Tooltip("Seconds per throat pump.")]
    private float breathPeriod = .9f;
    [SerializeField, Range(0f, .15f)] private float breathAmount = .035f;
    [SerializeField, Tooltip("Watches the chick inside this distance (m).")]
    private float watchRadius = 1.8f;
    [SerializeField, Tooltip("Leaps away when the chick comes this close (m).")]
    private float fleeRadius = .7f;
    [SerializeField, Tooltip("Seconds between little turning hops when nothing is going on.")]
    private Vector2 turnEvery = new Vector2(4f, 10f);
    [SerializeField, Tooltip("Seconds between jumps to another lily pad.")]
    private Vector2 padJumpEvery = new Vector2(15f, 40f);
    [SerializeField, Tooltip("Lily pads further than this (m) are out of reach.")]
    private float padReach = 3.5f;

    private enum Phase { Sit, Crouch, Air, Land }

    // Lily pads of the pond, shared by the frogs; a pad with a frog on it is taken.
    private static readonly List<Collider> Pads = new List<Collider>();
    private static readonly HashSet<Collider> Taken = new HashSet<Collider>();

    private Transform chick;
    private Vector3 restScale, restPosition;
    private Phase phase;
    private float t, nextTurn, nextPadJump, breathPhase, jumpDuration, jumpHeight;
    private Vector3 from, to;
    private float fromYaw, toYaw;
    private Collider pad;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPads() { Pads.Clear(); Taken.Clear(); }

    private void Start()
    {
        if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
        if (visual != null) { restScale = visual.localScale; restPosition = visual.localPosition; }
        var player = FindFirstObjectByType<ChickPlayerController>();
        if (player != null) chick = player.transform;
        breathPhase = Random.value * 10f;
        nextTurn = Time.time + Random.Range(turnEvery.x, turnEvery.y);
        nextPadJump = Time.time + Random.Range(padJumpEvery.x, padJumpEvery.y);
        FindPads();
        pad = NearestPad(transform.position, 1f);
        if (pad != null) Taken.Add(pad);
    }

    private void OnDisable()
    {
        if (pad != null) Taken.Remove(pad);
    }

    private void FindPads()
    {
        foreach (Collider c in Physics.OverlapSphere(transform.position, padReach * 3f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (Pads.Contains(c) || !(c is MeshCollider)) continue;
            for (Transform p = c.transform; p != null; p = p.parent)
                if (p.name.Contains("Lief") || p.name.Contains("Lily_Leaf")) { Pads.Add(c); break; }
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        t += dt;
        switch (phase)
        {
            case Phase.Sit: Sit(); break;
            case Phase.Crouch:
                // Gathering itself: low and wide.
                Squash(Mathf.Sin(Mathf.Clamp01(t / .16f) * Mathf.PI * .5f) * -.22f);
                if (t >= .16f) { phase = Phase.Air; t = 0f; }
                break;
            case Phase.Air:
            {
                float f = Mathf.Clamp01(t / jumpDuration);
                Vector3 p = Vector3.Lerp(from, to, f);
                p.y += Mathf.Sin(f * Mathf.PI) * jumpHeight;
                transform.position = p;
                transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(fromYaw, toYaw, Mathf.SmoothStep(0f, 1f, f)), 0f);
                // Long in the air, nose up as it rises and down as it falls.
                Squash(.18f * Mathf.Sin(f * Mathf.PI));
                if (visual != null) visual.localRotation = Quaternion.Euler(Mathf.Lerp(-18f, 14f, f) * (jumpHeight > .05f ? 1f : .3f), 0f, 0f);
                if (f >= 1f) { phase = Phase.Land; t = 0f; }
                break;
            }
            case Phase.Land:
            {
                // Splat flat, then spring back up a touch past its shape and settle.
                float f = Mathf.Clamp01(t / .3f);
                Squash(-.25f * Mathf.Exp(-f * 5f) * Mathf.Cos(f * Mathf.PI * 1.5f));
                if (visual != null) visual.localRotation = Quaternion.Slerp(visual.localRotation, Quaternion.identity, 1f - Mathf.Exp(-12f * dt));
                if (f >= 1f) { phase = Phase.Sit; t = 0f; if (visual != null) visual.localRotation = Quaternion.identity; }
                break;
            }
        }
    }

    private void Sit()
    {
        // Breathing: quick throat pumps (a little taller and wider at the front), a slow sway.
        float b = Mathf.Pow(Mathf.Max(0f, Mathf.Sin((Time.time + breathPhase) * Mathf.PI * 2f / breathPeriod)), 3f) * breathAmount;
        if (visual != null)
        {
            visual.localScale = new Vector3(restScale.x * (1f + b * .7f), restScale.y * (1f + b), restScale.z * (1f + b * .2f));
            visual.localPosition = restPosition;
            float sway = Mathf.Sin((Time.time + breathPhase) * .7f);
            visual.localRotation = Quaternion.Euler(sway * 2.5f, 0f, sway * 1.5f);
        }

        float chickDistance = chick != null ? Flat(chick.position - transform.position) : float.MaxValue;
        if (chickDistance < fleeRadius) { JumpToPad(away: true); return; }
        float yaw = transform.eulerAngles.y;
        if (chickDistance < watchRadius)
        {
            Vector3 toChick = chick.position - transform.position;
            float wanted = Mathf.Atan2(toChick.x, toChick.z) * Mathf.Rad2Deg;
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, wanted)) > 35f) Hop(transform.position, wanted, .025f, .22f);
            return;
        }
        if (Time.time >= nextPadJump)
        {
            nextPadJump = Time.time + Random.Range(padJumpEvery.x, padJumpEvery.y);
            JumpToPad(away: false);
            return;
        }
        if (Time.time >= nextTurn)
        {
            nextTurn = Time.time + Random.Range(turnEvery.x, turnEvery.y);
            Hop(transform.position, yaw + Random.Range(-90f, 90f), .03f, .25f);
        }
    }

    // A jump to another free lily pad within reach: a near one at random, or (fleeing) the one farthest from the chick.
    private void JumpToPad(bool away)
    {
        Collider best = null;
        float bestScore = float.MinValue;
        foreach (Collider c in Pads)
        {
            if (c == null || c == pad || Taken.Contains(c) || !c.enabled) continue;
            float d = Flat(c.bounds.center - transform.position);
            if (d > padReach || d < .3f) continue;
            float score = away && chick != null ? Flat(c.bounds.center - chick.position) : Random.value;
            if (score > bestScore) { bestScore = score; best = c; }
        }
        if (best == null)
        {
            // Nowhere to go: a startled hop round on its own pad.
            Hop(transform.position, transform.eulerAngles.y + 180f, .05f, .3f);
            return;
        }
        Vector3 target = PadTop(best);
        if (pad != null) Taken.Remove(pad);
        pad = best;
        Taken.Add(pad);
        Vector3 dir = target - transform.position;
        float distance = Flat(dir);
        Hop(target, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, .12f + distance * .12f, .35f + distance * .12f);
    }

    private void Hop(Vector3 target, float yaw, float height, float duration)
    {
        phase = Phase.Crouch;
        t = 0f;
        from = transform.position;
        to = target;
        fromYaw = transform.eulerAngles.y;
        toYaw = yaw;
        jumpHeight = height;
        jumpDuration = duration;
    }

    // Stretch > 0: longer and thinner; < 0: squashed low and wide (volume roughly kept).
    private void Squash(float stretch)
    {
        if (visual == null) return;
        float s = 1f + stretch;
        visual.localScale = new Vector3(restScale.x / Mathf.Sqrt(s), restScale.y * s, restScale.z * (1f + stretch * .5f));
    }

    private static Vector3 PadTop(Collider c)
    {
        Vector3 top = c.bounds.center + Vector3.up * (c.bounds.extents.y + .5f);
        return c.Raycast(new Ray(top, Vector3.down), out RaycastHit hit, 2f) ? hit.point + Vector3.up * .003f : c.bounds.center;
    }

    private static Collider NearestPad(Vector3 p, float within)
    {
        Collider best = null;
        float bestDistance = within;
        foreach (Collider c in Pads)
        {
            float d = Flat(c.bounds.center - p);
            if (d < bestDistance) { bestDistance = d; best = c; }
        }
        return best;
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
}
