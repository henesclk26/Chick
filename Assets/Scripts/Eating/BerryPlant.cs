using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes a berry plant's ripe berries edible (strawberry plant, blackberry and strawberry bushes, tomato and pepper plants, lettuces). They hang out of reach until
/// the player pecks at the plant: then the nearest hanging berry drops to the ground toward the player and can
/// be eaten there — or, on a plant whose pieces are eaten where they hang (the grape vine), that peck eats it.
/// Added to the plant models on import (BerryPlantImporter), so every placed plant (model or prefab) gets it.
/// Unripe berries ("*_Unripe_*") stay decoration.
/// </summary>
[DisallowMultipleComponent]
public sealed class BerryPlant : MonoBehaviour
{
    private const int EdibleLayer = 9;
    private static readonly List<BerryPlant> ActivePlants = new List<BerryPlant>();
    private static readonly Collider[] Overlaps = new Collider[16];

    [Tooltip("Ripe berries are the children whose names start with this and do not contain \"Unripe\".")]
    [SerializeField] private string berryPrefix = "Strawberry_";
    [Tooltip("Food statistics key; also prefixes each berry's save id.")]
    [SerializeField] private string foodKey = "strawberry";
    [Tooltip("Name shown in the food statistics.")]
    [SerializeField] private string foodName = "Çilek";
    [Tooltip("How close (m, from the plant's centre) a chick must peck to shake a berry loose; scaled up for the chicken.")]
    [SerializeField, Min(.1f)] private float knockReach = .45f;
    [Tooltip("Distance from the plant's centre where dropped berries land, clear of the plant's base.")]
    [SerializeField, Min(.05f)] private float dropRadius = .25f;
    [SerializeField, Min(.1f)] private float fallSeconds = .45f;
    [Tooltip("The pieces are leaves modelled flat in their own frame (blade in local XZ): they fall and lie down " +
             "flat instead of keeping the pose they hang in.")]
    [SerializeField] private bool layFlat;
    [Tooltip("The pieces are long fruits hanging along their length (peppers): they fall and lie down on their side " +
             "instead of standing on end.")]
    [SerializeField] private bool lieDown;
    [Tooltip("Optional: a child named this plus a piece's suffix (e.g. Stub_07 for Lettuce_07) is what is left on the " +
             "plant once that piece is gone; it is hidden while the piece hangs.")]
    [SerializeField] private string stubPrefix = "";
    [Tooltip("Optional: a child named this plus a piece's suffix (e.g. Half_07 for Lettuce_07) is the piece after its " +
             "first peck on the ground; such a piece takes two pecks (the child itself stays hidden, only its mesh is used).")]
    [SerializeField] private string halfPrefix = "";
    [Tooltip("The plant is peeled from the outside in: a knocked piece comes off its outermost layer and lies out along " +
             "its own side (keeping its heading) just past the pieces still hanging, instead of flying toward the pecker; " +
             "dropRadius is then the base's edge, which even the last pieces slip just off onto the ground.")]
    [SerializeField] private bool fallInPlace;
    [Tooltip("The pieces are eaten where they hang (grapes): a peck at the plant eats the nearest hanging piece on the " +
             "pecker's side of the row (the row runs along local X) in that head bob and nothing falls; knockReach is " +
             "then measured from that piece, not the plant's centre.")]
    [SerializeField] private bool eatOnPlant;

    // When, in a plant peck's head bob, the beak reaches a piece eaten where it hangs.
    private const float EatOnPlantDelay = .14f;

    public string FoodKey => foodKey;

    /// <summary>Import-time setup for a plant whose berries are not strawberries.</summary>
    public void Configure(string prefix, string key, string displayName, float reach, float drop, bool flat = false,
        string stubs = "", string halves = "", bool inPlace = false, bool lie = false, bool eatHanging = false)
    {
        berryPrefix = prefix;
        foodKey = key;
        foodName = displayName;
        knockReach = reach;
        dropRadius = drop;
        layFlat = flat;
        stubPrefix = stubs;
        halfPrefix = halves;
        fallInPlace = inPlace;
        lieDown = lie;
        eatOnPlant = eatHanging;
    }

    private sealed class Berry
    {
        public Transform pickup, mesh;
        public EdibleObject edible;
        public Collider trigger;
        // Solid sphere on the mesh (added on import) while it hangs; an upright box once it lies on the
        // ground, so characters bump into it without riding up its curve (on top they could not reach it to eat).
        public Collider hangingSolid, groundSolid;
        public float halfHeight;
        public bool falling, dropped;
        // Landed, but the ground box waits until no character stands in it, so it never shoves one aside.
        public bool solidPending;
        public Vector3 from, to;
        public Quaternion meshFrom, meshTo;
        public Vector3 meshPosFrom, meshPosTo;
        public float t;
        public GameObject stub;
        public Mesh half;
        public Transform bite;
        public bool bitten;
        // A piece lying along its own side of the plant: the line it rests on (outer end, inward slope per metre).
        public bool lying;
        public Vector3 restFar, restToward;
        public float restSlope;
        // Pecked where it hangs (eatOnPlant): eaten once the head bob reaches it.
        public bool eating;
        public float eatAt;
    }

    private readonly List<Berry> berries = new List<Berry>();
    // How far the outermost piece hangs from the centre: the full head, for pieces that fall in place.
    private float headRadius;

    public int HangingCount
    {
        get
        {
            int count = 0;
            foreach (Berry berry in berries) if (IsHanging(berry)) count++;
            return count;
        }
    }

    private void Awake() => Build();
    private void OnEnable() { if (!ActivePlants.Contains(this)) ActivePlants.Add(this); }
    private void OnDisable() => ActivePlants.Remove(this);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => ActivePlants.Clear();

    // Each ripe berry becomes an edible pickup (same shape as other food: trigger on the Edible layer,
    // BitePoint and moving visual as children). It stays disabled while it hangs on the plant.
    private void Build()
    {
        if (berries.Count > 0) return;
        var ripe = new List<Transform>();
        foreach (Transform child in transform)
            if (child.name.StartsWith(berryPrefix) && !child.name.Contains("Unripe") && child.GetComponent<Renderer>() != null)
                ripe.Add(child);

        Vector3 plantPosition = transform.position;
        string plantId = foodKey + "-" + Mathf.RoundToInt(plantPosition.x * 100f) + "_" +
                         Mathf.RoundToInt(plantPosition.y * 100f) + "_" + Mathf.RoundToInt(plantPosition.z * 100f);
        foreach (Transform mesh in ripe)
        {
            Bounds bounds = mesh.GetComponent<Renderer>().bounds;
            var pickup = new GameObject(mesh.name + " Pickup") { layer = EdibleLayer };
            pickup.transform.SetParent(transform, false);
            pickup.transform.SetPositionAndRotation(bounds.center, Quaternion.identity);
            mesh.SetParent(pickup.transform, true);

            // On the ground a berry keeps its hanging shape; a leaf lies flat, so its footprint and height come
            // from its own (flat) mesh bounds; a long fruit lies on its side, as long and as thick as it hangs.
            Vector3 ground = bounds.extents;
            if (layFlat && mesh.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                ground = Vector3.Scale(filter.sharedMesh.bounds.extents, mesh.lossyScale);
            else if (lieDown)
                ground = new Vector3(ground.y, Mathf.Min(ground.x, ground.z), ground.y);

            var trigger = pickup.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = Mathf.Max(ground.x, ground.z) * .8f;
            trigger.enabled = false;

            var groundSolid = pickup.AddComponent<BoxCollider>();
            float width = Mathf.Max(ground.x, ground.z) * (layFlat ? 1.2f : 1.6f);
            groundSolid.size = new Vector3(width, ground.y * 2f, width);
            groundSolid.enabled = false;

            var bite = new GameObject("BitePoint").transform;
            bite.SetParent(pickup.transform, false);
            bite.localPosition = new Vector3(0f, -(layFlat || lieDown ? ground.y : bounds.extents.y) * .45f, 0f);

            var edible = pickup.AddComponent<EdibleObject>();
            // Plant position keeps ids unique across several plants with identical child names.
            // A leaf dropped in place can rest on the base's rim, a little above the beak of a chick standing beside it.
            edible.ConfigureRuntime(plantId + "-" + mesh.name, EdibleCategory.Other, foodKey, foodName,
                bite, mesh, 1.35f, fallInPlace ? .12f : .06f);
            edible.enabled = false;

            string suffix = mesh.name.Substring(berryPrefix.Length);
            Transform stub = string.IsNullOrEmpty(stubPrefix) ? null : transform.Find(stubPrefix + suffix);
            if (stub != null) stub.gameObject.SetActive(false);
            Transform half = string.IsNullOrEmpty(halfPrefix) ? null : transform.Find(halfPrefix + suffix);
            if (half != null) half.gameObject.SetActive(false);

            var piece = new Berry
            {
                pickup = pickup.transform, mesh = mesh, edible = edible, trigger = trigger,
                hangingSolid = mesh.GetComponent<SphereCollider>(), groundSolid = groundSolid, halfHeight = ground.y,
                stub = stub != null ? stub.gameObject : null,
                half = half != null && half.TryGetComponent(out MeshFilter halfFilter) ? halfFilter.sharedMesh : null,
                bite = bite
            };
            if (piece.half != null)
            {
                edible.BiteHandler = () => BiteHalf(piece);
                // A big cupped leaf stands tall on the ground: peck it at its foot, within the beak's reach.
                bite.localPosition = new Vector3(0f, -ground.y * .85f, 0f);
            }
            berries.Add(piece);
            headRadius = Mathf.Max(headRadius, Flat(pickup.transform.position - plantPosition).magnitude);
        }
    }

    // How far the furthest still-hanging piece (other than skip) hangs from the centre.
    private float HangingRadius(Berry skip)
    {
        float radius = 0f;
        foreach (Berry berry in berries)
            if (berry != skip && IsHanging(berry))
                radius = Mathf.Max(radius, Flat(berry.pickup.position - transform.position).magnitude);
        return radius;
    }

    // A piece with a half mesh takes two pecks on the ground: the first leaves its half, the second eats it.
    private static bool BiteHalf(Berry piece)
    {
        if (piece.bitten) return true;
        piece.bitten = true;
        piece.mesh.GetComponent<MeshFilter>().sharedMesh = piece.half;
        // Peck what is left, not the empty spot where the eaten half lay.
        Vector3 left = piece.mesh.TransformPoint(piece.half.bounds.center);
        if (!piece.lying)
        {
            piece.bite.position = new Vector3(left.x, piece.bite.position.y, left.z);
            return false;
        }
        // Lying across the base, what is left may reach up onto it: peck its outer part, which lies lowest.
        Transform mesh = piece.mesh;
        Vector3 extents = Vector3.Scale(piece.half.bounds.extents, mesh.lossyScale);
        float along = Mathf.Abs(Vector3.Dot(mesh.right, piece.restToward)) * extents.x +
                      Mathf.Abs(Vector3.Dot(mesh.up, piece.restToward)) * extents.y +
                      Mathf.Abs(Vector3.Dot(mesh.forward, piece.restToward)) * extents.z;
        Vector3 bite = left + piece.restToward * (along * .7f);
        bite.y = RestHeight(piece, bite);
        piece.bite.position = bite;
        ((SphereCollider)piece.trigger).center = piece.pickup.InverseTransformPoint(bite);
        return false;
    }

    // How high the pickup must stand above the ground under it so that the piece, in its landing pose (meshTo,
    // meshPosTo), just touches a ground rising at 'slope' back toward the plant (against 'toward'): from its vertices.
    // 'fallback' when the mesh cannot be read.
    private static float Clearance(Berry piece, Vector3 toward, float slope, float fallback)
    {
        var filter = piece.mesh.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) return fallback;
        Vector3 scale = piece.mesh.localScale;
        float lowest = float.MaxValue;
        foreach (Vector3 v in filter.sharedMesh.vertices)
        {
            Vector3 offset = piece.pickup.TransformVector(piece.meshPosTo + piece.meshTo * Vector3.Scale(scale, v));
            lowest = Mathf.Min(lowest, offset.y + slope * Vector3.Dot(Flat(offset), toward));
        }
        return -lowest - .003f;      // a hair into the ground, never a gap
    }

    private static float RestHeight(Berry piece, Vector3 point) =>
        piece.restFar.y + piece.restSlope * Vector3.Dot(Flat(piece.restFar - point), piece.restToward);

    private static bool IsHanging(Berry berry) =>
        !berry.dropped && !berry.falling && !berry.eating && berry.pickup.gameObject.activeInHierarchy &&
        !berry.edible.IsConsumed;

    // The nearest hanging piece (on the ground plane) within reach of a pecker facing it, or null. Only the pieces on
    // the pecker's side of the row (the row runs along the plant's local X): the far side's are out of its reach.
    private Berry FacedPiece(Vector3 peckerPosition, Vector3 facing, float reach)
    {
        Berry nearest = null;
        float best = reach;
        float peckerSide = Vector3.Dot(peckerPosition - transform.position, transform.forward);
        foreach (Berry berry in berries)
        {
            if (!IsHanging(berry) || Vector3.Dot(berry.pickup.position - transform.position, transform.forward) * peckerSide <= 0f)
                continue;
            Vector3 toPiece = Flat(berry.pickup.position - peckerPosition);
            float distance = toPiece.magnitude;
            if (distance > best) continue;
            if (distance > .08f && Vector3.Dot(toPiece / distance, Flat(facing).normalized) < .2f) continue;
            nearest = berry;
            best = distance;
        }
        return nearest;
    }

    // A piece eaten where it hangs: it goes when the head bob reaches it (see Update).
    private bool EatHanging(Berry piece)
    {
        piece.eating = true;
        piece.eatAt = Time.time + EatOnPlantDelay;
        return true;
    }

    /// <summary>
    /// A peck that found no food: if it was at a berry plant, drop its nearest hanging ripe berry toward
    /// the pecker — or, on a plant eaten where it hangs, eat the nearest piece the pecker faces. sizeScale grows the
    /// reach for larger forms (chicken). Returns true if a berry fell or is being eaten.
    /// </summary>
    public static bool TryKnockNear(Vector3 peckerPosition, Vector3 facing, float sizeScale)
    {
        BerryPlant best = null;
        Berry bestPiece = null;
        float bestDistance = float.PositiveInfinity;
        foreach (BerryPlant plant in ActivePlants)
        {
            if (plant == null || plant.HangingCount == 0) continue;
            if (plant.eatOnPlant)
            {
                // Reach is from the piece itself: a long vine row is pecked anywhere along it.
                Berry piece = plant.FacedPiece(peckerPosition, facing, plant.knockReach * sizeScale);
                float reach = piece != null ? Flat(piece.pickup.position - peckerPosition).magnitude : float.PositiveInfinity;
                if (reach < bestDistance) { best = plant; bestPiece = piece; bestDistance = reach; }
                continue;
            }
            Vector3 toPlant = plant.transform.position - peckerPosition;
            toPlant.y = 0f;
            float distance = toPlant.magnitude;
            if (distance > plant.knockReach * sizeScale) continue;
            // Facing the plant (or standing right at it).
            if (distance > .08f && Vector3.Dot(toPlant / distance, Flat(facing).normalized) < .2f) continue;
            if (distance < bestDistance) { best = plant; bestPiece = null; bestDistance = distance; }
        }
        if (best == null) return false;
        return bestPiece != null ? best.EatHanging(bestPiece) : best.Knock(peckerPosition);
    }

    private bool Knock(Vector3 peckerPosition)
    {
        // A head that falls in place is eaten from the outside in: only its outermost hanging layer comes off.
        float outermost = fallInPlace ? HangingRadius(null) - .04f : float.NegativeInfinity;
        Berry chosen = null;
        float bestDistance = float.PositiveInfinity;
        foreach (Berry berry in berries)
        {
            if (!IsHanging(berry) || Flat(berry.pickup.position - transform.position).magnitude < outermost) continue;
            float distance = Flat(berry.pickup.position - peckerPosition).sqrMagnitude;
            if (distance < bestDistance) { chosen = berry; bestDistance = distance; }
        }
        if (chosen == null) return false;

        chosen.meshFrom = chosen.mesh.localRotation;
        chosen.meshPosFrom = chosen.meshPosTo = chosen.mesh.localPosition;
        Bounds meshBounds = chosen.mesh.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null
            ? filter.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.zero);
        if (layFlat)
        {
            // Lie the leaf down on its back (blade in local XZ, so identity is flat), turned at random — or, falling
            // in place, tipped over just enough to lie flat so it keeps the heading it had on the plant — and
            // centre it on the pickup so it rests on the ground.
            chosen.meshTo = fallInPlace
                ? Quaternion.Euler(0f, Random.Range(-15f, 15f), 0f) *
                  Quaternion.FromToRotation(chosen.meshFrom * Vector3.up, Vector3.up) * chosen.meshFrom
                : Quaternion.Euler(Random.Range(-6f, 6f), Random.Range(0f, 360f), Random.Range(-6f, 6f));
            chosen.meshPosTo = -(chosen.meshTo * Vector3.Scale(meshBounds.center, chosen.mesh.lossyScale));
        }
        else if (lieDown)
        {
            // Tip the fruit over: its long axis (as it hangs) turned level, rolled and headed at random, centred on
            // the pickup.
            Vector3 size = meshBounds.size;
            Vector3 axis = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
            axis = chosen.meshFrom * axis;
            Vector3 level = Flat(axis).sqrMagnitude > 1e-4f ? Flat(axis).normalized : Vector3.right;
            chosen.meshTo = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Quaternion.FromToRotation(axis, level) *
                            Quaternion.AngleAxis(Random.Range(0f, 360f), axis) * chosen.meshFrom;
            chosen.meshPosTo = -(chosen.meshTo * Vector3.Scale(meshBounds.center, chosen.mesh.lossyScale));
        }
        else
        {
            chosen.meshTo = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f)) * chosen.meshFrom;
        }

        // Land on the ground between the plant and the pecker, a little to either side, clear of the base; or, falling
        // in place, beside where the piece grew, on its own side of the plant.
        Vector3 center = transform.position;
        Vector3 own = Flat(chosen.pickup.position - center);
        Vector3 toward = fallInPlace ? own : Flat(peckerPosition - center);
        if (toward.sqrMagnitude < 1e-4f) toward = fallInPlace ? Flat(peckerPosition - center) : own;
        if (toward.sqrMagnitude < 1e-4f) toward = Vector3.forward;
        toward = Quaternion.Euler(0f, fallInPlace ? Random.Range(-12f, 12f) : Random.Range(-35f, 35f), 0f) * toward.normalized;
        Vector3 landing;
        if (fallInPlace)
        {
            // How far the lying piece reaches back toward the plant from its middle.
            Vector3 extents = Vector3.Scale(meshBounds.extents, chosen.mesh.lossyScale);
            float back = Mathf.Abs(Vector3.Dot(chosen.meshTo * Vector3.right, toward)) * extents.x +
                         Mathf.Abs(Vector3.Dot(chosen.meshTo * Vector3.up, toward)) * extents.y +
                         Mathf.Abs(Vector3.Dot(chosen.meshTo * Vector3.forward, toward)) * extents.z;
            // A peeled leaf opens outward just enough to clear the head still standing: an outer leaf lies out past
            // it, and the fewer leaves are left, the less it is thrown. Even the heart's last leaves land on the ground,
            // though: they slip just off the base's edge (dropRadius), at most their inner end left on its rim.
            float head = HangingRadius(chosen);
            float clear = headRadius > 0f ? Mathf.Clamp01(head / headRadius) : 0f;
            float reach = Mathf.Max(own.magnitude, head + back * clear, dropRadius + back * .5f) + Random.Range(.01f, .03f);
            landing = center + toward * reach;
            landing.y = LieAlong(chosen, landing, toward, back, meshBounds);
        }
        else
        {
            landing = center + toward * (dropRadius + Random.Range(0f, .05f));
            // On its lowest point, in the pose it lands in.
            landing.y = GroundHeight(landing, false) + Clearance(chosen, toward, 0f, chosen.halfHeight * (layFlat ? 1.1f : .9f));
        }

        chosen.from = chosen.pickup.position;
        chosen.to = landing;
        chosen.t = 0f;
        chosen.falling = true;
        // A falling piece is moved by hand, so a solid one would push the pecker standing below it.
        if (chosen.hangingSolid != null) chosen.hangingSolid.enabled = false;
        return true;
    }

    // A leaf lying along its side of the plant: its outer end on what is under it, the rest propped on the mound,
    // tilted along its length rather than sunk into it. Its bite moves onto the low outer part. Returns the pickup's height.
    private float LieAlong(Berry piece, Vector3 landing, Vector3 toward, float back, Bounds meshBounds)
    {
        const int Samples = 8;
        Vector3 far = landing + toward * back;
        float farY = GroundHeight(far, true);
        // Steepest rise from the outer end over the leaf's length: the line it rests on.
        float slope = -.4f;
        for (int i = 1; i <= Samples; i++)
        {
            float distance = 2f * back * i / Samples;
            slope = Mathf.Max(slope, (GroundHeight(far - toward * distance, true) - farY) / distance);
        }
        slope = Mathf.Min(slope, .7f);
        piece.meshTo = Quaternion.AngleAxis(Mathf.Atan(slope) * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, toward)) * piece.meshTo;
        piece.meshPosTo = -(piece.meshTo * Vector3.Scale(meshBounds.center, piece.mesh.lossyScale));

        piece.lying = true;
        piece.restFar = new Vector3(far.x, farY, far.z);
        piece.restToward = toward;
        piece.restSlope = slope;
        float pickupY = farY + slope * back + Clearance(piece, toward, slope, piece.halfHeight * 1.1f);
        // Near its outer end, on the line it rests on: low enough for a beak at ground level.
        Vector3 bite = landing + toward * (back * .85f);
        bite.y = RestHeight(piece, bite);
        landing.y = pickupY;
        piece.bite.localPosition = Quaternion.Inverse(piece.pickup.rotation) * (bite - landing);
        // Eaters find food by its trigger: keep it on the bite, not on the leaf's far-off middle.
        ((SphereCollider)piece.trigger).center = piece.bite.localPosition;
        return pickupY;
    }

    // Highest surface under a point that is not a character (the chick, chicken or helper may be standing right
    // there) nor part of this plant, except its base with includeBase; the plant's own height otherwise.
    private float GroundHeight(Vector3 point, bool includeBase)
    {
        float groundY = transform.position.y;
        bool found = false;
        foreach (RaycastHit hit in Physics.RaycastAll(point + Vector3.up * .6f, Vector3.down, 3f, ~(1 << EdibleLayer), QueryTriggerInteraction.Ignore))
        {
            bool plantPart = hit.collider.transform.IsChildOf(transform) &&
                             (!includeBase || hit.collider.GetComponentInParent<EdibleObject>() != null);
            if (plantPart || hit.collider is CharacterController ||
                hit.collider.GetComponentInParent<ChickPlayerController>() != null ||
                hit.collider.GetComponentInParent<HelperChickController>() != null ||
                (found && hit.point.y <= groundY)) continue;
            groundY = hit.point.y;
            found = true;
        }
        return groundY;
    }

    private void Update()
    {
        // Scaled time: a falling berry freezes with the game in pause and the journal.
        float dt = Time.deltaTime;
        foreach (Berry berry in berries)
        {
            // A piece that has left the plant (falling, on the ground, eaten or restored as eaten) shows its stub.
            if (berry.stub != null && berry.stub.activeSelf == IsHanging(berry)) berry.stub.SetActive(!IsHanging(berry));
            if (berry.solidPending && !OverlapsCharacter(berry))
            {
                berry.solidPending = false;
                berry.groundSolid.enabled = berry.pickup.gameObject.activeInHierarchy && !berry.edible.IsConsumed;
            }
            if (berry.eating && Time.time >= berry.eatAt)
            {
                // Eaten where it hangs: counted like any food eaten (statistics, growth, save).
                berry.eating = false;
                berry.edible.enabled = true;
                if (!berry.edible.Consume()) berry.edible.enabled = false;
            }
            if (!berry.falling) continue;
            berry.t = Mathf.Min(1f, berry.t + dt / fallSeconds);
            float t = berry.t;
            Vector3 position = Vector3.Lerp(berry.from, berry.to, Mathf.SmoothStep(0f, 1f, t));
            // Accelerating drop with a small bounce as it lands.
            position.y = Mathf.Lerp(berry.from.y, berry.to.y, t * t) + Mathf.Sin(Mathf.InverseLerp(.8f, 1f, t) * Mathf.PI) * .025f;
            berry.pickup.position = position;
            berry.mesh.localRotation = Quaternion.Slerp(berry.meshFrom, berry.meshTo, t);
            berry.mesh.localPosition = Vector3.Lerp(berry.meshPosFrom, berry.meshPosTo, Mathf.SmoothStep(0f, 1f, t));
            if (t < 1f) continue;
            berry.falling = false;
            berry.dropped = true;
            berry.trigger.enabled = true;
            if (berry.hangingSolid != null) berry.hangingSolid.enabled = false;
            // A fallen leaf stays passable, like other flat food on the ground: the chick walks onto it and eats.
            berry.solidPending = !layFlat;
            berry.edible.enabled = true;
        }
    }

    // Any chick, chicken or helper capsule inside where the ground box would be.
    private static bool OverlapsCharacter(Berry berry)
    {
        var box = (BoxCollider)berry.groundSolid;
        Transform pickup = berry.pickup;
        Vector3 halfExtents = Vector3.Scale(box.size * .5f, pickup.lossyScale);
        int count = Physics.OverlapBoxNonAlloc(pickup.TransformPoint(box.center), halfExtents, Overlaps, pickup.rotation,
            ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (Overlaps[i] is CharacterController) return true;
        return false;
    }

    private static Vector3 Flat(Vector3 value)
    {
        value.y = 0f;
        return value;
    }
}
