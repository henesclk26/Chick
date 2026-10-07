using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes a berry plant's ripe berries edible (strawberry plant, blackberry and strawberry bushes, tomato plant). They hang out of reach until
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

    public string FoodKey => foodKey;

    /// <summary>Import-time setup for a plant whose berries are not strawberries.</summary>
    public void Configure(string prefix, string key, string displayName, float reach, float drop)
    {
        berryPrefix = prefix;
        foodKey = key;
        foodName = displayName;
        knockReach = reach;
        dropRadius = drop;
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
        public float t;
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

            var trigger = pickup.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = Mathf.Max(bounds.extents.x, bounds.extents.z) * .8f;
            trigger.enabled = false;

            var groundSolid = pickup.AddComponent<BoxCollider>();
            float width = Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.6f;
            groundSolid.size = new Vector3(width, bounds.size.y, width);
            groundSolid.enabled = false;

            var bite = new GameObject("BitePoint").transform;
            bite.SetParent(pickup.transform, false);
            bite.localPosition = new Vector3(0f, -bounds.extents.y * .45f, 0f);

            var edible = pickup.AddComponent<EdibleObject>();
            // Plant position keeps ids unique across several plants with identical child names.
            edible.ConfigureRuntime(plantId + "-" + mesh.name, EdibleCategory.Other, foodKey, foodName,
                bite, mesh, 1.35f, .06f);
            edible.enabled = false;

            berries.Add(new Berry
            {
                pickup = pickup.transform, mesh = mesh, edible = edible, trigger = trigger,
                hangingSolid = mesh.GetComponent<SphereCollider>(), groundSolid = groundSolid, halfHeight = bounds.extents.y
            });
        }
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

        // Land on the ground between the plant and the pecker, a little to either side, clear of the base.
        Vector3 center = transform.position;
        Vector3 toward = Flat(peckerPosition - center);
        if (toward.sqrMagnitude < 1e-4f) toward = Flat(chosen.pickup.position - center);
        if (toward.sqrMagnitude < 1e-4f) toward = Vector3.forward;
        toward = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * toward.normalized;
        Vector3 landing = center + toward * (dropRadius + Random.Range(0f, .05f));
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
        landing.y = groundY + chosen.halfHeight * .9f;

        chosen.from = chosen.pickup.position;
        chosen.to = landing;
        chosen.meshFrom = chosen.mesh.localRotation;
        chosen.meshTo = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f)) * chosen.meshFrom;
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
            if (!berry.falling) continue;
            berry.t = Mathf.Min(1f, berry.t + dt / fallSeconds);
            float t = berry.t;
            Vector3 position = Vector3.Lerp(berry.from, berry.to, Mathf.SmoothStep(0f, 1f, t));
            // Accelerating drop with a small bounce as it lands.
            position.y = Mathf.Lerp(berry.from.y, berry.to.y, t * t) + Mathf.Sin(Mathf.InverseLerp(.8f, 1f, t) * Mathf.PI) * .025f;
            berry.pickup.position = position;
            berry.mesh.localRotation = Quaternion.Slerp(berry.meshFrom, berry.meshTo, t);
            if (t < 1f) continue;
            berry.falling = false;
            berry.dropped = true;
            berry.trigger.enabled = true;
            if (berry.hangingSolid != null) berry.hangingSolid.enabled = false;
            berry.groundSolid.enabled = true;
            berry.edible.enabled = true;
        }
    }

    private static Vector3 Flat(Vector3 value)
    {
        value.y = 0f;
        return value;
    }
}
