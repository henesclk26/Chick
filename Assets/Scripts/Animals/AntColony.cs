using System.Collections.Generic;
using UnityEngine;

// Ants travelling between several nests in small groups. Each group leaves a random nest for another random nest on
// one of a few winding routes between the two. The nests are whatever AntNest markers are in the scene (the nest models
// carry one), so nests added, removed or moved - also during play - are picked up by themselves: routes are planned
// for new pairs and dropped for gone ones, while ants already under way finish their trip. Where the bush is roughly on the way, one of a pair's routes runs
// through it; inside it, out of the player's sight, a few ants of each group pick up a wheat grain and carry it on
// their backs. Ants do not march in a ruler line: each weaves about its route on its own, walks at its own pace and
// now and then stops for a moment. The chick can peck ants like any other food (a carried grain counts as an eaten
// wheat seed too); as soon as it starts pecking one, the ants around it scurry to the nearest nest, faster and less
// orderly. Routes are planned once at start: candidate lines are scored against the colliders in the way (trees,
// coops, fences) and the clearest ones are laid onto the terrain and the nest mounds with raycasts. Legs swing in an
// alternating tripod gait, the antennae twitch and the mandibles nibble.
[DisallowMultipleComponent]
public sealed class AntColony : MonoBehaviour
{
    [SerializeField, Tooltip("Ant model (Assets/Art/Ants/Ant.fbx): Ant_Body with Ant_Leg_*, Ant_Antenna_* and Ant_Mandible_* children.")]
    private GameObject antModel;
    [SerializeField, Tooltip("Food the ants pick up in the bush (Assets/Prefabs/Edibles/WheatGrain.prefab).")]
    private GameObject wheatPrefab;
    [SerializeField, Tooltip("Nests the ants travel between, if set by hand (pivot = centre of the entrance hole). Left " +
                             "empty, every AntNest in the scene is used, as nests come and go.")]
    private Transform[] nests = new Transform[0];
    [SerializeField, Tooltip("First nest of the original two-nest colony (used when no other nests are found).")]
    private Transform fromNest;
    [SerializeField, Tooltip("Second nest of the original two-nest colony (used when no other nests are found).")]
    private Transform toNest;
    [SerializeField, Tooltip("Routes that pass close by run through this bush; the carried wheat appears inside it.")]
    private Transform bush;

    [Header("Routes")]
    [SerializeField, Range(1, 4), Tooltip("Different routes between each pair of nests.")]
    private int routeCount = 2;
    [SerializeField, Tooltip("How far the routes bow out to the sides of the straight line, in metres (less on short hops).")]
    private float routeSpread = 3.5f;
    [SerializeField, Tooltip("Typical sideways wiggle of a route, in metres (each route gets more or less of it).")]
    private float meander = 0.35f;
    [SerializeField, Tooltip("A route goes through the bush when that makes it at most this much longer than going straight.")]
    private float bushDetour = 1.45f;
    [SerializeField] private int seed = 11;

    [Header("Groups")]
    [SerializeField, Range(1, 10)] private int groupSize = 5;
    [SerializeField, Tooltip("How many ants of a group carry wheat (random between x and y), on routes through the bush.")]
    private Vector2Int wheatCarriers = new Vector2Int(2, 3);
    [SerializeField, Tooltip("Seconds between groups leaving a nest (longer if Max Ants would run out).")]
    private float groupInterval = 14f;
    [SerializeField] private int maxAnts = 45;
    [SerializeField, Tooltip("Distance from one ant to the next in a group, in ant lengths.")]
    private float spacingInAntLengths = 1.35f;
    [SerializeField, Tooltip("Walking speed in metres per second.")]
    private float speed = 0.22f;

    [Header("Wandering")]
    [SerializeField, Tooltip("How far each ant weaves about its route, in ant lengths.")]
    private float wander = 1.6f;
    [SerializeField, Tooltip("How much an ant's own pace differs from its group's (0.15 = up to 15% either way).")]
    private float paceSpread = 0.15f;
    [SerializeField, Tooltip("Chance per second that an ant stops for a moment.")]
    private float pausesPerSecond = 0.07f;

    [Header("Ants")]
    [SerializeField] private float antScale = 1f;
    [SerializeField, Tooltip("Distance covered by one full step cycle by an unscaled ant, in metres.")]
    private float stride = 0.055f;
    [SerializeField, Tooltip("Legs and antennae stop animating beyond this camera distance.")]
    private float animationDistance = 25f;

    [Header("Scatter when the chick eats")]
    [SerializeField] private float panicRadius = 6f;
    [SerializeField] private float panicSeconds = 5f;
    [SerializeField, Tooltip("Speed multiplier while scattering.")]
    private float panicSpeed = 2.6f;
    [SerializeField, Tooltip("How far scattering ants weave to the sides, in metres.")]
    private float panicWander = 0.35f;

    private const int EdibleLayer = 9;
    // Everything a route should steer around; Ignore Raycast, Water and Edible pickups are left out.
    private const int ObstacleMask = ~((1 << 2) | (1 << 4) | (1 << EdibleLayer));
    private const float SampleStep = 0.05f;
    private const float PlanStep = 0.3f;
    private const int Candidates = 10;
    private const float HideLength = 0.07f;   // ants shrink away over this distance (unscaled) inside the holes
    private static readonly RaycastHit[] Hits = new RaycastHit[16];
    private static readonly Collider[] Overlaps = new Collider[16];

    private const float MoveTolerance = 0.3f;   // a nest moved further than this gets its routes planned again
    private const float NestCheckSeconds = 1f;

    private readonly List<Transform> nestList = new List<Transform>();
    private readonly Dictionary<Transform, Vector3> plannedAt = new Dictionary<Transform, Vector3>();
    private readonly List<Route> routes = new List<Route>();
    // Routes of each nest pair, keyed (and run) from the nest with the lower instance id to the other; ants going the
    // other way walk them backwards. Routes of pairs that are gone stay in the list until no ant is on them.
    private readonly Dictionary<(Transform, Transform), List<int>> pairRoutes = new Dictionary<(Transform, Transform), List<int>>();
    private int nestVersion = -1;
    private float nextNestCheck;
    private readonly List<Ant> pool = new List<Ant>();
    private readonly List<Ant> walking = new List<Ant>();
    private readonly Dictionary<EdibleObject, Ant> byEdible = new Dictionary<EdibleObject, Ant>();
    private System.Random random;
    private ChickEatingController eater;
    private float nextEaterSearch, clock, nextGroup, interval, antLength = 0.16f;

    public float AntScale { get => antScale; set => antScale = Mathf.Max(0.05f, value); }

    public void Configure(GameObject model, GameObject wheat, Transform from, Transform to, Transform trailBush)
    {
        antModel = model; wheatPrefab = wheat; fromNest = from; toNest = to; bush = trailBush;
    }

    private void OnEnable() => EdibleObject.Consumed += OnConsumed;
    private void OnDisable() => EdibleObject.Consumed -= OnConsumed;

    private void Start()
    {
        if (antModel == null)
        {
            Debug.LogWarning("AntColony: ant model missing; no ants spawned.", this);
            enabled = false;
            return;
        }
        random = new System.Random(seed);
        SyncNests();
        // Start as if the colony had been busy for a while, with groups already out on their way.
        if (pairRoutes.Count > 0)
        {
            int prewarm = Mathf.Max(1, Mathf.FloorToInt(TripSeconds() / interval));
            for (int g = 0; g < prewarm; g++) SpawnGroup((float)random.NextDouble());
        }
        nextGroup = 0f;
    }

    // Average time to walk one of the current routes.
    private float TripSeconds()
    {
        float total = 0f; int count = 0;
        foreach (List<int> list in pairRoutes.Values) foreach (int r in list) { total += routes[r].Length; count++; }
        return count > 0 ? total / count / speed : 0f;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        clock += dt;
        if (nestVersion != AntNest.Version || clock >= nextNestCheck) SyncNests();
        if (clock >= nextGroup && pairRoutes.Count > 0) { SpawnGroup(0f); nextGroup = clock + interval; }
        WatchEater();

        Camera cam = Camera.main;
        Vector3 camPosition = cam != null ? cam.transform.position : transform.position;
        float animateSq = animationDistance * animationDistance;
        for (int i = walking.Count - 1; i >= 0; i--)
        {
            Ant ant = walking[i];
            if (ant.Edible.IsConsumed || !Step(ant, dt, camPosition, animateSq)) Release(i);
        }
    }

    // Nests: the ones set by hand, else every AntNest in the scene, else the original pair.
    private List<Transform> CurrentNests(bool editMode = false)
    {
        var found = new List<Transform>();
        foreach (Transform n in nests) if (n != null && n.gameObject.activeInHierarchy && !found.Contains(n)) found.Add(n);
        if (found.Count >= 2) return found;
        found.Clear();
        if (editMode) { foreach (AntNest n in FindObjectsByType<AntNest>(FindObjectsSortMode.None)) found.Add(n.transform); }
        else foreach (AntNest n in AntNest.Active) found.Add(n.transform);
        if (found.Count >= 2) return found;
        found.Clear();
        if (fromNest != null) found.Add(fromNest);
        if (toNest != null && toNest != fromNest) found.Add(toNest);
        return found;
    }

    // Brings the routes in line with the nests: plans every pair that is new (or whose nest moved), drops pairs whose
    // nest is gone. Ants already walking keep their route to the end.
    private void SyncNests()
    {
        nestVersion = AntNest.Version;
        nextNestCheck = clock + NestCheckSeconds;
        List<Transform> current = CurrentNests();
        var moved = new HashSet<Transform>();
        foreach (Transform n in current)
            if (plannedAt.TryGetValue(n, out Vector3 at) && (n.position - at).sqrMagnitude > MoveTolerance * MoveTolerance) moved.Add(n);
        bool same = moved.Count == 0 && current.Count == nestList.Count;
        if (same) foreach (Transform n in current) if (!nestList.Contains(n)) { same = false; break; }
        if (same) return;

        nestList.Clear();
        nestList.AddRange(current);
        var keep = new HashSet<(Transform, Transform)>();
        for (int i = 0; i < nestList.Count; i++)
            for (int j = i + 1; j < nestList.Count; j++)
            {
                var key = PairKey(nestList[i], nestList[j]);
                keep.Add(key);
                if (pairRoutes.ContainsKey(key) && !moved.Contains(key.Item1) && !moved.Contains(key.Item2)) continue;
                pairRoutes[key] = PlanPair(key.Item1, key.Item2);
            }
        foreach (var key in new List<(Transform, Transform)>(pairRoutes.Keys)) if (!keep.Contains(key)) pairRoutes.Remove(key);
        plannedAt.Clear();
        foreach (Transform n in nestList) plannedAt[n] = n.position;

        // Groups leave often enough to keep the routes busy, but never more often than the ant budget allows.
        float trip = TripSeconds();
        interval = Mathf.Max(groupInterval / Mathf.Max(1, nestList.Count - 1), trip / Mathf.Max(1f, (float)maxAnts / groupSize - 1f));
        if (interval <= 0f) interval = groupInterval;
    }

    private static (Transform, Transform) PairKey(Transform a, Transform b) =>
        a.GetInstanceID() < b.GetInstanceID() ? (a, b) : (b, a);

    // ---------------------------------------------------------------- walking

    // Moves one ant; false once it has gone into a nest (or was eaten).
    private bool Step(Ant ant, float dt, Vector3 camPosition, float animateSq)
    {
        // A chick in mid-peck needs its target to hold still, or the peck misses.
        if (eater != null && eater.IsBusy && eater.CurrentTarget == ant.Edible) return true;

        Route route = routes[ant.Route];
        bool panic = clock < ant.PanicUntil;
        // Now and then an ant stops for a moment (never while scattering).
        if (!panic && clock >= ant.PauseUntil && Random.value < pausesPerSecond * dt)
            ant.PauseUntil = clock + Random.Range(0.3f, 1.0f);
        bool paused = !panic && clock < ant.PauseUntil;
        float v = paused ? 0f : speed * ant.Pace * (panic ? panicSpeed : 1f);
        ant.Distance += ant.Direction * v * dt;
        float d = ant.Distance;
        if ((ant.Direction > 0 && d >= route.Length) || (ant.Direction < 0 && d <= 0f)) return false;

        bool visible = d > 0f && d < route.Length;
        if (ant.Root.gameObject.activeSelf != visible) ant.Root.gameObject.SetActive(visible);
        if (!visible) return true;

        if (ant.CarriesWheat && !ant.WheatShown && PastBush(ant, route)) ShowWheat(ant);
        if (ant.WheatShown && ant.WheatGrow < 1f)
        {
            ant.WheatGrow = Mathf.Min(1f, ant.WheatGrow + dt * 4f);
            ant.Wheat.transform.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, ant.WheatGrow);
        }

        // Each ant weaves about its route on its own; wider and quicker while scattering; never sideways in the nests.
        float scale = antScale;
        float weave = Mathf.PerlinNoise(clock * (panic ? 0.9f : 0.3f) + ant.Seed, ant.Seed * 0.37f) * 2f - 1f;
        float target = panic ? panicWander * weave : wander * antLength * scale * weave + ant.Lateral * scale;
        ant.Offset = Mathf.SmoothDamp(ant.Offset, target, ref ant.OffsetVelocity, panic ? 0.3f : 0.6f);
        float edge = Mathf.Min(d, route.Length - d);
        float sideways = Mathf.Clamp01(edge / 0.8f);

        route.Sample(d, out Vector3 pos, out Vector3 up);
        float look = 0.05f * scale;
        route.Sample(Mathf.Min(d + look, route.Length), out Vector3 ahead, out _);
        route.Sample(Mathf.Max(d - look, 0f), out Vector3 behind, out _);
        Vector3 tangent = (ahead - behind).normalized;
        Vector3 side = Vector3.Cross(up, tangent).normalized;
        // Face along the way it is going even while stopped, turned by its sideways drift.
        float facing = Mathf.Max(v, speed * 0.4f);
        Vector3 heading = tangent * (ant.Direction * facing) + side * (ant.OffsetVelocity * sideways);
        if (heading.sqrMagnitude < 1e-8f) heading = ant.Root.forward;
        ant.Root.SetPositionAndRotation(pos + side * (ant.Offset * sideways), Quaternion.LookRotation(heading, up));
        ant.Root.localScale = Vector3.one * (scale * Mathf.SmoothStep(0f, 1f, edge / (HideLength * scale)));

        if ((ant.Root.position - camPosition).sqrMagnitude < animateSq)
            ant.Animate(clock * (panic ? 1.8f : 1f), v * dt / (stride * scale) * Mathf.PI * 2f);
        return true;
    }

    private static bool PastBush(Ant ant, Route route) =>
        route.BushDistance >= 0f && (ant.Direction > 0 ? ant.Distance >= route.BushDistance : ant.Distance <= route.BushDistance);

    // Sends a group from a random nest to another random nest; lead is how far along (0..1) the first ant already is.
    private void SpawnGroup(float lead)
    {
        if (nestList.Count < 2) return;
        int from = random.Next(nestList.Count), to = random.Next(nestList.Count - 1);
        if (to >= from) to++;
        var key = PairKey(nestList[from], nestList[to]);
        if (!pairRoutes.TryGetValue(key, out List<int> options)) return;
        int route = options[random.Next(options.Count)];
        int direction = key.Item1 == nestList[from] ? 1 : -1;  // routes run from the pair's first nest
        Route r = routes[route];
        float pace = 0.9f + (float)random.NextDouble() * 0.2f;
        int carriers = r.BushDistance >= 0f ? Mathf.Clamp(random.Next(wheatCarriers.x, wheatCarriers.y + 1), 0, groupSize) : 0;
        var wheat = new List<int>();
        for (int i = 0; i < groupSize; i++) wheat.Add(i);
        for (int i = wheat.Count - 1; i > 0; i--) { int j = random.Next(i + 1); (wheat[i], wheat[j]) = (wheat[j], wheat[i]); }
        wheat.RemoveRange(carriers, wheat.Count - carriers);

        float start = direction > 0 ? lead * r.Length : r.Length * (1f - lead);
        for (int i = 0; i < groupSize; i++)
        {
            Ant ant = Acquire();
            if (ant == null) return;
            ant.Route = route;
            ant.Pace = pace * (1f + ((float)random.NextDouble() * 2f - 1f) * paceSpread);
            ant.Direction = direction;
            ant.Distance = start - direction * i * spacingInAntLengths * antLength * antScale;
            ant.CarriesWheat = wheat.Contains(i) && wheatPrefab != null;
            ant.PanicUntil = 0f;
            ant.PauseUntil = 0f;
            ant.Offset = ant.Lateral * antScale;
            ant.OffsetVelocity = 0f;
            if (ant.CarriesWheat && PastBush(ant, r)) ShowWheat(ant);
            ant.Root.gameObject.SetActive(false);
            walking.Add(ant);
        }
    }

    private Ant Acquire()
    {
        foreach (Ant ant in pool)
            if (!ant.Busy) { ant.Busy = true; return ant; }
        if (pool.Count >= maxAnts) return null;
        var created = new Ant(antModel, wheatPrefab, transform, pool.Count, seed);
        antLength = created.Length;
        pool.Add(created);
        byEdible[created.Edible] = created;
        created.Busy = true;
        return created;
    }

    private void Release(int walkingIndex)
    {
        Ant ant = walking[walkingIndex];
        walking.RemoveAt(walkingIndex);
        ant.Busy = false;
        if (ant.Edible.IsConsumed) ant.Edible.ResetForReuse();
        HideWheat(ant);
        ant.Root.gameObject.SetActive(false);
    }

    private static void ShowWheat(Ant ant)
    {
        if (ant.Wheat == null) return;
        ant.WheatShown = true;
        ant.WheatGrow = 0f;
        ant.Wheat.transform.localScale = Vector3.zero;
        ant.Wheat.gameObject.SetActive(true);
    }

    private static void HideWheat(Ant ant)
    {
        ant.WheatShown = false;
        if (ant.Wheat == null) return;
        if (ant.Wheat.IsConsumed) ant.Wheat.ResetForReuse();
        ant.Wheat.enabled = false;
        ant.Wheat.gameObject.SetActive(false);
    }

    // Eating an ant that carries a grain eats the grain too, so it counts like any wheat seed.
    private static bool EatCarriedWheat(Ant ant)
    {
        if (ant.WheatShown && ant.Wheat != null && !ant.Wheat.IsConsumed)
        {
            ant.Wheat.enabled = true;
            ant.Wheat.Consume();
        }
        return true;
    }

    // ---------------------------------------------------------------- scattering

    private void WatchEater()
    {
        if (eater == null && Time.time >= nextEaterSearch)
        {
            eater = FindFirstObjectByType<ChickEatingController>();
            nextEaterSearch = Time.time + 2f;
        }
        if (eater != null && eater.IsBusy && eater.CurrentTarget != null &&
            byEdible.TryGetValue(eater.CurrentTarget, out Ant target) && target.Busy)
            Scatter(target.Root.position);
    }

    private void OnConsumed(EdibleObject food)
    {
        if (food != null && byEdible.TryGetValue(food, out Ant ant) && ant.Busy) Scatter(ant.Root.position);
    }

    private void Scatter(Vector3 centre)
    {
        float radiusSq = panicRadius * panicRadius;
        foreach (Ant ant in walking)
        {
            if (ant.Distance <= 0f || (ant.Root.position - centre).sqrMagnitude > radiusSq) continue;
            // A fresh scare sends each ant to whichever nest is nearer along its route.
            if (clock >= ant.PanicUntil) ant.Direction = ant.Distance < routes[ant.Route].Length * 0.5f ? -1 : 1;
            ant.PanicUntil = clock + panicSeconds;
        }
    }

    // ---------------------------------------------------------------- routes

    // The routes of one pair of nests, run from first to second.
    private List<int> PlanPair(Transform first, Transform second)
    {
        Vector3 a = first.position, b = second.position;
        float straight = Flat(b - a);
        // The first route of a pair goes through the bush if that is not much of a detour.
        bool bushOnTheWay = bush != null && Flat(bush.position - a) + Flat(b - bush.position) <= straight * bushDetour;
        var list = new List<int>();
        for (int r = 0; r < routeCount; r++)
        {
            float lane = routeCount == 1 ? 0f : Mathf.Lerp(-1f, 1f, r / (routeCount - 1f));
            list.Add(routes.Count);
            routes.Add(PlanRoute(a, b, r == 0 && bushOnTheWay ? bush.position : (Vector3?)null, lane, straight));
        }
        return list;
    }

    private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    // The clearest of several candidate lines from a to b (through via, if given), laid onto the ground.
    private Route PlanRoute(Vector3 a, Vector3 b, Vector3? via, float lane, float straight)
    {
        float spread = routeSpread * Mathf.Clamp01(straight / 15f);           // short hops bow less
        float wiggle = meander * (0.7f + (float)random.NextDouble() * 1.1f);  // every route wiggles its own amount
        Vector3 best = Vector3.zero;   // bow of each leg and wiggle phase of the clearest candidate
        int bestBlocked = int.MaxValue;
        for (int c = 0; c < Candidates; c++)
        {
            // Lanes fan out on the first leg and cross over on the second, so the routes look unrelated.
            float bow1 = (lane + Jitter(0.45f)) * spread;
            float bow2 = (-lane + Jitter(0.45f)) * spread;
            float phase = (float)random.NextDouble() * 100f;
            var plan = new List<Vector3>();
            if (via.HasValue) { PlanLeg(a, via.Value, bow1, wiggle, phase, PlanStep, plan); PlanLeg(via.Value, b, bow2, wiggle, phase + 37f, PlanStep, plan); }
            else PlanLeg(a, b, bow1, wiggle, phase, PlanStep, plan);
            int blocked = CountBlocked(plan);
            if (blocked < bestBlocked) { bestBlocked = blocked; best = new Vector3(bow1, bow2, phase); }
            if (blocked == 0) break;
        }
        var points = new List<Vector3>();
        int junction = -1;
        if (via.HasValue)
        {
            PlanLeg(a, via.Value, best.x, wiggle, best.z, SampleStep, points);
            junction = points.Count - 1;
            PlanLeg(via.Value, b, best.y, wiggle, best.z + 37f, SampleStep, points);
        }
        else PlanLeg(a, b, best.x, wiggle, best.z, SampleStep, points);
        return new Route(this, points, junction);
    }

    private float Jitter(float amount) => ((float)random.NextDouble() * 2f - 1f) * amount;

    // One leg of a route: a bow away from the straight line plus a wiggle, both zero at the ends so every route leaves
    // a nest hole (and meets the bush) along the same line.
    private static void PlanLeg(Vector3 from, Vector3 to, float bow, float wiggle, float phase, float step, List<Vector3> into)
    {
        Vector3 flat = to - from; flat.y = 0f;
        float distance = flat.magnitude;
        if (distance < 0.01f) { into.Add(from); into.Add(to); return; }
        Vector3 side = Vector3.Cross(Vector3.up, flat / distance);
        int steps = Mathf.Max(2, Mathf.CeilToInt(distance / step));
        for (int i = into.Count > 0 ? 1 : 0; i <= steps; i++)
        {
            float u = i / (float)steps;
            // Sin(PI) comes out as a tiny negative float, and a negative base to a fractional power is NaN.
            float arc = Mathf.Max(0f, Mathf.Sin(u * Mathf.PI));
            float wave = 0.5f * Mathf.Sin(u * distance * 1.3f + phase) + 0.3f * Mathf.Sin(u * distance * 0.47f + phase * 1.7f)
                       + 0.2f * Mathf.Sin(u * distance * 2.9f + phase * 0.6f);
            into.Add(Vector3.Lerp(from, to, u) + side * (bow * arc + wiggle * Mathf.Pow(arc, 0.8f) * wave));
        }
    }

    private int CountBlocked(List<Vector3> plan)
    {
        int blocked = 0;
        foreach (Vector3 p in plan)
        {
            Vector3 ground = TryGround(p, out RaycastHit hit) ? hit.point : p;
            int count = Physics.OverlapSphereNonAlloc(ground + Vector3.up * 0.25f, 0.22f, Overlaps, ObstacleMask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!IsWalkable(Overlaps[i]) && !Overlaps[i].transform.IsChildOf(bush != null ? bush : transform) &&
                    Overlaps[i].attachedRigidbody == null && !(Overlaps[i] is CharacterController)) { blocked++; break; }
        }
        return blocked;
    }

    // The trail lies on the terrain and over the nest mounds only, never on top of bushes, fences or props.
    private bool IsWalkable(Collider c)
    {
        if (c is TerrainCollider) return true;
        foreach (Transform nest in nestList) if (c.transform.IsChildOf(nest)) return true;
        return (fromNest != null && c.transform.IsChildOf(fromNest)) || (toNest != null && c.transform.IsChildOf(toNest));
    }

    private bool TryGround(Vector3 point, out RaycastHit best)
    {
        best = default;
        int count = Physics.RaycastNonAlloc(point + Vector3.up * 3f, Vector3.down, Hits, 6f, ~0, QueryTriggerInteraction.Ignore);
        float bestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
            if (IsWalkable(Hits[i].collider) && Hits[i].distance < bestDistance) { bestDistance = Hits[i].distance; best = Hits[i]; }
        return bestDistance < float.MaxValue;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.85f, 0.35f, 0.15f);
        if (Application.isPlaying && routes.Count > 0)
        {
            foreach (Route r in routes)
                for (int i = 1; i < r.Points.Count; i++) Gizmos.DrawLine(r.Points[i - 1], r.Points[i]);
            return;
        }
        List<Transform> found = CurrentNests(true);
        for (int i = 0; i < found.Count; i++)
            for (int j = i + 1; j < found.Count; j++) Gizmos.DrawLine(found[i].position, found[j].position);
    }

    private sealed class Route
    {
        public readonly List<Vector3> Points = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<float> lengths = new List<float>();
        public readonly float Length;
        public readonly float BushDistance = -1f;   // distance of the bush along the route; -1: not through it

        public Route(AntColony colony, List<Vector3> plan, int junction)
        {
            var raw = new List<Vector3>(plan.Count);
            foreach (Vector3 p in plan)
            {
                Vector3 normal = Vector3.up, point = p;
                if (colony.TryGround(p, out RaycastHit hit)) { point = hit.point; normal = hit.normal; }
                Points.Add(point); raw.Add(normal);
            }
            // Soften the ground normals so ants do not snap their tilt on every facet.
            for (int i = 0; i < raw.Count; i++)
            {
                Vector3 sum = Vector3.zero;
                for (int k = -2; k <= 2; k++) sum += raw[Mathf.Clamp(i + k, 0, raw.Count - 1)];
                normals.Add(sum.normalized);
            }
            lengths.Add(0f);
            for (int i = 1; i < Points.Count; i++)
            {
                Length += Vector3.Distance(Points[i - 1], Points[i]);
                lengths.Add(Length);
                if (i == junction) BushDistance = Length;
            }
        }

        public void Sample(float d, out Vector3 position, out Vector3 up)
        {
            int hi = lengths.BinarySearch(d);
            if (hi >= 0) { position = Points[hi]; up = normals[hi]; return; }
            hi = Mathf.Clamp(~hi, 1, Points.Count - 1);
            int lo = hi - 1;
            float t = Mathf.InverseLerp(lengths[lo], lengths[hi], d);
            position = Vector3.Lerp(Points[lo], Points[hi], t);
            up = Vector3.Slerp(normals[lo], normals[hi], t);
        }
    }

    // ---------------------------------------------------------------- one ant

    private sealed class Ant
    {
        public readonly Transform Root;
        public readonly EdibleObject Edible;
        public readonly EdibleObject Wheat;
        public readonly float Seed;
        public readonly float Lateral;
        public readonly float Length;   // nose to tail of the unscaled model
        public bool Busy, CarriesWheat, WheatShown;
        public int Route, Direction;
        public float Distance, Pace, PanicUntil, PauseUntil, Offset, OffsetVelocity, WheatGrow;
        private readonly Limb[] legs;
        private readonly Limb[] antennae;
        private readonly Limb[] mandibles;
        private float phase;

        private struct Limb
        {
            public Transform Bone;
            public Quaternion Rest;
            public Vector3 Up, Forward, Right;
            public float Side;
            public int Group;
        }

        public Ant(GameObject model, GameObject wheatPrefab, Transform parent, int index, int colonySeed)
        {
            var random = new System.Random(colonySeed * 7919 + index * 104729);
            Seed = (float)random.NextDouble() * 100f;
            Lateral = ((float)random.NextDouble() - 0.5f) * 0.03f;
            phase = (float)random.NextDouble() * Mathf.PI * 2f;

            // Same shape as other food: the root carries the trigger and EdibleObject on the Edible layer, the
            // model sits in a child the peck's contact assist may nudge.
            Root = new GameObject("Ant " + (index + 1)) { layer = EdibleLayer }.transform;
            Root.SetParent(parent, false);
            Root.SetPositionAndRotation(parent.position, Quaternion.identity);   // set up in world axes
            Root.localScale = Vector3.one;
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(Root, false);
            Transform body = Object.Instantiate(model, visual, false).transform;

            var legList = new List<Transform>();
            var antennaList = new List<Transform>();
            var mandibleList = new List<Transform>();
            foreach (Transform t in body.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Ant_Leg")) legList.Add(t);
                else if (t.name.StartsWith("Ant_Antenna")) antennaList.Add(t);
                else if (t.name.StartsWith("Ant_Mandible")) mandibleList.Add(t);
            }

            // Turn the model so its head looks down the root's +Z, whatever axes the FBX import produced.
            Vector3 hips = Vector3.zero;
            foreach (Transform leg in legList) hips += leg.position;
            hips /= Mathf.Max(1, legList.Count);
            Vector3 head = Vector3.zero;
            foreach (Transform a in antennaList) head += a.position;
            head = antennaList.Count > 0 ? head / antennaList.Count : hips + Root.forward;
            Vector3 facing = head - hips; facing.y = 0f;
            if (facing.sqrMagnitude > 1e-8f)
            {
                Quaternion turn = Quaternion.FromToRotation(facing.normalized, Vector3.forward);
                body.localRotation = turn * body.localRotation;
                hips = Root.position + turn * (hips - Root.position);   // the reference points turned with the model
                head = Root.position + turn * (head - Root.position);
            }
            Renderer[] renderers = body.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Root.position, Vector3.one * 0.1f);
            foreach (Renderer r in renderers)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                bounds.Encapsulate(r.bounds);
            }
            Vector3 centre = Root.InverseTransformPoint(bounds.center);
            Length = bounds.size.z;

            var bite = new GameObject("BitePoint").transform;
            bite.SetParent(Root, false);
            bite.localPosition = new Vector3(centre.x, bounds.size.y * 0.35f, centre.z);
            var trigger = Root.gameObject.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.center = centre;
            trigger.radius = Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.7f;
            Edible = Root.gameObject.AddComponent<EdibleObject>();
            Edible.ConfigureRuntime("ant-" + colonySeed + "-" + index, EdibleCategory.Other, "ant", "Karınca",
                bite, visual, 1.35f, 0.08f);
            Edible.BiteHandler = () => EatCarriedWheat(this);
            Edible.IgnoredByHelpers = true;

            if (wheatPrefab != null)
            {
                // The grain lies along the back, its own pickup switched off: only the ant is peckable.
                GameObject grain = Object.Instantiate(wheatPrefab, visual, false);
                grain.name = "Carried Wheat";
                grain.transform.localPosition = new Vector3(centre.x, bounds.size.y * 0.82f, centre.z - bounds.size.z * 0.08f);
                grain.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);
                foreach (Collider c in grain.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                Wheat = grain.GetComponent<EdibleObject>();
                if (Wheat == null) Wheat = grain.GetComponentInChildren<EdibleObject>(true);
                if (Wheat != null)
                {
                    Transform wheatRoot = Wheat.transform;
                    Transform wheatBite = wheatRoot.Find("BitePoint");
                    Transform wheatVisual = wheatRoot.Find("Visual");
                    if (wheatBite != null && wheatVisual != null)
                        Wheat.ConfigureRuntime("ant-wheat-" + colonySeed + "-" + index, EdibleCategory.Seed, "wheat", null,
                            wheatBite, wheatVisual, 1f, 0.04f);
                    Wheat.enabled = false;
                }
                grain.SetActive(false);
            }

            legs = new Limb[legList.Count];
            for (int i = 0; i < legList.Count; i++) legs[i] = MakeLimb(legList[i], hips);
            // Alternating tripods: front and back leg of one side step with the middle leg of the other side.
            for (int s = -1; s <= 1; s += 2)
            {
                var side = new List<int>();
                for (int i = 0; i < legs.Length; i++) if (legs[i].Side == s) side.Add(i);
                side.Sort((a, b) => Vector3.Dot(legs[a].Bone.position - hips, Root.forward)
                    .CompareTo(Vector3.Dot(legs[b].Bone.position - hips, Root.forward)));
                for (int rank = 0; rank < side.Count; rank++)
                    legs[side[rank]].Group = ((rank % 2 == 0) == (s > 0)) ? 0 : 1;
            }
            antennae = new Limb[antennaList.Count];
            for (int i = 0; i < antennaList.Count; i++) antennae[i] = MakeLimb(antennaList[i], hips);
            mandibles = new Limb[mandibleList.Count];
            for (int i = 0; i < mandibleList.Count; i++) mandibles[i] = MakeLimb(mandibleList[i], head);
            Root.gameObject.SetActive(false);
        }

        private Limb MakeLimb(Transform bone, Vector3 centre)
        {
            Transform space = bone.parent;
            return new Limb
            {
                Bone = bone,
                Rest = bone.localRotation,
                Up = space.InverseTransformDirection(Root.up).normalized,
                Forward = space.InverseTransformDirection(Root.forward).normalized,
                Right = space.InverseTransformDirection(Root.right).normalized,
                Side = Vector3.Dot(bone.position - centre, Root.right) >= 0f ? 1f : -1f,
            };
        }

        public void Animate(float time, float phaseStep)
        {
            phase += phaseStep;
            for (int i = 0; i < legs.Length; i++)
            {
                Limb leg = legs[i];
                float p = phase + leg.Group * Mathf.PI;
                float reach = Mathf.Cos(p);                   // +1 foot forward, -1 foot back
                float lift = Mathf.Max(0f, -Mathf.Sin(p));    // raised only while swinging forward
                leg.Bone.localRotation = Quaternion.AngleAxis(-leg.Side * 20f * reach, leg.Up) *
                                         Quaternion.AngleAxis(leg.Side * 16f * lift, leg.Forward) * leg.Rest;
            }
            for (int i = 0; i < antennae.Length; i++)
            {
                Limb a = antennae[i];
                float sway = Mathf.Sin(time * 5.1f + Seed + i * 1.9f) * 10f;
                float nod = Mathf.Sin(time * 3.7f + Seed * 1.3f + i * 0.7f) * 8f;
                a.Bone.localRotation = Quaternion.AngleAxis(sway, a.Up) * Quaternion.AngleAxis(nod, a.Right) * a.Rest;
            }
            // Pincers open outwards together and snap back, in short bursts of nibbling with pauses between.
            float burst = Mathf.Clamp01(Mathf.Sin(time * 0.9f + Seed) * 2f);
            float open = burst * (0.5f + 0.5f * Mathf.Sin(time * 9f + Seed * 2.3f)) * 18f;
            for (int i = 0; i < mandibles.Length; i++)
            {
                Limb m = mandibles[i];
                m.Bone.localRotation = Quaternion.AngleAxis(m.Side * open, m.Up) * m.Rest;
            }
        }
    }
}
