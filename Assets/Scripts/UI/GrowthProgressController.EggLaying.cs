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
    public event Action<bool> OnEggLaid;

    private bool eggCycleSelected, lastEggWasGolden;
    private Texture2D whiteEggIcon, goldEggIcon;
    private PlayerUpgrades eggWallet;
    private VisualElement eggPickupRoot;
    private LaidEggPresentation nearbyEgg;

    private void BindEggPickupUI(VisualElement root)
    {
        eggPickupRoot = root.Q("EggPickupRoot");
        if (eggPickupRoot == null)
        {
            eggPickupRoot = new VisualElement { name = "EggPickupRoot", pickingMode = PickingMode.Ignore };
            var prompt = new VisualElement { name = "EggPickupPrompt", pickingMode = PickingMode.Ignore };
            prompt.AddToClassList("interaction-prompt");
            var key = new VisualElement { pickingMode = PickingMode.Ignore };
            key.AddToClassList("interaction-key-slot");
            var circle = new VisualElement { pickingMode = PickingMode.Ignore };
            circle.AddToClassList("interaction-key-circle");
            key.Add(circle);
            var keyLabel = new Label("Z") { pickingMode = PickingMode.Ignore };
            keyLabel.AddToClassList("interaction-key");
            key.Add(keyLabel);
            prompt.Add(key);
            var text = new Label("TOPLA") { name = "EggPickupText", pickingMode = PickingMode.Ignore };
            text.AddToClassList("interaction-text");
            prompt.Add(text);
            eggPickupRoot.Add(prompt);
            root.Add(eggPickupRoot);
        }
    }

    private bool HandleEggPickup(bool pressed, bool available)
    {
        bool canCollect = available && upgradePlayer != null && !upgradePlayer.EggLayingPose &&
            CurrentUpgradeState != UpgradeState.HoldingUpgrade;
        nearbyEgg = canCollect ? LaidEggPresentation.FindNearest(upgradePlayer.transform, eggPickupRange) : null;
        if (eggPickupRoot != null)
        {
            eggPickupRoot.style.display = nearbyEgg != null ? DisplayStyle.Flex : DisplayStyle.None;
        }
        if (nearbyEgg == null || !pressed || requireZRelease) return false;
        if (eggWallet == null) eggWallet = FindFirstObjectByType<PlayerUpgrades>();
        bool collected = nearbyEgg.TryCollect(upgradePlayer.transform, eggWallet, eggPickupRange);
        requireZRelease = true;
        if (collected)
        {
            nearbyEgg = null;
            if (eggPickupRoot != null) eggPickupRoot.style.display = DisplayStyle.None;
        }
        return true; // One Z press can only collect OR start laying.
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

    private void FeedEggCycle()
    {
        EnsureEggCycle();
        if (EggSeedCount >= EggSeedGoal) return;
        fromProgress = bar.Progress;
        EggSeedCount++;
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

        // The world egg is the reward. Wallet credit happens only on a later pickup.
        bool golden = NextEggIsGolden;
        ChangeUpgradeState(UpgradeState.UpgradeCompleted);
        var egg = LaidEggPresentation.Lay(playerGrowth.transform, golden);
        if (egg == null)
        {
            ChangeUpgradeState(UpgradeState.WaitingForUpgrade);
            CancelUpgradeHold(true);
            return;
        }
        lastEggWasGolden = golden;
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
