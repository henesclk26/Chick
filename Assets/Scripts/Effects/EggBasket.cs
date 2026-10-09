using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The egg basket by the barn. Eggs dropped in (or laid straight into it with the "Otomatik Sepet" upgrade)
/// pay gold into <see cref="PlayerUpgrades"/> and pile up visibly inside; the pile is saved.
/// </summary>
[DisallowMultipleComponent]
public sealed class EggBasket : MonoBehaviour
{
    [SerializeField] private PlayerUpgrades wallet;
    [SerializeField] private FarmSaveSystem saveSystem;
    [Tooltip("How close (m) the chicken must stand to drop an egg in.")]
    [SerializeField, Min(.3f)] private float dropRange = 1.1f;
    [Tooltip("Height of the straw floor inside the basket model, in its local space (props.py BASKET_FLOOR).")]
    [SerializeField] private float floorHeight = .07f;

    // Two layers of eggs lying on their sides: (radius, count) rings, the lower layer first.
    private static readonly (float radius, int count, int layer)[] Rings =
        { (0f, 1, 0), (.12f, 6, 0), (.235f, 10, 0), (.07f, 5, 1), (.18f, 9, 1) };
    private static EggBasket active;
    private readonly List<bool> eggs = new List<bool>();
    private readonly List<GameObject> models = new List<GameObject>();

    public static EggBasket Active => active;
    public int Capacity { get { int n = 0; foreach (var ring in Rings) n += ring.count; return n; } }

    public bool InReach(Vector3 position) =>
        Vector3.Distance(new Vector3(position.x, 0f, position.z), new Vector3(transform.position.x, 0f, transform.position.z)) <= dropRange &&
        Mathf.Abs(position.y - transform.position.y) < .6f;

    /// <summary>Pays for one egg and adds it to the pile (the pile stops growing once the basket is full).</summary>
    public bool Deposit(bool golden)
    {
        if (wallet == null || !wallet.TryCollectEgg(golden)) return false;
        if (eggs.Count < Capacity) { eggs.Add(golden); AddModel(eggs.Count - 1, golden); }
        return true;
    }

    private void Awake()
    {
        if (wallet == null) wallet = FindFirstObjectByType<PlayerUpgrades>(FindObjectsInactive.Include);
        if (saveSystem == null) saveSystem = FindFirstObjectByType<FarmSaveSystem>(FindObjectsInactive.Include);
    }

    private void OnEnable()
    {
        active = this;
        if (saveSystem == null) return;
        saveSystem.Loaded += Restore;
        saveSystem.Saving += Capture;
        if (saveSystem.LastLoadedData != null) Restore(saveSystem.LastLoadedData);
    }

    private void OnDisable()
    {
        if (active == this) active = null;
        if (saveSystem == null) return;
        saveSystem.Loaded -= Restore;
        saveSystem.Saving -= Capture;
    }

    private void Capture(FarmSaveData data)
    {
        if (data != null) data.basketEggs = eggs.ToArray();
    }

    private void Restore(FarmSaveData data)
    {
        foreach (var model in models) if (model != null) Destroy(model);
        models.Clear();
        eggs.Clear();
        if (data?.basketEggs == null) return;
        for (int i = 0; i < data.basketEggs.Length && i < Capacity; i++)
        {
            eggs.Add(data.basketEggs[i]);
            AddModel(i, data.basketEggs[i]);
        }
    }

    private void AddModel(int index, bool golden)
    {
        int start = 0;
        foreach (var ring in Rings)
        {
            if (index < start + ring.count)
            {
                int slot = index - start;
                float angle = (slot + .5f * ring.layer) * Mathf.PI * 2f / ring.count;
                var local = new Vector3(Mathf.Cos(angle) * ring.radius, floorHeight + ring.layer * .085f, Mathf.Sin(angle) * ring.radius);
                Vector3 floor = transform.TransformPoint(local);
                var model = LaidEggPresentation.CreateModel(floor, 0f, golden);
                if (model == null) return;
                model.name = golden ? "Basket Golden Egg" : "Basket Egg";
                // Lying on its side across the ring, centre raised by half its girth.
                model.transform.SetPositionAndRotation(floor + Vector3.up * .05f,
                    transform.rotation * Quaternion.Euler(0f, -angle * Mathf.Rad2Deg + 37f * index, 90f));
                model.transform.SetParent(transform, true);
                models.Add(model);
                return;
            }
            start += ring.count;
        }
    }
}
