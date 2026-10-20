using System.Collections.Generic;
using UnityEngine;

// A butterfly fluttering round its home spot: it wanders in a wavering flight, now and then settles on a rock or a
// flower to rest (wings opening and closing slowly) and takes off again after a while, or at once when the chick comes
// close. Not food. The Animator plays "Fly" in the air and "Idle" when settled. Perches are the rocks and flowers of
// the map, found once per scene; two butterflies never sit on the same one.
[DisallowMultipleComponent]
public sealed class Butterfly : MonoBehaviour
{
    [SerializeField, Tooltip("How far (m) it wanders from where it was released.")]
    private float roamRadius = 9f;
    [SerializeField] private float flySpeed = 1.2f;
    [SerializeField, Tooltip("Flying height above the ground (m).")]
    private Vector2 height = new Vector2(.4f, 1.8f);
    [SerializeField, Tooltip("Seconds in the air before it looks for somewhere to settle.")]
    private Vector2 flightSeconds = new Vector2(5f, 14f);
    [SerializeField, Tooltip("Seconds it rests when settled.")]
    private Vector2 restSeconds = new Vector2(4f, 12f);
    [SerializeField, Tooltip("Takes off when the chick comes this close (m).")]
    private float scareRadius = 1f;

    private enum State { Wander, Approach, Rest }

    private static List<Vector3> perches;
    private static readonly HashSet<int> TakenPerches = new HashSet<int>();

    private Animator animator;
    private Transform chick;
    private Vector3 home, waypoint, velocity;
    private State state;
    private float timer, phase;
    private int perch = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPerches()
    {
        perches = null;
        TakenPerches.Clear();
    }

    private void Start()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.speed = Random.Range(.85f, 1.2f);
        var player = FindFirstObjectByType<ChickPlayerController>();
        if (player != null) chick = player.transform;
        home = transform.position;
        phase = Random.value * 100f;
        if (perches == null) perches = FindPerches();
        TakeOff();
    }

    private void OnDisable() => Release();

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        timer -= dt;
        switch (state)
        {
            case State.Wander:
                if ((transform.position - waypoint).sqrMagnitude < .25f) waypoint = NewWaypoint();
                Fly(waypoint, flySpeed, dt);
                if (timer <= 0f && ChoosePerch()) state = State.Approach;
                break;

            case State.Approach:
            {
                Vector3 spot = perches[perch];
                // Swoop in: level with the perch a little before it, slowing down as it arrives.
                float distance = Vector3.Distance(transform.position, spot);
                Vector3 aim = distance > .6f ? spot + Vector3.up * Mathf.Min(.5f, distance * .3f) : spot;
                Fly(aim, flySpeed * Mathf.Clamp(distance, .35f, 1f), dt);
                if (distance < .05f || (distance < .12f && timer < -6f)) Settle(spot);
                else if (timer < -10f) { Release(); TakeOff(); }      // could not get there
                break;
            }

            case State.Rest:
                if (timer <= 0f || ChickNear()) TakeOff();
                break;
        }
    }

    private void Fly(Vector3 target, float speed, float dt)
    {
        Vector3 to = target - transform.position;
        Vector3 desired = to.sqrMagnitude > 1e-6f ? to.normalized * speed : Vector3.zero;
        velocity = Vector3.Lerp(velocity, desired, 1f - Mathf.Exp(-3f * dt));
        // Butterflies waver: a quick bob up and down and a slower drift side to side.
        float t = Time.time + phase;
        Vector3 side = Vector3.Cross(Vector3.up, velocity.sqrMagnitude > 1e-4f ? velocity.normalized : transform.forward);
        Vector3 flutter = Vector3.up * Mathf.Sin(t * 11f) * .35f + side * (Mathf.PerlinNoise(t * .7f, phase) - .5f) * 1.2f;
        Vector3 position = transform.position + (velocity + flutter * (state == State.Approach ? .3f : 1f)) * dt;
        // Never through the ground.
        if (state != State.Approach && Ground(position, out float ground) && position.y < ground + .2f) position.y = ground + .2f;
        transform.position = position;
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        if (flat.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(flat) * Quaternion.Euler(-velocity.y * 12f, 0f, 0f), 1f - Mathf.Exp(-6f * dt));
    }

    private void Settle(Vector3 spot)
    {
        state = State.Rest;
        timer = Random.Range(restSeconds.x, restSeconds.y);
        transform.position = spot;
        transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        velocity = Vector3.zero;
        Play("Idle");
    }

    private void TakeOff()
    {
        Release();
        state = State.Wander;
        timer = Random.Range(flightSeconds.x, flightSeconds.y);
        waypoint = NewWaypoint();
        velocity = Vector3.up * .6f;
        Play("Fly");
    }

    private void Release()
    {
        if (perch >= 0) TakenPerches.Remove(perch);
        perch = -1;
    }

    // A free perch within its range, preferring nearer ones.
    private bool ChoosePerch()
    {
        if (perches == null || perches.Count == 0) { timer = Random.Range(flightSeconds.x, flightSeconds.y); return false; }
        int best = -1;
        float bestScore = float.MaxValue;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            int i = Random.Range(0, perches.Count);
            if (TakenPerches.Contains(i)) continue;
            Vector3 p = perches[i];
            if (Flat(p - home) > roamRadius) continue;
            float score = Vector3.Distance(p, transform.position) * Random.Range(.7f, 1.3f);
            if (score < bestScore) { bestScore = score; best = i; }
        }
        if (best < 0) { timer = Random.Range(2f, 5f); return false; }
        perch = best;
        TakenPerches.Add(best);
        timer = 0f;
        return true;
    }

    private Vector3 NewWaypoint()
    {
        Vector2 r = Random.insideUnitCircle * roamRadius;
        Vector3 p = home + new Vector3(r.x, 0f, r.y);
        float ground = Ground(p + Vector3.up * 30f, out float g) ? g : home.y;
        p.y = ground + Random.Range(height.x, height.y);
        return p;
    }

    private bool ChickNear() => chick != null && Vector3.Distance(chick.position, transform.position) < scareRadius;

    private void Play(string state)
    {
        if (animator != null && animator.isActiveAndEnabled) animator.CrossFadeInFixedTime(state, .2f);
    }

    private static bool Ground(Vector3 from, out float y)
    {
        y = 0f;
        if (!Physics.Raycast(from + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore)) return false;
        y = hit.point.y;
        return true;
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    // The tops of the map's rocks and flowers.
    private static List<Vector3> FindPerches()
    {
        var found = new List<Vector3>();
        string[] words = { "rock", "stone", "boulder", "flower", "daisy", "poppy", "sunflower", "bellflower", "buttercup", "rose" };
        foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!renderer.enabled || renderer is ParticleSystemRenderer) continue;
            string name = renderer.name.ToLowerInvariant() + " " + (renderer.transform.parent != null ? renderer.transform.parent.name.ToLowerInvariant() : "");
            if (name.Contains("wall") || name.Contains("shore")) continue;
            bool match = false;
            foreach (string w in words) if (name.Contains(w)) { match = true; break; }
            if (!match) continue;
            Bounds b = renderer.bounds;
            if (b.size.y < .05f || b.size.y > 3f || b.size.x > 4f || b.size.z > 4f) continue;
            Vector3 top = new Vector3(b.center.x, b.max.y, b.center.z);
            // On a rock with a collider, its real top surface under the middle.
            if (Physics.Raycast(top + Vector3.up * .5f, Vector3.down, out RaycastHit hit, b.size.y + .6f, ~0, QueryTriggerInteraction.Ignore) &&
                hit.point.y > b.min.y + b.size.y * .5f)
                top = hit.point;
            found.Add(top + Vector3.up * .005f);
        }
        return found;
    }
}
