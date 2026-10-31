using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class GrowthProgressController
{
    [Header("Chicken egg laying")]
    [SerializeField, Min(1)] private int seedsPerEgg = 50;
    [SerializeField, Range(0f, 1f)] private float goldenEggChance = .5f;
    [SerializeField, Min(.2f)] private float eggPickupRange = .75f;

    public int EggSeedGoal => Mathf.Max(1, seedsPerEgg);
    public int EggSeedCount { get; private set; }
    public float EggProgress => EggSeedCount / (float)EggSeedGoal;
    public bool NextEggIsGolden { get; private set; }
    // A full egg bar pauses all eating (player and helper chicks) until the egg is laid.
    public bool EggReady => !IsChickForm && EggSeedCount >= EggSeedGoal;
    public event Action<bool> OnEggLaid;
    /// <summary>An egg picked up off the ground (or, with the auto basket, collected straight into it).</summary>
    public static event Action EggCollected;
    /// <summary>Eggs put into the basket (how many at once).</summary>
    public static event Action<int> EggsDelivered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEggListeners() { EggCollected = null; EggsDelivered = null; }

    private bool eggCycleSelected, lastEggWasGolden, lastEggToBasket;
    private Texture2D whiteEggIcon, goldEggIcon;
    private PlayerUpgrades eggWallet;
    private VisualElement eggPickupRoot;
    private LaidEggPresentation nearbyEgg;
    private bool atBasket;

    private void BindEggPickupUI(VisualElement root)
    {
        eggPickupRoot = root.Q("EggPickupRoot") ?? InteractionPrompt(root, "EggPickup", "Z", "TOPLA");
    }

    /// <summary>A bottom-centre "[key] text" prompt, hidden until shown; its text label is named <c>{name}Text</c>.</summary>
    private static VisualElement InteractionPrompt(VisualElement root, string name, string keyText, string message)
    {
        var holder = new VisualElement { name = name + "Root", pickingMode = PickingMode.Ignore };
        var prompt = new VisualElement { name = name + "Prompt", pickingMode = PickingMode.Ignore };
        prompt.AddToClassList("interaction-prompt");
        var key = new VisualElement { pickingMode = PickingMode.Ignore };
        key.AddToClassList("interaction-key-slot");
        key.EnableInClassList("wide-key", keyText.Length > 1);
        var circle = new VisualElement { pickingMode = PickingMode.Ignore };
        circle.AddToClassList("interaction-key-circle");
        key.Add(circle);
        var keyLabel = new Label(keyText) { pickingMode = PickingMode.Ignore };
        keyLabel.AddToClassList("interaction-key");
        key.Add(keyLabel);
        prompt.Add(key);
        var text = new Label(message) { name = name + "Text", pickingMode = PickingMode.Ignore };
        text.AddToClassList("interaction-text");
        prompt.Add(text);
        holder.Add(prompt);
        root.Add(holder);
        return holder;
    }

    // Z picks up nearby eggs (as many at once as "Otomatik Sepet" allows: 1, 2, then 3) and, carrying them, drops them
    // all into the basket by the barn for gold. At the upgrade's last level a picked-up egg goes straight to the basket.
    // Full up beside another egg, the prompt says so and Z tells the player (CarriedEggHUD).
    private bool HandleEggPickup(bool pressed, bool available)
    {
        bool canCollect = available && upgradePlayer != null && !upgradePlayer.EggLayingPose &&
            CurrentUpgradeState != UpgradeState.HoldingUpgrade;
        if (eggWallet == null) eggWallet = FindFirstObjectByType<PlayerUpgrades>();
        int capacity = eggWallet != null ? eggWallet.EggCarryCapacity : 1;
        bool straightToBasket = eggWallet != null && eggWallet.AutoBasketUnlocked && EggBasket.Active != null;
        int carrying = LaidEggPresentation.CarriedCount;
        bool full = !straightToBasket && carrying >= capacity;
        EggBasket basket = canCollect && carrying > 0 && EggBasket.Active != null &&
            EggBasket.Active.InReach(upgradePlayer.transform.position) ? EggBasket.Active : null;
        nearbyEgg = canCollect ? LaidEggPresentation.FindNearest(upgradePlayer.transform, eggPickupRange) : null;
        atBasket = basket != null;
        if (eggPickupRoot != null)
        {
            eggPickupRoot.style.display = nearbyEgg != null || basket != null ? DisplayStyle.Flex : DisplayStyle.None;
            eggPickupRoot.Q<Label>("EggPickupText").text = basket != null ? "SEPETE BIRAK" : full ? "ENVANTER DOLU" : "TOPLA";
        }
        if ((nearbyEgg == null && basket == null) || !pressed || requireZRelease) return false;
        requireZRelease = true;
        if (basket != null)
        {
            int delivered = 0;
            while (LaidEggPresentation.TryTakeCarried(out bool golden)) { basket.Deposit(golden); delivered++; }
            if (delivered > 0) EggsDelivered?.Invoke(delivered);
        }
        else if (full) CarriedEggHUD.ShowFull();
        else if (straightToBasket && nearbyEgg.TryCollectStraight(upgradePlayer.transform, eggPickupRange, out bool collectedGolden))
        {
            EggBasket.Active.Deposit(collectedGolden);
            nearbyEgg = null;
            EggCollected?.Invoke();
            EggsDelivered?.Invoke(1);
        }
        else if (nearbyEgg.TryPickUp(upgradePlayer.transform, eggPickupRange, capacity))
        {
            nearbyEgg = null;
            EggCollected?.Invoke();
        }
        return true; // One Z press can only pick up, drop OR start laying.
    }

    private Texture2D GetEggIcon(bool golden)
    {
        if (whiteEggIcon == null) whiteEggIcon = Resources.Load<Texture2D>("Journal/Egg");
        if (goldEggIcon == null) goldEggIcon = Resources.Load<Texture2D>("Journal/GoldEgg");
        return golden ? goldEggIcon : whiteEggIcon;
    }

    // Exclude transparent margins so both existing icons have the same visible height.
    private static Rect EggIconUV(bool golden) => golden
        ? new Rect(.12f, .047f, .76f, .90f)
        : new Rect(.17f, .088f, .66f, .80f);

    private void EnsureEggCycle()
    {
        if (eggCycleSelected) return;
        // Select once, before the player fills the bar. Eating, reopening menus and
        // switching forms never reroll the reward shown on the right-hand icon.
        NextEggIsGolden = UnityEngine.Random.value < Mathf.Clamp01(goldenEggChance);
        eggCycleSelected = true;
    }

    // How far one food fills the egg bar: a seed (and a caterpillar) 1, an ant, a vegetable, a berry or grape 2, a fruit 3; a
    // mushroom 2 for every bite (EdibleMushroom.Bitten counts the bites before the last). Lettuce only 1: it is many
    // leaves, each eaten on its own.
    private static int EggPoints(EdibleObject food)
    {
        if (food.Category == EdibleCategory.Seed) return 1;
        switch (food.StatisticKey)
        {
            case "apple": case "cherry": case "orange": case "pear": case "watermelon_flesh":
                return 3;
            case "strawberry": case "blackberry": case "grape":
            case "tomato_fruit": case "pepper": case EdibleMushroom.StatisticKey: case "ant":
                return 2;
            default:
                return 1;   // lettuce, caterpillars, anything else
        }
    }

    private void MushroomBitten(EdibleMushroom mushroom)
    {
        SyncUpgradeWithForm();
        if (!IsChickForm) FeedEggCycle(EggPoints(mushroom.Edible));
        else GrowChick(GrowthPoints(mushroom.Edible));
    }

    private void FeedEggCycle(int points = 1)
    {
        EnsureEggCycle();
        if (EggSeedCount >= EggSeedGoal) return;
        fromProgress = bar.Progress;
        EggSeedCount = Mathf.Min(EggSeedCount + Mathf.Max(1, points), EggSeedGoal);
        changeTime = Time.unscaledTime;
        glowUntil = Time.unscaledTime + GlowHoldSeconds;
        if (EggSeedCount == EggSeedGoal) BeginGrowthReadyMessage();
    }

    private void ClaimEgg()
    {
        if (IsChickForm || !eggCycleSelected || EggSeedCount < EggSeedGoal ||
            CurrentUpgradeState != UpgradeState.HoldingUpgrade) return;
        if (upgradePlayer == null || !upgradePlayer.EggLayingPose)
        { CancelUpgradeHold(true); return; }

        // The world egg is the reward; gold is paid once it reaches the basket. "Otomatik Sepet" sends it there at once.
        bool golden = NextEggIsGolden;
        ChangeUpgradeState(UpgradeState.UpgradeCompleted);
        if (eggWallet == null) eggWallet = FindFirstObjectByType<PlayerUpgrades>();
        lastEggToBasket = eggWallet != null && eggWallet.AutoBasketUnlocked && EggBasket.Active != null &&
            EggBasket.Active.Deposit(golden);
        bool laid = lastEggToBasket || LaidEggPresentation.Lay(playerGrowth.transform, golden) != null;
        if (!laid)
        {
            ChangeUpgradeState(UpgradeState.WaitingForUpgrade);
            CancelUpgradeHold(true);
            return;
        }
        lastEggWasGolden = golden;
        // laid straight into the basket: collected and delivered at once
        if (lastEggToBasket) { EggCollected?.Invoke(); EggsDelivered?.Invoke(1); }
        CancelUpgradeHold(true);
        EggSeedCount = 0;
        fromProgress = 0f;
        bar.Progress = 0f;
        changeTime = Time.unscaledTime;
        glowUntil = float.NegativeInfinity;
        eggCycleSelected = false;
        EnsureEggCycle();
        UpdateRightUpgradeIcon();
        ChangeUpgradeState(UpgradeState.EggLaidMessage);
        OnEggLaid?.Invoke(golden);
        PaintUpgradeUI();
    }

    private void RestoreEggCycle(FarmSaveData data)
    {
        LaidEggPresentation.RestoreAll(data?.groundEggs);
        bool saved = data != null && data.eggLayingVersion >= 1;
        EggSeedCount = saved ? Mathf.Clamp(data.eggCycleSeedCount, 0, EggSeedGoal) : 0;
        eggCycleSelected = saved && data.eggCycleSelected;
        NextEggIsGolden = eggCycleSelected && data.nextEggGolden;
        lastEggWasGolden = false;
    }

    private void CaptureEggCycle(FarmSaveData data)
    {
        // Save the selected outcome too, so loading cannot reroll a pending egg.
        data.eggLayingVersion = 1;
        data.eggCycleSeedCount = EggSeedCount;
        data.eggCycleSelected = eggCycleSelected;
        data.nextEggGolden = NextEggIsGolden;
        data.groundEggs = LaidEggPresentation.CaptureAll();
    }
}
