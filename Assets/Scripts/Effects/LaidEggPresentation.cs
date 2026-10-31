using System;
using System.Collections.Generic;
using UnityEngine;

// Persistent world pickup; no per-egg Update, physics simulation or lifetime timer.
// Picked up with Z, the egg goes with the chicken, out of sight (CarriedEggHUD shows it under the quests), until it is
// dropped into the EggBasket. How many go along at once is the caller's to say ("Otomatik Sepet" levels); with the
// upgrade's last level an egg is collected straight into the basket instead.
public sealed class LaidEggPresentation : MonoBehaviour
{
    private const float TargetHeight = .136f; // 15% smaller; shared by white and golden eggs.
    private static readonly List<LaidEggPresentation> eggs = new();
    private static readonly List<LaidEggPresentation> carriedEggs = new();
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
    /// <summary>The egg the chicken picked up last, if it carries any.</summary>
    public static LaidEggPresentation Carried => carriedEggs.Count > 0 ? carriedEggs[carriedEggs.Count - 1] : null;
    public static int CarriedCount => carriedEggs.Count;
    public static int CountCarried(bool golden)
    {
        int n = 0;
        foreach (var egg in carriedEggs) if (egg != null && egg.golden == golden) n++;
        return n;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() { eggs.Clear(); carriedEggs.Clear(); }
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

    /// <summary>Takes the egg along with the chicken; fails once it carries <paramref name="capacity"/> eggs.</summary>
    public bool TryPickUp(Transform player, float range, int capacity = 1)
    {
        if (carriedEggs.Count >= capacity || collected || carried || !isActiveAndEnabled || !IsReachable(player, range)) return false;
        carried = true;
        carriedEggs.Add(this);
        carrier = player;
        // shortcut: a form switch to chick (developer panel only) hides the egg with the chicken model; drop it there if forms change in play.
        // Lying along the back between the wings, riding the body bone so it follows sitting and walking.
        Transform body = FindBone(player.GetComponent<ChickPlayerController>()?.ActiveAnimator?.transform, "body");
        transform.SetPositionAndRotation((body != null ? body.position : player.position + Vector3.up * .2f) +
            Vector3.up * .13f - player.forward * .07f, Quaternion.LookRotation(Vector3.up, -player.forward));
        transform.SetParent(body != null ? body : player, true);
        // Not shown on the back any more: the HUD shows what is carried. The egg stays active so it is still saved.
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
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

    /// <summary>Hands one carried egg over (to the basket), the earliest picked up: it leaves the world and the save.</summary>
    public static bool TryTakeCarried(out bool golden)
    {
        golden = false;
        carriedEggs.RemoveAll(e => e == null || e.collected);
        if (carriedEggs.Count == 0) return false;
        var egg = carriedEggs[0];
        carriedEggs.RemoveAt(0);
        golden = egg.golden;
        egg.collected = true;
        egg.gameObject.SetActive(false);
        Destroy(egg.gameObject);
        return true;
    }

    /// <summary>"Otomatik Sepet" at its last level: the egg is collected straight into the basket, never carried.</summary>
    public bool TryCollectStraight(Transform player, float range, out bool wasGolden)
    {
        wasGolden = golden;
        if (collected || carried || !isActiveAndEnabled || !IsReachable(player, range)) return false;
        collected = true;
        gameObject.SetActive(false);
        Destroy(gameObject);
        return true;
    }

    private void OnDestroy() => carriedEggs.Remove(this);

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
        carriedEggs.Clear();
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
