using System;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument)), DisallowMultipleComponent, DefaultExecutionOrder(100)]
public sealed partial class GrowthProgressController : MonoBehaviour
{
    // About three quarters of the coop's seeds: the chick has to roam most of the coop, and what it leaves there still
    // feeds the chicken's first egg (the barn only opens with that egg).
    public const int SeedGoal = 800;
    // The chick's passive stages: stage n is reached at StageThresholds[n - 1] points, the last at the goal. The gaps
    // widen as the chick speeds up, so each stage takes about as long as the one before.
    public static readonly int[] StageThresholds = { 100, 220, 360, 540, SeedGoal };
    public static int StageCount => StageThresholds.Length;
    public static int StageAt(int points)
    {
        int stage = 0;
        while (stage < StageThresholds.Length && points >= StageThresholds[stage]) stage++;
        return stage;
    }
    public int EatenSeedCount { get; private set; }
    public int ChickStage => StageAt(EatenSeedCount);
    // Chick-only passive stages; the chicken always uses 1x.
    public float PeckSpeedMultiplier => !IsChickForm ? 1f : ChickStage >= 5 ? 1.5f : ChickStage >= 1 ? 1f : .5f;
    public float StaminaMultiplier => !IsChickForm ? 1f : ChickStage >= 2 ? 1f : .5f;
    public float WalkSpeedMultiplier => !IsChickForm ? 1f : ChickStage >= 4 ? 1.5f : .75f;
    public float ReachMultiplier => !IsChickForm ? 1f : ChickStage >= 3 ? 1f : .5f;
    public float Progress => EatenSeedCount / (float)SeedGoal;
    public float DisplayProgress => IsChickForm ? Progress : EggProgress;
    public event Action OnGrowthProgressComplete;

    [SerializeField] private FarmSaveSystem saves;
    [SerializeField] private MainMenuGameplayGate gameplayGate;
    [SerializeField] private GameTimeManager timeManager;
    [SerializeField] private Texture2D chickIcon;
    // Original PNG pixels are kept intact. Rects omit only transparent margins.
    [SerializeField] private Rect chickIconUV = new(347f / 1254f, 186f / 1254f, 614f / 1254f, 813f / 1254f);
    [SerializeField, Range(.2f, .35f)] private float fillDuration = .26f;

    private VisualElement hud;
    private VisualElement glow;
    private const float GlowHoldSeconds = 1.2f;
    private float glowUntil, glowStrength, lastRefreshTime;
    private GrowthProgressBarElement bar;
    private Sprite leftSprite, rightSprite;
    private GrowthIconGlow leftIconGlow, rightIconGlow;
    private IVisualElementScheduledItem tick;
    private float fromProgress, changeTime;
    private bool initialized;

    private void OnEnable()
    {
        if (saves == null) saves = FindFirstObjectByType<FarmSaveSystem>();
        if (gameplayGate == null) gameplayGate = FindFirstObjectByType<MainMenuGameplayGate>();
        if (timeManager == null) timeManager = FindFirstObjectByType<GameTimeManager>();
        var root = GetComponent<UIDocument>().rootVisualElement;
        root.pickingMode = PickingMode.Ignore;
        hud = root.Q("GrowthProgressHUD");
        glow = root.Q("GrowthGlow");
        glowUntil = float.NegativeInfinity;
        glowStrength = 0f;
        lastRefreshTime = Time.unscaledTime;
        bar = root.Q<GrowthProgressBarElement>("GrowthBarContainer");
        if (hud == null || bar == null)
        {
            Debug.LogError("Growth HUD needs GrowthProgressHUD.uxml on its UIDocument.", this);
            return;
        }
        BindUpgradeUI(root);
        if (chickIcon != null)
        {
            leftSprite = IconSprite(chickIcon, chickIconUV);
            root.Q<Image>("LeftIcon").sprite = leftSprite;
            leftIconGlow = new GrowthIconGlow(root.Q<Image>("LeftIcon"));
        }
        EdibleObject.Consumed += FoodConsumed;
        EdibleMushroom.Bitten += MushroomBitten;
        if (saves != null)
        {
            saves.Loaded += Restore;
            saves.Saving += Capture;
            if (!initialized) Restore(saves.LastLoadedData);
            else { SetCount(EatenSeedCount, true); RestoreUpgradePresentation(); }
        }
        else { SetCount(EatenSeedCount, true); RestoreUpgradePresentation(); }
        initialized = true;
        RefreshUI();
        tick = hud.schedule.Execute(RefreshUI).Every(16);
    }

    private void OnDisable()
    {
        EdibleObject.Consumed -= FoodConsumed;
        EdibleMushroom.Bitten -= MushroomBitten;
        if (saves != null) { saves.Loaded -= Restore; saves.Saving -= Capture; }
        tick?.Pause();
        CancelUpgradeHold(true);
        if (eggPickupRoot != null) eggPickupRoot.style.display = DisplayStyle.None;
        leftIconGlow?.Dispose();
        rightIconGlow?.Dispose();
        leftIconGlow = rightIconGlow = null;
        if (leftSprite != null) Destroy(leftSprite);
        if (rightSprite != null) Destroy(rightSprite);
    }

    private static Sprite IconSprite(Texture2D texture, Rect uv) => Sprite.Create(texture,
        new Rect(uv.x * texture.width, uv.y * texture.height, uv.width * texture.width, uv.height * texture.height),
        new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);

    private void FoodConsumed(EdibleObject food)
    {
        if (food == null || !food.IsConsumed) return;
        SyncUpgradeWithForm();
        // The chicken's egg bar takes every food; the chick grows on seeds, ants and mushrooms.
        if (!IsChickForm) { FeedEggCycle(EggPoints(food)); return; }
        GrowChick(GrowthPoints(food));
    }

    // How far one food grows the chick: a seed 1, an ant 2, a mushroom 2 for every bite (EdibleMushroom.Bitten counts
    // the bites before the last); anything else does not count.
    private static int GrowthPoints(EdibleObject food)
    {
        if (food.Category == EdibleCategory.Seed) return 1;
        string key = food.StatisticKey;
        return key == "ant" || key == EdibleMushroom.StatisticKey ? 2 : 0;
    }

    private void GrowChick(int points)
    {
        if (points <= 0 || EatenSeedCount >= SeedGoal) return;
        int stageBefore = ChickStage;
        SetCount(Mathf.Min(EatenSeedCount + points, SeedGoal), false);
        // Extend the existing light instead of restarting a flash for every seed.
        glowUntil = Time.unscaledTime + GlowHoldSeconds;
        if (EatenSeedCount == SeedGoal)
        {
            BeginGrowthReadyMessage();
            OnGrowthProgressComplete?.Invoke();
        }
        else if (ChickStage > stageBefore) BeginStageUpMessage();
    }

    private void Restore(FarmSaveData data)
    {
        int restored = data != null ? data.eatenSeedCount : 0;
        if (data != null && data.growthUpgradeVersion >= 1 &&
            !float.IsNaN(data.currentGrowthProgress) && !float.IsInfinity(data.currentGrowthProgress))
        {
            float progress = Mathf.Clamp01(data.currentGrowthProgress);
            // Never round an incomplete save (e.g. 99%) up into a claimable reward.
            restored = progress >= 1f ? SeedGoal : Mathf.Min(SeedGoal - 1, Mathf.RoundToInt(progress * SeedGoal));
        }
        RestoreEggCycle(data);
        SetCount(restored, true);
        RestoreUpgradePresentation();
    }
    private void Capture(FarmSaveData data)
    {
        // The existing menu also writes a fresh save before releasing gameplay.
        // Leave that new game's explicit zero intact even after previewing an old save.
        if (gameplayGate != null && !gameplayGate.IsReleased) return;
        data.eatenSeedCount = EatenSeedCount;
        CaptureUpgradeData(data);
        CaptureEggCycle(data);
    }

    private void SetCount(int value, bool immediate)
    {
        fromProgress = bar.Progress;
        EatenSeedCount = Mathf.Clamp(value, 0, SeedGoal);
        changeTime = Time.unscaledTime;
        if (immediate)
        {
            bar.Progress = DisplayProgress;
            fromProgress = DisplayProgress;
            glowUntil = float.NegativeInfinity;
            glowStrength = 0f;
            bar.EatingHighlight = 0f;
            leftIconGlow?.SetIntensity(0f);
            rightIconGlow?.SetIntensity(0f);
            if (glow != null) glow.style.opacity = 0f;
        }
    }

    private void RefreshUI()
    {
        SyncUpgradeWithForm();
        bool visible = (gameplayGate == null || gameplayGate.IsReleased) &&
            (timeManager == null || ((!timeManager.IsTransitioning || timeManager.IsRevealingGameplay) && !timeManager.IsPaused));
        hud.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        float now = Time.unscaledTime;
        float delta = Mathf.Max(0f, now - lastRefreshTime);
        lastRefreshTime = now;
        if (!visible)
        {
            glowUntil = float.NegativeInfinity;
            glowStrength = 0f;
        }
        float glowTarget = visible && now < glowUntil ? 1f : 0f;
        glowStrength = Mathf.MoveTowards(glowStrength, glowTarget,
            delta / (glowTarget > 0f ? .12f : .55f));
        float highlight = Mathf.SmoothStep(0f, 1f, glowStrength);
        bar.EatingHighlight = highlight;
        leftIconGlow?.SetIntensity(highlight);
        rightIconGlow?.SetIntensity(highlight);
        if (glow != null) glow.style.opacity = highlight;
        float t = Mathf.Clamp01((Time.unscaledTime - changeTime) / fillDuration);
        bar.Progress = Mathf.Lerp(fromProgress, DisplayProgress, t * t * (3f - 2f * t));
    }

    public void RefreshForReveal() => RefreshUI();
}
