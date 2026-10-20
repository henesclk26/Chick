using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// A caterpillar that comes out of an apple left lying on the ground (FruitTree): it pushes out of the apple head
// first, turns round and nibbles at it, the apple shrinking bite by bite, until it is gone; then it crawls off to the
// nearest grass tuft or bush and disappears into it (or, with none in reach, crawls a little way and disappears). If
// the chick eats the apple first, it crawls off at once. The chick can eat the caterpillar itself at any time. It
// crawls like an inchworm: its body stretches and bunches up as it goes. The model faces +Z with its origin under its
// belly.
[DisallowMultipleComponent]
public sealed class AppleCaterpillar : MonoBehaviour
{
    private const int EdibleLayer = 9;

    [SerializeField, Tooltip("The model; stretches, bunches and nods. Defaults to the first child.")]
    private Transform visual;
    [SerializeField] private float emergeSeconds = 2.4f;
    [SerializeField] private float biteEvery = 1.2f;
    [SerializeField, Range(.5f, .99f), Tooltip("Apple size kept after each bite.")]
    private float keptPerBite = .9f;
    [SerializeField, Range(.05f, .6f), Tooltip("The apple is gone once it is this small.")]
    private float goneBelow = .3f;
    [SerializeField, Tooltip("Crawling speed (m/s).")]
    private float crawlSpeed = .03f;
    [SerializeField, Tooltip("Crawling speed (m/s) on the way to the grass.")]
    private float leaveSpeed = .1f;
    [SerializeField, Tooltip("Grass further than this (m) is out of reach.")]
    private float grassReach = 5f;
    [SerializeField, Tooltip("With no grass in reach it crawls this long before disappearing.")]
    private float leaveSeconds = 5f;

    private enum Phase { Emerge, Turn, Eat, Leave, Vanish }

    // Where grass tufts and bushes stand in the open scenes, found once on first need.
    private static readonly List<Vector3> GrassSpots = new List<Vector3>();
    private static bool grassFound;

    private Phase phase;
    private EdibleObject apple;
    private EdibleObject self;
    private Transform appleBody;                  // the apple's pickup, centred on it: shrinks round its middle
    private Vector3 appleScale, appleRest, restScale, outPoint, inPoint, grass;
    private float appleRadius, t, nextBite, gait, startHeight;
    private bool toGrass;
    private Action onEaten;
    private Quaternion turnFrom, turnTo;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetGrass()
    {
        GrassSpots.Clear();
        grassFound = false;
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
    }

    private static void SceneLoaded(Scene scene, LoadSceneMode mode) { GrassSpots.Clear(); grassFound = false; }

    public void Begin(EdibleObject food, float radius, Action eaten)
    {
        apple = food;
        appleBody = food.transform;
        appleScale = appleBody.localScale;
        appleRest = appleBody.position;
        appleRadius = radius;
        onEaten = eaten;
        if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
        restScale = visual != null ? visual.localScale : Vector3.one;

        // Out of the apple's middle, head first, towards a random side, onto the ground beside it.
        Vector3 centre = food.transform.position;
        Vector3 dir = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
        float ground = centre.y - radius * .9f;
        inPoint = new Vector3(centre.x, ground, centre.z);
        outPoint = inPoint + dir * (radius + .03f);
        startHeight = ground;
        transform.SetPositionAndRotation(inPoint, Quaternion.LookRotation(dir));
        transform.localScale = Vector3.one;
        MakeEdible();
        transform.localScale = Vector3.one * .4f;
        phase = Phase.Emerge;
        t = 0f;
    }

    // Food like the walking ants: the root carries a trigger and the EdibleObject on the Edible layer, the model sits
    // in the child the peck's contact assist may nudge. Helper chicks leave it alone (it moves).
    private void MakeEdible()
    {
        gameObject.layer = EdibleLayer;
        Bounds bounds = new Bounds(transform.position, Vector3.one * .05f);
        bool first = true;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (first) { bounds = r.bounds; first = false; }
            else bounds.Encapsulate(r.bounds);
        }
        Vector3 centre = transform.InverseTransformPoint(bounds.center);
        var bite = new GameObject("BitePoint").transform;
        bite.SetParent(transform, false);
        bite.localPosition = new Vector3(centre.x, bounds.size.y * .5f, centre.z);
        var trigger = gameObject.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.center = centre;
        trigger.radius = Mathf.Max(.035f, Mathf.Max(bounds.extents.x, bounds.extents.z) * .8f);
        self = gameObject.AddComponent<EdibleObject>();
        self.ConfigureRuntime("caterpillar-" + GetInstanceID(), EdibleCategory.Other, "caterpillar", "Kurtçuk",
            bite, visual, 1.35f, .08f);
        self.IgnoredByHelpers = true;
    }

    // Eaten by the chick: the EdibleObject hides it; it is not coming back.
    private void OnDisable()
    {
        if (self != null && self.IsConsumed) Destroy(gameObject);
    }

    /// <summary>The apple is gone (eaten by the chick): crawl off now.</summary>
    public void Leave()
    {
        if (phase == Phase.Leave || phase == Phase.Vanish) return;
        StartLeaving();
    }

    private void StartLeaving()
    {
        phase = Phase.Leave;
        t = 0f;
        transform.localScale = Vector3.one;
        if (visual != null) visual.localRotation = Quaternion.identity;
        turnFrom = transform.rotation;
        toGrass = NearestGrass(transform.position, grassReach, out grass);
        Vector3 away = toGrass ? grass - transform.position : -transform.forward;
        away.y = 0f;
        turnTo = toGrass && away.sqrMagnitude > 1e-6f
            ? Quaternion.LookRotation(away)
            : Quaternion.Euler(0f, transform.eulerAngles.y + 180f + UnityEngine.Random.Range(-40f, 40f), 0f);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        bool appleGone = apple == null || apple.IsConsumed || !apple.gameObject.activeInHierarchy;
        if (appleGone && phase < Phase.Leave) StartLeaving();
        t += dt;

        switch (phase)
        {
            case Phase.Emerge:
            {
                float f = Mathf.Clamp01(t / emergeSeconds);
                transform.position = Vector3.Lerp(inPoint, outPoint, Mathf.SmoothStep(0f, 1f, f));
                transform.localScale = Vector3.one * Mathf.Lerp(.4f, 1f, f);
                Crawl(dt, 1f, crawlSpeed);
                if (f >= 1f)
                {
                    phase = Phase.Turn;
                    t = 0f;
                    turnFrom = transform.rotation;
                    Vector3 toApple = apple.transform.position - transform.position;
                    toApple.y = 0f;
                    turnTo = Quaternion.LookRotation(toApple.sqrMagnitude > 1e-6f ? toApple : -transform.forward);
                }
                break;
            }
            case Phase.Turn:
            {
                float f = Mathf.Clamp01(t / .9f);
                transform.rotation = Quaternion.Slerp(turnFrom, turnTo, Mathf.SmoothStep(0f, 1f, f));
                Crawl(dt, .5f, crawlSpeed);
                if (f >= 1f) { phase = Phase.Eat; t = 0f; nextBite = biteEvery * .5f; }
                break;
            }
            case Phase.Eat:
            {
                // Nodding into the apple; each bite takes a little off it.
                if (visual != null)
                {
                    visual.localScale = restScale;
                    visual.localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 2f / biteEvery)) * 18f, 0f, 0f);
                }
                if (t >= nextBite)
                {
                    nextBite += biteEvery;
                    // Smaller round its middle, still resting on the ground.
                    appleBody.localScale *= keptPerBite;
                    float kept = appleBody.localScale.x / appleScale.x;
                    appleBody.position = appleRest - Vector3.up * appleRadius * .9f * (1f - kept);
                    if (kept < goneBelow)
                    {
                        onEaten?.Invoke();
                        StartLeaving();
                    }
                }
                break;
            }
            case Phase.Leave:
            {
                if (t <= .9f)
                {
                    transform.rotation = Quaternion.Slerp(turnFrom, turnTo, Mathf.SmoothStep(0f, 1f, t / .9f));
                    Crawl(dt, .5f, leaveSpeed);
                    break;
                }
                if (toGrass)
                {
                    // Keep heading for the grass; into it once there.
                    Vector3 to = grass - transform.position;
                    to.y = 0f;
                    if (to.magnitude < .06f) { phase = Phase.Vanish; t = 0f; break; }
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 90f * dt);
                }
                Vector3 next = transform.position + transform.forward * Crawl(dt, 2f, leaveSpeed) * dt;
                // Over the ground; never down into the pond nor up a fence.
                if (!Ground(next, out float y) || y < startHeight - .35f) { phase = Phase.Vanish; t = 0f; break; }
                next.y = y;
                transform.position = next;
                if (!toGrass && t >= leaveSeconds || t >= 120f) { phase = Phase.Vanish; t = 0f; }
                break;
            }
            case Phase.Vanish:
            {
                // Into the grass.
                transform.localScale = Vector3.one * Mathf.Max(0f, 1f - t / .8f);
                if (t >= .8f) Destroy(gameObject);
                break;
            }
        }
    }

    // Inchworm gait: the body stretches (moving forward) and bunches up (resting). Returns the speed this moment.
    private float Crawl(float dt, float pace, float speed)
    {
        gait += dt * pace * 5f;
        float s = Mathf.Sin(gait);
        if (visual != null)
        {
            visual.localScale = new Vector3(restScale.x * (1f - s * .05f), restScale.y * (1f - s * .08f), restScale.z * (1f + s * .14f));
            visual.localRotation = Quaternion.identity;
        }
        return speed * Mathf.Max(0f, s) * 2f;
    }

    // The ground under a point, looking from a small step above it (so it does not climb onto things it meets).
    private static bool Ground(Vector3 p, out float y)
    {
        y = 0f;
        bool found = false;
        foreach (RaycastHit hit in Physics.RaycastAll(p + Vector3.up * .2f, Vector3.down, 1.5f, ~(1 << EdibleLayer), QueryTriggerInteraction.Ignore))
        {
            if (hit.collider is CharacterController || hit.collider.attachedRigidbody != null ||
                hit.collider.GetComponentInParent<ChickPlayerController>() != null) continue;
            if (!found || hit.point.y > y) { y = hit.point.y; found = true; }
        }
        return found;
    }

    // The foot of the nearest grass tuft or bush within reach, just on this side of it.
    private static bool NearestGrass(Vector3 from, float reach, out Vector3 spot)
    {
        if (!grassFound) FindGrass();
        spot = from;
        float best = reach * reach;
        bool found = false;
        foreach (Vector3 g in GrassSpots)
        {
            float dx = g.x - from.x, dz = g.z - from.z, d = dx * dx + dz * dz;
            if (d < best && Mathf.Abs(g.y - from.y) < 1f) { best = d; spot = g; found = true; }
        }
        if (found)
        {
            Vector3 back = from - spot;
            back.y = 0f;
            if (back.magnitude > .1f) spot += back.normalized * .05f;
        }
        return found;
    }

    private static void FindGrass()
    {
        grassFound = true;
        GrassSpots.Clear();
        foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            Bounds b = r.bounds;
            if (b.size.x > 3f || b.size.z > 3f || b.size.y > 2.5f || !IsGrass(r.transform)) continue;
            GrassSpots.Add(new Vector3(b.center.x, b.min.y, b.center.z));
        }
    }

    // Named as grass or a bush by itself or a parent close above it; not the water plants, the crops or the yard's
    // point-grass carpet.
    private static bool IsGrass(Transform t)
    {
        for (int i = 0; i < 4 && t != null; i++, t = t.parent)
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("lily") || n.Contains("water") || n.Contains("wheat") || n.Contains("point grass") || n.Contains("pointgrass")) return false;
            if (n.Contains("grass") || n.Contains("tuft") || n.Contains("bush") || n.Contains("fern") || n.Contains("undergrowth")) return true;
        }
        return false;
    }
}
