using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Makes a mushroom food. Put it on the object that is one mushroom (its meshes may be on children). Everything is
/// worked out when play starts, from how the mushroom stands in the scene at that moment, so a copy that is moved,
/// turned or made bigger simply works: how many bites it takes comes from its size on the farm (one; two from
/// <see cref="TwoBitesFrom"/>; three from <see cref="ThreeBitesFrom"/>; a selected one shows it in the editor), the
/// beak's bite point is at its foot, and its save id comes from where it stands.
/// A mushroom of two or three bites gets smaller with each bite, shrinking towards its foot: three bites take it
/// from big to medium to small, two from big to small, and the last bite eats what is left (EatingFeedback shrinks it
/// away). Each bite gives it a little hop: it springs up, settles at its new size with a small overshoot and squashes
/// softly as it lands. Crumbs fly in <see cref="crumbColour"/>. Helper chicks leave mushrooms to the player.
/// (MushroomStaticBatching keeps mushrooms out of static batching, which would freeze their size.)
/// </summary>
[DisallowMultipleComponent]
public sealed class EdibleMushroom : MonoBehaviour
{
    private const int EdibleLayer = 9;
    // One rule for every mushroom. Size on the farm (m): the cube root of its bounding box volume, or 3/4 of its
    // height if more. From TwoBitesFrom it takes two bites, from ThreeBitesFrom three.
    private const float TwoBitesFrom = .27f, ThreeBitesFrom = .37f;
    public const string StatisticKey = "mushroom";

    /// <summary>A bite that did not finish the mushroom (the last one raises EdibleObject.Consumed).</summary>
    public static event Action<EdibleMushroom> Bitten;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetBiteListeners() => Bitten = null;

    [SerializeField] private Color crumbColour = new Color(.86f, .74f, .6f);

    // Its size after each bite, as a share of its full size, by how many bites it takes: two bites leave it small,
    // three medium and then small.
    private static readonly float[][] SizesAfterBites = { null, new[] { 1f }, new[] { 1f, .6f }, new[] { 1f, .74f, .5f } };
    private const float HopTime = .4f;
    private const float HopStretch = .14f;   // how much taller (and thinner) it springs for a moment
    private const float HopSquash = .1f;     // how much flatter (and wider) it settles for a moment after

    private EdibleObject edible;
    private Transform bitePoint;
    private Vector3 foot, anchor, restPosition, restScale;
    private Coroutine hop;

    public Color CrumbColour => crumbColour;
    public int Bites { get; private set; } = 1;
    public int BitesTaken { get; private set; }
    public EdibleObject Edible => edible;
    private string SaveId => edible.PersistentId + "#bites";

    /// <summary>How many bites the mushroom takes at its present size (also in the editor).</summary>
    public int BitesForCurrentSize()
    {
        if (!WorldBounds(out Bounds b)) return 1;
        // its bulk, or for a tall thin one its height (taller than the chick is a mouthful too)
        float size = Mathf.Max(Mathf.Pow(Mathf.Max(b.size.x*b.size.y*b.size.z, 0f), 1f/3f), b.size.y*.75f);
        return size >= ThreeBitesFrom ? 3 : size >= TwoBitesFrom ? 2 : 1;
    }

    /// <summary>Editor setup: the crumb colour for this model.</summary>
    public void Configure(Color crumbs) => crumbColour = crumbs;

    private void Awake()
    {
        if (!WorldBounds(out Bounds b)) { enabled = false; return; }
        Bites = BitesForCurrentSize();
        foot = new Vector3(b.center.x, b.min.y, b.center.z);
        restPosition = transform.position;
        restScale = transform.localScale;
        // it shrinks towards the ground's surface where it stands (a stem sunk into the ground would otherwise sink
        // further as the mushroom gets smaller)
        anchor = foot;
        var terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            float ground = terrain.SampleHeight(foot) + terrain.transform.position.y;
            if (ground > b.min.y && ground < b.min.y + b.size.y*.4f) anchor.y = ground;
        }
        gameObject.layer = EdibleLayer;

        // The beak's target: at the stem's foot, where the bird pecks on the ground. The contact point the eater
        // draws to the beak has no mesh, so the mushroom itself never moves.
        var bite = new GameObject("Mushroom Bite") { layer = EdibleLayer }.transform;
        bite.SetParent(transform, false);
        bite.position = foot + Vector3.up*.015f;
        var trigger = bite.gameObject.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = .05f/Mathf.Max(MaxScale(bite), .0001f);
        bitePoint = bite;
        var contact = new GameObject("ContactProxy").transform;
        contact.SetParent(bite, false);

        edible = GetComponent<EdibleObject>();
        if (edible == null) edible = gameObject.AddComponent<EdibleObject>();
        var p = transform.position;
        string id = string.Format(CultureInfo.InvariantCulture, "{0}/mushroom/{1}@{2:F3},{3:F3},{4:F3}", gameObject.scene.path, name, p.x, p.y, p.z);
        edible.ConfigureRuntime(id, EdibleCategory.Other, StatisticKey, "Mantar", bite, contact, 1.5f, .05f);
        edible.IgnoredByHelpers = true;
        if (Bites > 1) edible.BiteHandler = Bite;
    }

    private bool WorldBounds(out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.GetComponentInParent<EdibleMushroom>(true) != this) continue;
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    private static float MaxScale(Transform t)
    {
        var s = t.lossyScale;
        return Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
    }

    // One peck landed. True when it was the last bite (the caller then eats the mushroom).
    private bool Bite()
    {
        if (hop != null) { StopCoroutine(hop); hop = null; Pose(SizeAfter(BitesTaken), 1f); }
        if (++BitesTaken >= Bites) return true;
        hop = StartCoroutine(Hop(SizeAfter(BitesTaken - 1), SizeAfter(BitesTaken)));
        Bitten?.Invoke(this);
        return false;
    }

    private float SizeAfter(int bites) => SizesAfterBites[Mathf.Clamp(Bites, 1, 3)][Mathf.Clamp(bites, 0, Bites - 1)];

    // a hop on the spot, its foot never leaving the ground: it springs up taller and thinner, shrinks to its new size
    // with a little overshoot, settles a touch flatter and wider, and comes to rest
    private System.Collections.IEnumerator Hop(float from, float to)
    {
        for (float time = 0f; time < HopTime; time += Time.deltaTime)
        {
            float t = time/HopTime;
            const float back = 1.6f;   // ease out with overshoot
            float e = t - 1f;
            float size = Mathf.LerpUnclamped(from, to, 1f + e*e*((back + 1f)*e + back));
            float shape = 1f;
            if (t < .35f) shape += HopStretch*Mathf.Sin(t/.35f*Mathf.PI);
            else if (t < .85f) shape -= HopSquash*Mathf.Sin((t - .35f)/.5f*Mathf.PI);
            Pose(size, shape);
            yield return null;
        }
        Pose(to, 1f);
        hop = null;
    }

    // the mushroom at this share of its full size, scaled about where it stands on the ground; shape > 1 makes it
    // taller and thinner, < 1 flatter and wider
    private void Pose(float size, float shape)
    {
        float wide = size*(1f + (1f - shape)*.5f), tall = size*shape;
        transform.position = anchor + Vector3.Scale(restPosition - anchor, new Vector3(wide, tall, wide));
        transform.localScale = new Vector3(restScale.x*wide, restScale.y*tall, restScale.z*wide);
        // the beak's bite point keeps its size, so a small mushroom is as easy to peck as a big one
        if (bitePoint != null) bitePoint.localScale = new Vector3(1f/wide, 1f/tall, 1f/wide);
    }

    // ---- save ----

    /// <summary>The other bitten foods' entries followed by the mushrooms' (they share the save's food bites).</summary>
    public static FoodBiteSaveEntry[] AppendTo(FoodBiteSaveEntry[] others)
    {
        var mushrooms = CaptureAll();
        if (mushrooms.Length == 0) return others;
        var all = new FoodBiteSaveEntry[(others?.Length ?? 0) + mushrooms.Length];
        others?.CopyTo(all, 0);
        mushrooms.CopyTo(all, others?.Length ?? 0);
        return all;
    }

    public static FoodBiteSaveEntry[] CaptureAll()
    {
        var entries = new List<FoodBiteSaveEntry>();
        foreach (var m in FindObjectsByType<EdibleMushroom>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (m.edible == null || m.edible.IsConsumed || m.BitesTaken == 0) continue;
            entries.Add(new FoodBiteSaveEntry { id = m.SaveId, stage = m.BitesTaken, bites = 0, bitesNeeded = m.Bites });
        }
        entries.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        return entries.ToArray();
    }

    public static void RestoreAll(FoodBiteSaveEntry[] entries)
    {
        if (entries == null || entries.Length == 0) return;
        var byId = new Dictionary<string, FoodBiteSaveEntry>(StringComparer.Ordinal);
        foreach (var entry in entries) if (entry != null && !string.IsNullOrEmpty(entry.id)) byId[entry.id] = entry;
        foreach (var m in FindObjectsByType<EdibleMushroom>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (m.edible == null || m.edible.IsConsumed || !byId.TryGetValue(m.SaveId, out var entry)) continue;
            // a mushroom now smaller than when it was bitten keeps at least its last bite
            m.BitesTaken = Mathf.Clamp(entry.stage, 0, m.Bites - 1);
            if (m.BitesTaken == 0) continue;
            m.Pose(m.SizeAfter(m.BitesTaken), 1f);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!WorldBounds(out Bounds b)) return;
        int bites = Application.isPlaying ? Bites - BitesTaken : BitesForCurrentSize();
        Gizmos.color = new Color(1f, .75f, .2f, .9f);
        for (int i = 0; i < bites; i++)
            Gizmos.DrawSphere(new Vector3(b.center.x + (i - (bites - 1)*.5f)*.06f, b.max.y + .06f, b.center.z), .02f);
        UnityEditor.Handles.Label(new Vector3(b.center.x, b.max.y + .13f, b.center.z), $"{bites} ısırık");
    }
#endif
}
