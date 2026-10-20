using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A fruit tree whose fruit ripens and falls: every so often one of the hanging fruits (the "Fruit_*" children) drops
/// to the ground under the crown, bounces and rolls a little, and can then be eaten like any other food (same pickup
/// shape as BerryPlant's: a trigger on the Edible layer, a BitePoint and the moving visual). Fruit that is gone grows
/// back on the tree after a while, so the tree never runs out.
/// Apples left on the ground too long get a caterpillar: it crawls out of the apple and eats it (AppleCaterpillar);
/// an apple finished by a caterpillar is gone without counting as eaten by the player.
/// Added to the fruit tree models on import (FruitTreeSamples), so every placed tree gets it.
/// </summary>
[DisallowMultipleComponent]
public sealed class FruitTree : MonoBehaviour
{
    private const int EdibleLayer = 9;
    private const string CaterpillarResource = "Bugs/AppleCaterpillar";

    [SerializeField] private string fruitPrefix = "Fruit_";
    [Tooltip("Food statistics key; also prefixes each fruit's save id.")]
    [SerializeField] private string foodKey = "apple";
    [SerializeField] private string foodName = "Elma";
    [Tooltip("Seconds (game time) between two fruits falling from this tree.")]
    [SerializeField] private Vector2 dropEvery = new Vector2(25f, 50f);
    [Tooltip("Seconds after the start before the first fruit falls.")]
    [SerializeField] private Vector2 firstDrop = new Vector2(6f, 20f);
    [Tooltip("Seconds before a fruit that is gone (eaten) hangs on the tree again.")]
    [SerializeField, Min(1f)] private float regrowSeconds = 240f;
    [Tooltip("Seconds a regrowing fruit takes to swell from nothing to full size.")]
    [SerializeField, Min(.1f)] private float swellSeconds = 4f;
    [Tooltip("A caterpillar comes out of fruit left on the ground this long (apples).")]
    [SerializeField] private bool caterpillars;
    [SerializeField, Min(1f)] private float caterpillarAfter = 75f;

    public void Configure(string key, string displayName, bool withCaterpillars)
    {
        foodKey = key;
        foodName = displayName;
        caterpillars = withCaterpillars;
    }

    private enum State { Hanging, Falling, Grounded, Gone, Swelling }

    private sealed class Fruit
    {
        public Transform pickup, mesh, bite;
        public EdibleObject edible;
        public SphereCollider trigger;
        public BoxCollider solid;
        public float radius;
        public Vector3 hangLocal, meshLocal, meshScale;
        public Quaternion meshRotation;
        public State state;
        public float t, duration, timer;
        public Vector3 from, to;
        public Quaternion rollFrom, rollTo;
        public bool solidPending;
        public AppleCaterpillar caterpillar;
    }

    private readonly List<Fruit> fruits = new List<Fruit>();
    private static readonly Collider[] Overlaps = new Collider[8];
    private float nextDrop;
    private GameObject caterpillarPrefab;

    private void Awake()
    {
        Vector3 p = transform.position;
        string treeId = foodKey + "-" + Mathf.RoundToInt(p.x * 100f) + "_" + Mathf.RoundToInt(p.y * 100f) + "_" + Mathf.RoundToInt(p.z * 100f);
        var meshes = new List<Transform>();
        foreach (Transform child in transform)
            if (child.name.StartsWith(fruitPrefix) && child.GetComponent<Renderer>() != null) meshes.Add(child);
        foreach (Transform mesh in meshes)
        {
            Bounds bounds = mesh.GetComponent<Renderer>().bounds;
            var pickup = new GameObject(mesh.name + " Pickup") { layer = EdibleLayer };
            pickup.transform.SetParent(transform, false);
            pickup.transform.SetPositionAndRotation(bounds.center, Quaternion.identity);
            mesh.SetParent(pickup.transform, true);
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);

            var trigger = pickup.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = radius * .9f;
            trigger.enabled = false;
            var solid = pickup.AddComponent<BoxCollider>();
            solid.size = new Vector3(radius * 1.5f, radius * 2f, radius * 1.5f);
            solid.enabled = false;
            var bite = new GameObject("BitePoint").transform;
            bite.SetParent(pickup.transform, false);
            bite.localPosition = new Vector3(0f, -radius * .45f, 0f);

            var edible = pickup.AddComponent<EdibleObject>();
            edible.ConfigureRuntime(treeId + "-" + mesh.name, EdibleCategory.Other, foodKey, foodName, bite, mesh, 1.35f, .08f);
            edible.enabled = false;

            fruits.Add(new Fruit
            {
                pickup = pickup.transform, mesh = mesh, bite = bite, edible = edible, trigger = trigger, solid = solid,
                radius = radius, hangLocal = pickup.transform.localPosition, meshLocal = mesh.localPosition,
                meshRotation = mesh.localRotation, meshScale = mesh.localScale
            });
        }
        nextDrop = Random.Range(firstDrop.x, firstDrop.y);
        if (caterpillars) caterpillarPrefab = Resources.Load<GameObject>(CaterpillarResource);
    }

    private void Update()
    {
        // Scaled time: falling and ripening freeze with the game in pause and the journal.
        float dt = Time.deltaTime;
        nextDrop -= dt;
        if (nextDrop <= 0f)
        {
            nextDrop = Random.Range(dropEvery.x, dropEvery.y);
            DropOne();
        }
        foreach (Fruit fruit in fruits) Tick(fruit, dt);
    }

    private void Tick(Fruit fruit, float dt)
    {
        switch (fruit.state)
        {
            case State.Hanging:
                // Marked eaten by a restored save.
                if (fruit.edible.IsConsumed) GoneNow(fruit);
                break;

            case State.Falling:
                fruit.t = Mathf.Min(1f, fruit.t + dt / fruit.duration);
                float t = fruit.t;
                // Falling under gravity (accelerating), then a small bounce as it lands and rolls a little further.
                const float Land = .8f;
                Vector3 position;
                if (t < Land)
                {
                    float f = t / Land;
                    position = Vector3.Lerp(fruit.from, fruit.to, f * .85f);
                    position.y = Mathf.Lerp(fruit.from.y, fruit.to.y, f * f);
                }
                else
                {
                    float f = Mathf.InverseLerp(Land, 1f, t);
                    position = Vector3.Lerp(fruit.from, fruit.to, .85f + .15f * f);
                    position.y = fruit.to.y + Mathf.Sin(f * Mathf.PI) * Mathf.Min(.06f, fruit.radius * .5f);
                }
                fruit.pickup.position = position;
                fruit.mesh.rotation = Quaternion.Slerp(fruit.rollFrom, fruit.rollTo, Mathf.SmoothStep(0f, 1f, t));
                if (t < 1f) break;
                fruit.state = State.Grounded;
                fruit.timer = 0f;
                fruit.trigger.enabled = true;
                fruit.solidPending = true;
                fruit.edible.enabled = true;
                break;

            case State.Grounded:
                if (fruit.edible.IsConsumed) { GoneNow(fruit); break; }
                if (fruit.solidPending && !OverlapsCharacter(fruit))
                {
                    fruit.solidPending = false;
                    fruit.solid.enabled = true;
                }
                fruit.timer += dt;
                if (caterpillars && fruit.caterpillar == null && fruit.timer >= caterpillarAfter && caterpillarPrefab != null)
                {
                    var bug = Instantiate(caterpillarPrefab, fruit.pickup.position, Quaternion.identity, transform);
                    fruit.caterpillar = bug.GetComponent<AppleCaterpillar>();
                    if (fruit.caterpillar != null)
                        fruit.caterpillar.Begin(fruit.edible, fruit.radius, () =>
                        {
                            // Finished by the caterpillar: gone, but not eaten by the player.
                            if (!fruit.edible.IsConsumed) fruit.edible.MarkConsumed();
                        });
                }
                break;

            case State.Gone:
                fruit.timer -= dt;
                if (fruit.timer > 0f) break;
                // Grows back where it hung, swelling from nothing.
                fruit.edible.ResetForReuse();
                fruit.edible.enabled = false;
                fruit.trigger.enabled = false;
                fruit.solid.enabled = false;
                fruit.solidPending = false;
                fruit.pickup.localPosition = fruit.hangLocal;
                fruit.pickup.localRotation = Quaternion.identity;
                fruit.pickup.localScale = Vector3.one;           // a caterpillar may have nibbled it small
                fruit.mesh.localPosition = fruit.meshLocal;
                fruit.mesh.localRotation = fruit.meshRotation;
                fruit.mesh.localScale = Vector3.zero;
                fruit.state = State.Swelling;
                fruit.t = 0f;
                break;

            case State.Swelling:
                fruit.t = Mathf.Min(1f, fruit.t + dt / swellSeconds);
                fruit.mesh.localScale = fruit.meshScale * Mathf.SmoothStep(0f, 1f, fruit.t);
                if (fruit.t >= 1f) fruit.state = State.Hanging;
                break;
        }
    }

    private void GoneNow(Fruit fruit)
    {
        fruit.state = State.Gone;
        fruit.timer = regrowSeconds * Random.Range(.8f, 1.2f);
        if (fruit.caterpillar != null) fruit.caterpillar.Leave();
        fruit.caterpillar = null;
    }

    // One ripe hanging fruit falls to the ground under the crown.
    private void DropOne()
    {
        var hanging = fruits.FindAll(f => f.state == State.Hanging && !f.edible.IsConsumed);
        if (hanging.Count == 0) return;
        Fruit fruit = hanging[Random.Range(0, hanging.Count)];
        Vector3 start = fruit.pickup.position;
        Vector3 trunk = transform.position;
        Vector3 landing = LandingSpot(fruit, start, trunk);
        fruit.from = start;
        fruit.to = landing;
        fruit.duration = Mathf.Clamp(Mathf.Sqrt(2f * Mathf.Max(.1f, start.y - landing.y) / 9.81f) / .8f, .45f, 1.6f);
        fruit.t = 0f;
        fruit.rollFrom = fruit.mesh.rotation;
        fruit.rollTo = Quaternion.Euler(Random.Range(-40f, 40f), Random.Range(0f, 360f), Random.Range(-40f, 40f)) * fruit.mesh.rotation;
        fruit.state = State.Falling;
    }

    // Under the fruit, scattered a little; never into the pond or another low hollow (more than a step below the
    // tree's foot): then nearer the trunk instead.
    private Vector3 LandingSpot(Fruit fruit, Vector3 start, Vector3 trunk)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            Vector3 spot = attempt < 4
                ? start + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(.1f, .6f)
                : trunk + Flat(start - trunk).normalized * Mathf.Lerp(.7f, 1.2f, attempt - 4);
            if (Ground(new Vector3(spot.x, start.y + .3f, spot.z), out float y) && y > trunk.y - .3f)
                return new Vector3(spot.x, y + fruit.radius * .9f, spot.z);
        }
        Vector3 fallback = trunk + Flat(start - trunk).normalized * .8f;
        return new Vector3(fallback.x, trunk.y + fruit.radius * .9f, fallback.z);
    }

    // The highest surface under a point that is not this tree, a character or food.
    private bool Ground(Vector3 from, out float y)
    {
        y = 0f;
        bool found = false;
        foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, 20f, ~(1 << EdibleLayer), QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.collider is CharacterController ||
                hit.collider.GetComponentInParent<ChickPlayerController>() != null ||
                hit.collider.GetComponentInParent<HelperChickController>() != null) continue;
            if (!found || hit.point.y > y) { y = hit.point.y; found = true; }
        }
        return found;
    }

    private static bool OverlapsCharacter(Fruit fruit)
    {
        Vector3 half = Vector3.Scale(fruit.solid.size * .5f, fruit.pickup.lossyScale);
        int count = Physics.OverlapBoxNonAlloc(fruit.pickup.TransformPoint(fruit.solid.center), half, Overlaps,
            fruit.pickup.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) if (Overlaps[i] is CharacterController) return true;
        return false;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v : Vector3.forward;
    }
}
