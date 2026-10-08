using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes a berry plant's ripe berries edible (strawberry plant, blackberry and strawberry bushes, tomato plant, lettuces). They hang out of reach until
/// the player pecks at the plant: then the nearest hanging berry drops to the ground toward the player and can
/// be eaten there. Added to the plant models on import (BerryPlantImporter), so every placed plant (model or
/// prefab) gets it. Unripe berries ("*_Unripe_*") stay decoration.
/// </summary>
[DisallowMultipleComponent]
public sealed class BerryPlant : MonoBehaviour
{
    private const int EdibleLayer = 9;
    private static readonly List<BerryPlant> ActivePlants = new List<BerryPlant>();

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
    [Tooltip("Optional: a child named this plus a piece's suffix (e.g. Stub_07 for Lettuce_07) is what is left on the " +
             "plant once that piece is gone; it is hidden while the piece hangs.")]
    [SerializeField] private string stubPrefix = "";
    [Tooltip("Optional: a child named this plus a piece's suffix (e.g. Half_07 for Lettuce_07) is the piece after its " +
             "first peck on the ground; such a piece takes two pecks (the child itself stays hidden, only its mesh is used).")]
    [SerializeField] private string halfPrefix = "";
    [Tooltip("A knocked piece slides down off its own side of the plant (keeping its heading) instead of flying " +
             "toward the pecker; dropRadius is then the least distance it lands from the centre.")]
    [SerializeField] private bool fallInPlace;

    public string FoodKey => foodKey;

    /// <summary>Import-time setup for a plant whose berries are not strawberries.</summary>
    public void Configure(string prefix, string key, string displayName, float reach, float drop, bool flat = false,
        string stubs = "", string halves = "", bool inPlace = false)
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
        public Vector3 from, to;
        public Quaternion meshFrom, meshTo;
        public Vector3 meshPosFrom, meshPosTo;
        public float t;
        public GameObject stub;
        public Mesh half;
        public Transform bite;
        public bool bitten;
    }

    private readonly List<Berry> berries = new List<Berry>();

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
            // from its own (flat) mesh bounds.
            Vector3 ground = bounds.extents;
            if (layFlat && mesh.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                ground = Vector3.Scale(filter.sharedMesh.bounds.extents, mesh.lossyScale);

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
            bite.localPosition = new Vector3(0f, -(layFlat ? ground.y : bounds.extents.y) * .45f, 0f);

            var edible = pickup.AddComponent<EdibleObject>();
            // Plant position keeps ids unique across several plants with identical child names.
            edible.ConfigureRuntime(plantId + "-" + mesh.name, EdibleCategory.Other, foodKey, foodName,
                bite, mesh, 1.35f, .06f);
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
        }
    }

    // A piece with a half mesh takes two pecks on the ground: the first leaves its half, the second eats it.
    private static bool BiteHalf(Berry piece)
    {
        if (piece.bitten) return true;
        piece.bitten = true;
        piece.mesh.GetComponent<MeshFilter>().sharedMesh = piece.half;
        // Peck what is left, not the empty spot where the eaten half lay.
        Vector3 left = piece.mesh.TransformPoint(piece.half.bounds.center);
        piece.bite.position = new Vector3(left.x, piece.bite.position.y, left.z);
        return false;
    }

    private static bool IsHanging(Berry berry) =>
        !berry.dropped && !berry.falling && berry.pickup.gameObject.activeInHierarchy && !berry.edible.IsConsumed;

    /// <summary>
    /// A peck that found no food: if it was at a berry plant, drop its nearest hanging ripe berry toward
    /// the pecker. sizeScale grows the reach for larger forms (chicken). Returns true if a berry fell.
    /// </summary>
    public static bool TryKnockNear(Vector3 peckerPosition, Vector3 facing, float sizeScale)
    {
        BerryPlant best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (BerryPlant plant in ActivePlants)
        {
            if (plant == null || plant.HangingCount == 0) continue;
            Vector3 toPlant = plant.transform.position - peckerPosition;
            toPlant.y = 0f;
            float distance = toPlant.magnitude;
            if (distance > plant.knockReach * sizeScale) continue;
            // Facing the plant (or standing right at it).
            if (distance > .08f && Vector3.Dot(toPlant / distance, Flat(facing).normalized) < .2f) continue;
            if (distance < bestDistance) { best = plant; bestDistance = distance; }
        }
        return best != null && best.Knock(peckerPosition);
    }

    private bool Knock(Vector3 peckerPosition)
    {
        Berry chosen = null;
        float bestDistance = float.PositiveInfinity;
        foreach (Berry berry in berries)
        {
            if (!IsHanging(berry)) continue;
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
        else
        {
            chosen.meshTo = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f)) * chosen.meshFrom;
        }

        // Land on the ground between the plant and the pecker, a little to either side, clear of the base; or, falling
        // in place, down off the piece's own side of the plant, its near end just past dropRadius (the base's edge).
        Vector3 center = transform.position;
        Vector3 own = Flat(chosen.pickup.position - center);
        Vector3 toward = fallInPlace ? own : Flat(peckerPosition - center);
        if (toward.sqrMagnitude < 1e-4f) toward = fallInPlace ? Flat(peckerPosition - center) : own;
        if (toward.sqrMagnitude < 1e-4f) toward = Vector3.forward;
        toward = Quaternion.Euler(0f, fallInPlace ? Random.Range(-12f, 12f) : Random.Range(-35f, 35f), 0f) * toward.normalized;
        float reach = dropRadius + Random.Range(0f, .05f);
        if (fallInPlace)
        {
            // How far the lying piece reaches back toward the plant from its middle.
            Vector3 extents = Vector3.Scale(meshBounds.extents, chosen.mesh.lossyScale);
            float back = Mathf.Abs(Vector3.Dot(chosen.meshTo * Vector3.right, toward)) * extents.x +
                         Mathf.Abs(Vector3.Dot(chosen.meshTo * Vector3.up, toward)) * extents.y +
                         Mathf.Abs(Vector3.Dot(chosen.meshTo * Vector3.forward, toward)) * extents.z;
            reach = Mathf.Max(own.magnitude, dropRadius + back) + Random.Range(0f, .04f);
        }
        Vector3 landing = center + toward * reach;
        // Highest surface under the landing spot that is not part of the plant or a character (the chick,
        // chicken or helper may be standing right there); the plant's base otherwise.
        float groundY = center.y;
        bool found = false;
        foreach (RaycastHit hit in Physics.RaycastAll(landing + Vector3.up * .6f, Vector3.down, 3f, ~(1 << EdibleLayer), QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.collider is CharacterController ||
                hit.collider.GetComponentInParent<ChickPlayerController>() != null ||
                hit.collider.GetComponentInParent<HelperChickController>() != null ||
                (found && hit.point.y <= groundY)) continue;
            groundY = hit.point.y;
            found = true;
        }
        // A curled leaf's rim dips below its bounds' middle plane, so it rests a touch higher than a berry.
        landing.y = groundY + chosen.halfHeight * (layFlat ? 1.1f : .9f);

        chosen.from = chosen.pickup.position;
        chosen.to = landing;
        chosen.t = 0f;
        chosen.falling = true;
        return true;
    }

    private void Update()
    {
        // Scaled time: a falling berry freezes with the game in pause and the journal.
        float dt = Time.deltaTime;
        foreach (Berry berry in berries)
        {
            // A piece that has left the plant (falling, on the ground, eaten or restored as eaten) shows its stub.
            if (berry.stub != null && berry.stub.activeSelf == IsHanging(berry)) berry.stub.SetActive(!IsHanging(berry));
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
            berry.groundSolid.enabled = !layFlat;
            berry.edible.enabled = true;
        }
    }

    private static Vector3 Flat(Vector3 value)
    {
        value.y = 0f;
        return value;
    }
}
