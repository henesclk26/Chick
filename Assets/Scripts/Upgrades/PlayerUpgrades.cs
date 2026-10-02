using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Journal upgrades bought with collected eggs, independently of food statistics.
/// Owns levels and spent eggs; gameplay mechanics only read levels from here.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerUpgrades : MonoBehaviour
{
    public const string FasterPeck = "faster-peck";
    public const string CollectRange = "collect-range";
    public const string SprintSpeed = "sprint-speed";
    public const string SprintDuration = "sprint-duration";
    public const string HelperChick = "helper-chick";
    public const string HelperEatSpeed = "helper-eat-speed";
    public const string HelperRange = "helper-range";
    public const string DoubleJump = "double-jump";
    public const string Glide = "glide";
    public const int MaxHelperChicks = 3;

    [Serializable]
    private sealed class UpgradeDefinition
    {
        public string id;
        [Tooltip("Egg price of each level, in purchase order. Its length is the maximum level.")]
        public int[] levelPrices;
    }

    [SerializeField] private FarmSaveSystem saveSystem;
    [SerializeField] private MainMenuGameplayGate gameplayGate;
    [SerializeField] private UpgradeDefinition[] upgrades = DefaultUpgrades();

    private static UpgradeDefinition[] DefaultUpgrades() => new[]
    {
        new UpgradeDefinition { id = FasterPeck, levelPrices = new[] { 10, 20, 30, 40 } },
        new UpgradeDefinition { id = CollectRange, levelPrices = new[] { 15, 30, 45 } },
        new UpgradeDefinition { id = SprintSpeed, levelPrices = new[] { 15, 30, 45 } },
        new UpgradeDefinition { id = SprintDuration, levelPrices = new[] { 15, 30, 45 } },
        new UpgradeDefinition { id = HelperChick, levelPrices = new[] { 20, 20, 20 } },
        new UpgradeDefinition { id = HelperEatSpeed, levelPrices = new[] { 30 } },
        new UpgradeDefinition { id = HelperRange, levelPrices = new[] { 25 } },
        new UpgradeDefinition { id = DoubleJump, levelPrices = new[] { 200 } },
        new UpgradeDefinition { id = Glide, levelPrices = new[] { 200 } }
    };

    [Header("Daha Hızlı Gagalama")]
    [Tooltip("Extra peck animation speed per level: .125 makes level 4 peck 50% faster.")]
    [SerializeField, Range(0f, .5f)] private float peckSpeedPerLevel = .125f;

    [Header("Toplama Menzili")]
    [Tooltip("Extra eating reach per level: .15 makes level 3 reach 45% farther.")]
    [SerializeField, Range(0f, .5f)] private float collectRangePerLevel = .15f;

    [Header("Sprint Hızı")]
    [Tooltip("Extra running speed per level: .1 makes level 3 run 30% faster.")]
    [SerializeField, Range(0f, .5f)] private float sprintSpeedPerLevel = .1f;

    [Header("Sprint Süresi")]
    [Tooltip("Extra seconds of continuous sprint per level, on top of the player's base sprint time.")]
    [SerializeField, Range(0f, 5f)] private float sprintDurationPerLevel = 2f;

    [Header("Yardımcı Civciv")]
    [Tooltip("How far from the chicken the helper looks for and goes to food, in metres, before upgrades.")]
    [SerializeField, Min(.3f)] private float helperRoamRadius = .5f;
    [Tooltip("\"Yardımcı Menzili\" multiplies the helper's food range by this per level: 2 doubles it.")]
    [SerializeField, Range(1f, 4f)] private float helperRangeMultiplierPerLevel = 2f;
    [Tooltip("Helper peck speed relative to the base peck speed. Future upgrades add to this.")]
    [SerializeField, Range(.1f, 1f)] private float helperEatSpeedFraction = .5f;
    [Tooltip("\"Yardımcı Gagalama\" multiplies the helper's peck speed by this per level: 2 doubles it.")]
    [SerializeField, Range(1f, 4f)] private float helperEatSpeedMultiplierPerLevel = 2f;

    private readonly Dictionary<string, int> levels = new Dictionary<string, int>(StringComparer.Ordinal);
    private int spentEggs;
    private int bonusEggs;

    public int WhiteEggsLaid { get; private set; }
    public int GoldenEggsLaid { get; private set; }

    /// <summary>Raised when a level or the spendable egg balance changes.</summary>
    public event Action Changed;

    public int AvailableEggs => (int)Math.Max(0L, Math.Min(int.MaxValue,
        (long)bonusEggs + WhiteEggsLaid + 5L * GoldenEggsLaid - spentEggs));
    public int AvailableGoldenEggs => GoldenEggsLaid;
    public float PeckSpeedMultiplier => 1f + peckSpeedPerLevel * GetLevel(FasterPeck);
    public float CollectRangeMultiplier => 1f + collectRangePerLevel * GetLevel(CollectRange);
    public float SprintSpeedMultiplier => 1f + sprintSpeedPerLevel * GetLevel(SprintSpeed);
    public float SprintDurationBonusSeconds => sprintDurationPerLevel * GetLevel(SprintDuration);
    public bool DoubleJumpUnlocked => GetLevel(DoubleJump) > 0;
    public bool GlideUnlocked => GetLevel(Glide) > 0;
    // Each purchased level adds one independent companion.
    public int HelperChickCount => Mathf.Min(GetLevel(HelperChick), MaxHelperChicks);
    public float HelperRangeMultiplier => Mathf.Pow(helperRangeMultiplierPerLevel, GetLevel(HelperRange));
    public float HelperRoamRadius => helperRoamRadius * HelperRangeMultiplier;
    public float HelperEatSpeedFraction =>
        helperEatSpeedFraction * Mathf.Pow(helperEatSpeedMultiplierPerLevel, GetLevel(HelperEatSpeed));

    // Helper upgrades mean nothing until the helper itself is bought.
    public bool IsLocked(string id) => (id == HelperEatSpeed || id == HelperRange) && GetLevel(HelperChick) == 0;

    public int GetLevel(string id) => levels.TryGetValue(id, out int level) ? level : 0;
    public int GetMaxLevel(string id) => Find(id)?.levelPrices?.Length ?? 0;
    public bool IsMaxed(string id) => GetLevel(id) >= GetMaxLevel(id);

    /// <summary>Price of the next level, or -1 when the upgrade is maxed or unknown.</summary>
    public int GetNextPrice(string id)
    {
        UpgradeDefinition definition = Find(id);
        int level = GetLevel(id);
        return definition?.levelPrices == null || level >= definition.levelPrices.Length
            ? -1 : Mathf.Max(0, definition.levelPrices[level]);
    }

    // Developer grants enter the wallet without recording fictitious food consumption.
    public bool TryGrantDebugEggs(int amount)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var growth = FindFirstObjectByType<PlayerGrowthController>();
        if (!Application.isPlaying || growth == null ||
            growth.CurrentForm != PlayerGrowthController.Form.Chicken || amount <= 0 ||
            bonusEggs > int.MaxValue - amount || AvailableEggs > int.MaxValue - amount)
            return false;
        bonusEggs += amount;
        Changed?.Invoke();
        return true;
#else
        return false;
#endif
    }

    // Legacy save field names are retained; these counts now increase only on pickup.
    public bool TryCollectEgg(bool golden)
    {
        if (AvailableEggs > int.MaxValue - (golden ? 5 : 1)) return false;
        if (golden)
        {
            if (GoldenEggsLaid == int.MaxValue) return false;
            GoldenEggsLaid++;
        }
        else
        {
            if (WhiteEggsLaid == int.MaxValue) return false;
            WhiteEggsLaid++;
        }
        Changed?.Invoke();
        return true;
    }

    public bool TryPurchase(string id)
    {
        int price = GetNextPrice(id);
        if (price < 0 || AvailableEggs < price || IsLocked(id)) return false;
        spentEggs += price;
        levels[id] = GetLevel(id) + 1;
        Changed?.Invoke();
        return true;
    }

    private void Awake()
    {
        ResolveReferences();
        EnsureDefaultUpgrades();
    }

    // Scenes saved before an upgrade existed keep their tuned prices and gain the new entry.
    private void EnsureDefaultUpgrades()
    {
        var list = (upgrades ?? new UpgradeDefinition[0]).Where(definition => definition != null && definition.id != "auto-eat").ToList();
        foreach (UpgradeDefinition definition in DefaultUpgrades())
            if (list.All(existing => existing.id != definition.id)) list.Add(definition);
        // Migrate scenes/prefabs with the old one-helper definition without losing tuned prices.
        UpgradeDefinition helpers = list.Find(definition => definition.id == HelperChick);
        if (helpers.levelPrices == null || helpers.levelPrices.Length != MaxHelperChicks)
        {
            int[] previous = helpers.levelPrices ?? Array.Empty<int>();
            int price = previous.Length > 0 ? previous[previous.Length - 1] : 20;
            helpers.levelPrices = Enumerable.Range(0, MaxHelperChicks)
                .Select(index => index < previous.Length ? previous[index] : price).ToArray();
        }
        upgrades = list.ToArray();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (saveSystem == null) return;
        saveSystem.Loaded += Restore;
        saveSystem.Saving += Capture;
        if (saveSystem.LastLoadedData != null) Restore(saveSystem.LastLoadedData);
    }

    private void OnDisable()
    {
        if (saveSystem == null) return;
        saveSystem.Loaded -= Restore;
        saveSystem.Saving -= Capture;
    }

    private void Capture(FarmSaveData data)
    {
        // The menu writes a fresh save before gameplay starts; keep that new game at zero.
        if (data == null || (gameplayGate != null && !gameplayGate.IsReleased)) return;
        data.spentEggs = spentEggs;
        data.bonusEggs = bonusEggs;
        data.whiteEggsLaid = WhiteEggsLaid;
        data.goldenEggsLaid = GoldenEggsLaid;
        data.upgradeLevels = levels.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new UpgradeLevelSaveEntry { id = pair.Key, level = pair.Value }).ToArray();
    }

    private void Restore(FarmSaveData data)
    {
        levels.Clear();
        spentEggs = Mathf.Max(0, data?.spentEggs ?? 0);
        bonusEggs = Mathf.Max(0, data?.bonusEggs ?? 0);
        WhiteEggsLaid = Mathf.Max(0, data?.whiteEggsLaid ?? 0);
        GoldenEggsLaid = Mathf.Max(0, data?.goldenEggsLaid ?? 0);
        // Retired one-time upgrade: refund its purchase when loading an older save.
        // Capture writes the reduced spend and drops the retired level, so it is not refunded again.
        if (data?.upgradeLevels != null &&
            data.upgradeLevels.Any(saved => saved != null && saved.id == "auto-eat" && saved.level > 0))
            spentEggs = Mathf.Max(0, spentEggs - 100);
        // Old purchases could be paid for with food points. Keep the purchased levels,
        // but do not carry that removed currency forward as debt against future pickups.
        spentEggs = (int)Math.Min(spentEggs, (long)bonusEggs + WhiteEggsLaid + 5L * GoldenEggsLaid);
        if (data?.upgradeLevels != null)
            foreach (UpgradeLevelSaveEntry saved in data.upgradeLevels)
            {
                if (saved == null || Find(saved.id) == null) continue;
                int level = Mathf.Clamp(saved.level, 0, GetMaxLevel(saved.id));
                if (level > 0) levels[saved.id] = level;
            }
        Changed?.Invoke();
    }

    private UpgradeDefinition Find(string id)
    {
        if (string.IsNullOrEmpty(id) || upgrades == null) return null;
        foreach (UpgradeDefinition definition in upgrades)
            if (definition != null && definition.id == id) return definition;
        return null;
    }

    private void ResolveReferences()
    {
        if (saveSystem == null) saveSystem = FindFirstObjectByType<FarmSaveSystem>(FindObjectsInactive.Include);
        if (gameplayGate == null) gameplayGate = FindFirstObjectByType<MainMenuGameplayGate>(FindObjectsInactive.Include);
    }
}
