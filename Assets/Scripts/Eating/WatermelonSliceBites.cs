using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A watermelon slice whose red flesh is eaten a quarter at a time: every 2–3 pecks remove the next 25%, from
/// the flat cut edge toward the rind (with bite marks), until only the rind is left. Seeds inside an eaten
/// quarter are eaten with it. The rind and flesh meshes are made by WatermelonSliceBiteMeshBuilder (editor);
/// pecks reach the flesh through two edible proxies, one per face, that share this counter.
/// </summary>
[DisallowMultipleComponent]
public sealed class WatermelonSliceBites : MonoBehaviour
{
    public const int Quarters = 4;
    private const int EdibleLayer = 9;

    [SerializeField] private MeshFilter body;
    [SerializeField] private Mesh rindMesh;
    [Tooltip("Flesh left after 0, 1, 2 and 3 eaten quarters. After the fourth only the rind remains.")]
    [SerializeField] private Mesh[] fleshStages = new Mesh[Quarters];
    [Header("Eaten edge (body mesh plane; must match the built meshes)")]
    [SerializeField] private float[] stageBaseRadius = new float[Quarters];
    [SerializeField] private int[] stageBiteCount = new int[Quarters];
    [SerializeField] private float biteAmplitude;
    [SerializeField] private float thickness;
    [Tooltip("Where the beak meets the flesh, body-local on the front face; mirrored onto the back face.")]
    [SerializeField] private Vector3 bitePoint;
    [Header("Eating")]
    [SerializeField, Min(1)] private int minPecksPerQuarter = 2;
    [SerializeField, Min(1)] private int maxPecksPerQuarter = 3;
    [Tooltip("Seeds inside an eaten quarter count as eaten (statistics and growth); off: they just disappear.")]
    [SerializeField] private bool seedsCountWhenEaten = true;

    private MeshFilter flesh;
    private EdibleObject front, back;
    private readonly List<EdibleObject> seeds = new List<EdibleObject>();
    private int pecks, pecksNeeded;
    private float punch;

    public int EatenQuarters { get; private set; }
    public int PecksThisQuarter => pecks;
    public int PecksNeeded => pecksNeeded;
    public string SaveId { get; private set; }
    public EdibleObject FrontFlesh => front;
    public EdibleObject BackFlesh => back;
    public MeshFilter Flesh => flesh;

    /// <summary>Radius of the eaten edge after <paramref name="eatenQuarters"/> quarters at angle theta (0..π)
    /// in the body mesh plane: a circle plus rounded bite notches. Shared with the mesh builder.</summary>
    public static float InnerRadius(float baseRadius, int biteCount, float amplitude, float theta)
    {
        if (baseRadius <= 0f) return 0f;
        if (biteCount <= 0) return baseRadius;
        float u = Mathf.Repeat(biteCount * theta / Mathf.PI, 1f);
        return baseRadius + amplitude * Mathf.Sqrt(Mathf.Max(0f, 1f - (2f * u - 1f) * (2f * u - 1f)));
    }

    private void Awake() => Build();

    private void Build()
    {
        if (flesh != null) return;
        if (body == null || rindMesh == null || fleshStages == null || fleshStages.Length < Quarters)
        {
            Debug.LogWarning($"{name}: watermelon bite stages are not built; the slice stays whole.", this);
            enabled = false;
            return;
        }
        var bodyRenderer = body.GetComponent<MeshRenderer>();
        body.sharedMesh = rindMesh;
        var fleshObject = new GameObject("Flesh");
        fleshObject.transform.SetParent(body.transform, false);
        flesh = fleshObject.AddComponent<MeshFilter>();
        var fleshRenderer = fleshObject.AddComponent<MeshRenderer>();
        fleshRenderer.sharedMaterials = bodyRenderer.sharedMaterials;
        fleshRenderer.shadowCastingMode = bodyRenderer.shadowCastingMode;
        fleshRenderer.receiveShadows = bodyRenderer.receiveShadows;

        var root = GetComponent<EdibleObject>();
        string baseId = root != null ? root.PersistentId : name + "/";
        SaveId = baseId + "flesh";
        front = BuildProxy("Flesh Bite Front", bitePoint, Vector3.down, baseId + "flesh-front");
        back = BuildProxy("Flesh Bite Back", new Vector3(bitePoint.x, thickness - bitePoint.y, bitePoint.z), Vector3.up, baseId + "flesh-back");
        foreach (EdibleObject edible in GetComponentsInChildren<EdibleObject>(true))
            if (edible != root && edible != front && edible != back) seeds.Add(edible);
        pecksNeeded = RollPecks();
        Apply();
    }

    // Invisible edible stand-in on one face of the flesh: the eaters target it like any food, and each peck
    // that lands on it is one bite of the shared flesh.
    private EdibleObject BuildProxy(string proxyName, Vector3 localPoint, Vector3 localNormal, string id)
    {
        var proxy = new GameObject(proxyName) { layer = EdibleLayer };
        proxy.transform.SetParent(body.transform, false);
        proxy.transform.localPosition = localPoint;
        var trigger = proxy.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(.06f, .004f, .03f);
        var bite = new GameObject("BitePoint").transform;
        bite.SetParent(proxy.transform, false);
        // The contact assist nudges this toward the beak; it has no visual, so the flesh itself never moves.
        var contact = new GameObject("ContactProxy").transform;
        contact.SetParent(proxy.transform, false);
        var edible = proxy.AddComponent<EdibleObject>();
        edible.ConfigureRuntime(id, EdibleCategory.Other, "watermelon_flesh", "Karpuz", bite, contact, 1.35f, .1f);
        edible.ConfigureExposedFace(localNormal);
        edible.BiteHandler = () => Bite(edible);
        return edible;
    }

    private int RollPecks() => UnityEngine.Random.Range(minPecksPerQuarter, Mathf.Max(minPecksPerQuarter, maxPecksPerQuarter) + 1);

    // One peck landed on the flesh through `proxy` (either face). True when it finished the last quarter.
    private bool Bite(EdibleObject proxy)
    {
        if (EatenQuarters >= Quarters) return true;
        punch = 1f;
        if (++pecks < pecksNeeded) return false;
        pecks = 0;
        pecksNeeded = RollPecks();
        EatenQuarters++;
        EatSeeds(seedsCountWhenEaten);
        Apply();
        if (EatenQuarters < Quarters) return false;
        // The pecked proxy is consumed by the caller (counted once); retire its twin quietly.
        (proxy == front ? back : front).MarkConsumed();
        return true;
    }

    private void EatSeeds(bool count)
    {
        foreach (EdibleObject seed in seeds)
        {
            if (seed == null || seed.IsConsumed || !IsEaten(seed.transform.position)) continue;
            if (count && seed.CanBeEaten()) seed.Consume();
            else seed.MarkConsumed();
        }
    }

    private bool IsEaten(Vector3 worldPosition)
    {
        if (EatenQuarters >= Quarters) return true;
        if (EatenQuarters <= 0) return false;
        Vector3 local = body.transform.InverseTransformPoint(worldPosition);
        float theta = Mathf.Clamp(Mathf.Atan2(local.z, local.x), 0f, Mathf.PI);
        float radius = new Vector2(local.x, local.z).magnitude;
        return radius < InnerRadius(stageBaseRadius[EatenQuarters], stageBiteCount[EatenQuarters], biteAmplitude, theta) + .004f;
    }

    private void Apply()
    {
        bool left = EatenQuarters < Quarters;
        flesh.gameObject.SetActive(left);
        if (left) flesh.sharedMesh = fleshStages[EatenQuarters];
    }

    private void Update()
    {
        if (punch <= 0f || flesh == null) return;
        // A small squash on every landed peck, so pecks that do not finish a quarter still read.
        punch = Mathf.Max(0f, punch - Time.deltaTime / .16f);
        float squash = 1f - .035f * Mathf.Sin(punch * Mathf.PI);
        flesh.transform.localScale = new Vector3(squash, 1f, squash);
    }

    public static FoodBiteSaveEntry[] CaptureAll()
    {
        var entries = new List<FoodBiteSaveEntry>();
        foreach (var slice in FindObjectsByType<WatermelonSliceBites>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (slice.flesh == null || (slice.EatenQuarters == 0 && slice.pecks == 0)) continue;
            entries.Add(new FoodBiteSaveEntry
            {
                id = slice.SaveId, stage = slice.EatenQuarters, bites = slice.pecks, bitesNeeded = slice.pecksNeeded
            });
        }
        entries.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        return entries.ToArray();
    }

    public static void RestoreAll(FoodBiteSaveEntry[] entries)
    {
        if (entries == null || entries.Length == 0) return;
        var byId = new Dictionary<string, FoodBiteSaveEntry>(StringComparer.Ordinal);
        foreach (var entry in entries) if (entry != null && !string.IsNullOrEmpty(entry.id)) byId[entry.id] = entry;
        foreach (var slice in FindObjectsByType<WatermelonSliceBites>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            slice.Build();
            if (slice.flesh != null && byId.TryGetValue(slice.SaveId, out var entry)) slice.Restore(entry);
        }
    }

    private void Restore(FoodBiteSaveEntry entry)
    {
        EatenQuarters = Mathf.Clamp(entry.stage, 0, Quarters);
        pecksNeeded = Mathf.Clamp(entry.bitesNeeded, minPecksPerQuarter, Mathf.Max(minPecksPerQuarter, maxPecksPerQuarter));
        pecks = Mathf.Clamp(entry.bites, 0, pecksNeeded - 1);
        // Counted seeds are already in the consumed ids; this only catches any that were not.
        EatSeeds(false);
        if (EatenQuarters >= Quarters) { front.MarkConsumed(); back.MarkConsumed(); }
        Apply();
    }
}
