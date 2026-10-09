using System;
using System.Collections.Generic;
using UnityEngine;

// Persistent world pickup; no per-egg Update, physics simulation or lifetime timer.
// Picked up with Z, the egg rides on the chicken's back until it is dropped into the EggBasket.
public sealed class LaidEggPresentation : MonoBehaviour
{
    private const float TargetHeight = .136f; // 15% smaller; shared by white and golden eggs.
    private static readonly List<LaidEggPresentation> eggs = new();
    private static readonly RaycastHit[] sightHits = new RaycastHit[16];
    [SerializeField] private string eggId;
    [SerializeField] private bool golden;
    [SerializeField] private Vector3 groundPosition;
    private bool collected;
    private bool carried;
    private Transform carrier;

    public bool IsGolden => golden;
    public int Value => golden ? 5 : 1;
    public Vector3 GroundPosition => groundPosition;
    public static int ActiveEggCount => eggs.Count;
    /// <summary>The egg on the chicken's back, if any. Only one is carried at a time.</summary>
    public static LaidEggPresentation Carried { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() { eggs.Clear(); Carried = null; }
    private void OnEnable() { if (!eggs.Contains(this)) eggs.Add(this); }
    private void OnDisable() => eggs.Remove(this);

    public static LaidEggPresentation Lay(Transform chicken, bool golden)
    {
        if (chicken == null) return null;
        // Drop at the same footprint occupied during the sit, not behind the character.
        Vector3 floor = chicken.position;
        var hits = Physics.RaycastAll(floor + Vector3.up * .2f, Vector3.down, .65f,
            ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.transform.IsChildOf(chicken) || hit.normal.y < .5f) continue;
            floor.y = hit.point.y;
            break;
        }
        return Create(floor, chicken.eulerAngles.y, golden, Guid.NewGuid().ToString("N"));
    }

    private static LaidEggPresentation Create(Vector3 floor, float yaw, bool golden, string id)
    {
        var egg = CreateModel(floor, yaw, golden);
        if (egg == null) return null;
        egg.name = golden ? "Golden Egg (Z Collect)" : "White Egg (Z Collect)";
        var pickup = egg.AddComponent<LaidEggPresentation>();
        pickup.eggId = id;
        pickup.golden = golden;
        pickup.groundPosition = floor;
        return pickup;
    }

    /// <summary>The egg model alone (no pickup), standing on <paramref name="floor"/>; also used for eggs in the basket.</summary>
    public static GameObject CreateModel(Vector3 floor, float yaw, bool golden)
    {
        // Prebuilt ovoid based on the Animals_3D topology; no runtime subdivision.
        var prefab = Resources.Load<GameObject>("EggLaying/SoftEgg");
        if (prefab == null) prefab = Resources.Load<GameObject>("Prefabs/egg");
        if (prefab == null) { Debug.LogError("Missing Animals_3D egg prefab."); return null; }
        var egg = Instantiate(prefab, floor, Quaternion.Euler(0f, yaw, 0f));
        foreach (var animator in egg.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        foreach (var collider in egg.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        var renderers = egg.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) { Destroy(egg); return null; }
        Bounds bounds = CombinedBounds(renderers);
        if (bounds.size.y < .0001f) { Destroy(egg); return null; }
        egg.transform.localScale *= TargetHeight / bounds.size.y;
        bounds = CombinedBounds(renderers);
        egg.transform.position += Vector3.up * (floor.y - bounds.min.y + .002f);
        if (golden)
        {
            var material = Resources.Load<Material>("EggLaying/GoldenEgg");
            if (material == null) { Destroy(egg); Debug.LogError("Missing golden egg material."); return null; }
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }
        }
        return egg;
    }

    private static Bounds CombinedBounds(Renderer[] renderers)
    {
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    public static LaidEggPresentation FindNearest(Transform player, float range)
    {
        if (player == null) return null;
        LaidEggPresentation nearest = null;
        float best = range * range;
        foreach (var egg in eggs)
        {
            if (egg == null || egg.collected || egg.carried || !egg.isActiveAndEnabled) continue;
            float distance = (egg.groundPosition - player.position).sqrMagnitude;
            if (distance > best || !egg.IsReachable(player, range)) continue;
            nearest = egg;
            best = distance;
        }
        return nearest;
    }

    private bool IsReachable(Transform player, float range)
    {
        if (player == null || (groundPosition - player.position).sqrMagnitude > range * range ||
            Mathf.Abs(groundPosition.y - player.position.y) > .35f) return false;
        Vector3 start = player.position + Vector3.up * .15f;
        Vector3 delta = groundPosition + Vector3.up * .1f - start;
        if (delta.sqrMagnitude < .0001f) return true;
        int count = Physics.RaycastNonAlloc(start, delta.normalized, sightHits, delta.magnitude,
            ~0, QueryTriggerInteraction.Ignore);
        if (count == sightHits.Length) return false;
        for (int i = 0; i < count; i++)
            if (!sightHits[i].transform.IsChildOf(player) && !sightHits[i].transform.IsChildOf(transform) &&
                sightHits[i].collider.GetComponentInParent<HelperChickController>() == null)
                return false;
        return true;
    }

    /// <summary>Lifts the egg onto the chicken's back; fails while another egg is carried.</summary>
    public bool TryPickUp(Transform player, float range)
    {
        if (Carried != null || collected || carried || !isActiveAndEnabled || !IsReachable(player, range)) return false;
        carried = true;
        Carried = this;
        carrier = player;
        // shortcut: a form switch to chick (developer panel only) hides the egg with the chicken model; drop it there if forms change in play.
        // Lying along the back between the wings, riding the body bone so it follows sitting and walking.
        Transform body = FindBone(player.GetComponent<ChickPlayerController>()?.ActiveAnimator?.transform, "body");
        transform.SetPositionAndRotation((body != null ? body.position : player.position + Vector3.up * .2f) +
            Vector3.up * .13f - player.forward * .07f, Quaternion.LookRotation(Vector3.up, -player.forward));
        transform.SetParent(body != null ? body : player, true);
        return true;
    }

    private static Transform FindBone(Transform root, string name)
    {
        if (root == null || root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = FindBone(child, name);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>Hands the carried egg over (to the basket): it leaves the world and the save.</summary>
    public static bool TryTakeCarried(out bool golden)
    {
        golden = false;
        var egg = Carried;
        if (egg == null || egg.collected) return false;
        golden = egg.golden;
        egg.collected = true;
        Carried = null;
        egg.gameObject.SetActive(false);
        Destroy(egg.gameObject);
        return true;
    }

    private void OnDestroy()
    {
        if (Carried == this) Carried = null;
    }

    public static GroundEggSaveEntry[] CaptureAll()
    {
        var result = new List<GroundEggSaveEntry>();
        foreach (var egg in eggs)
            if (egg != null && !egg.collected && egg.isActiveAndEnabled)
                // A carried egg is saved on the ground where the chicken stands.
                result.Add(new GroundEggSaveEntry { id = egg.eggId, golden = egg.golden,
                    position = egg.carried ? egg.carrier.position : egg.groundPosition, yaw = egg.transform.eulerAngles.y });
        return result.ToArray();
    }

    public static void RestoreAll(GroundEggSaveEntry[] saved)
    {
        foreach (var egg in eggs.ToArray())
            if (egg != null) { egg.gameObject.SetActive(false); Destroy(egg.gameObject); }
        eggs.Clear();
        Carried = null;
        if (saved == null) return;
        var ids = new HashSet<string>();
        foreach (var entry in saved)
        {
            if (entry == null || !Finite(entry.position.x) || !Finite(entry.position.y) ||
                !Finite(entry.position.z) || !Finite(entry.yaw)) continue;
            string id = string.IsNullOrEmpty(entry.id) ? Guid.NewGuid().ToString("N") : entry.id;
            if (ids.Add(id)) Create(entry.position, entry.yaw, entry.golden, id);
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
