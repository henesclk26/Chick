using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public sealed partial class GrowthProgressController
{
    public enum UpgradeState { Progressing, GrowthReadyMessage, WaitingForUpgrade, HoldingUpgrade, UpgradeCompleted, EggLaidMessage, StageUpMessage }

    [Header("Manual growth upgrade")]
    [SerializeField, Min(.1f)] private float upgradeHoldDuration = 1f;
    [SerializeField] private ChickPlayerController upgradePlayer;
    [SerializeField] private Texture2D chickenIcon;
    [SerializeField] private Rect chickenIconUV = new(253f / 1254f, 74f / 1254f, 816f / 1254f, 1067f / 1254f);

    public UpgradeState CurrentUpgradeState { get; private set; }
    public float UpgradeHoldProgress => holdTime / Mathf.Max(.1f, upgradeHoldDuration);

    private PlayerGrowthController playerGrowth;
    private VisualElement notificationRoot, upgradePrompt, stationPromptRoot;
    private Label stationPromptText;
    private Label readyLabel;
    private Label actionLabel;
    private UpgradeHoldIndicator holdIndicator;
    private Image rightIconImage;
    private float stateTime, holdTime, ringResetStart, ringResetTime, promptTime;
    private bool requireZRelease;
    private bool formPresentationBound, presentedAsChicken;
    private const float ReadyMessageDuration = 1.5f;
    private const float RingResetDuration = .2f;

    // Chick growth and chicken laying keep independent progress behind the same HUD.
    private bool IsChickForm => playerGrowth == null || playerGrowth.CurrentForm == PlayerGrowthController.Form.Chick;
    private bool ActiveBarReady => IsChickForm ? EatenSeedCount >= SeedGoal : EggSeedCount >= EggSeedGoal;

    private void BindUpgradeUI(VisualElement root)
    {
        if (upgradePlayer == null) upgradePlayer = FindFirstObjectByType<ChickPlayerController>();
        if (upgradePlayer != null) playerGrowth = upgradePlayer.GetComponent<PlayerGrowthController>();
        notificationRoot = root.Q("UpgradeNotificationRoot");
        readyLabel = root.Q<Label>("GrowthReadyLabel");
        actionLabel = root.Q<Label>("UpgradeText");
        upgradePrompt = root.Q("UpgradePrompt");
        holdIndicator = root.Q<UpgradeHoldIndicator>("UpgradeHoldIndicator");
        rightIconImage = root.Q<Image>("RightIcon");
        stageUpBanner = root.Q("StageUpBanner");
        stageUpNumber = root.Q<Label>("StageUpNumber");
        stageUpTitle = root.Q<Label>("StageUpTitle");
        stageUpDetail = root.Q<Label>("StageUpDetail");
        BindEggPickupUI(root);
        stationPromptRoot = root.Q("StationPromptRoot") ?? InteractionPrompt(root, "StationPrompt", "TAB", "");
        stationPromptText = stationPromptRoot.Q<Label>("StationPromptText");
        stationPromptRoot.Q("StationPromptPrompt").AddToClassList("keycap-out");
    }

    // "[TAB] Yükseltmeler" in the barn, "[TAB] Civciv Yükseltmeleri" at the coop; the egg pickup prompt wins.
    private void PaintStationPrompt(bool available)
    {
        if (stationPromptRoot == null) return;
        UpgradeStation station = available && nearbyEgg == null && !atBasket ? UpgradeStation.At(upgradePlayer.transform.position) : null;
        stationPromptRoot.style.display = station != null ? DisplayStyle.Flex : DisplayStyle.None;
        if (station != null) stationPromptText.text = station.PromptText;
    }

    private void CaptureUpgradeData(FarmSaveData data)
    {
        data.growthUpgradeVersion = 1;
        data.currentGrowthProgress = Progress;
        data.growthUpgradePending = EatenSeedCount >= SeedGoal && IsChickForm;
    }

    private void RestoreUpgradePresentation()
    {
        presentedAsChicken = !IsChickForm;
        formPresentationBound = true;
        if (presentedAsChicken) EnsureEggCycle();
        CancelUpgradeHold(true);
        ChangeUpgradeState(ActiveBarReady
            ? UpgradeState.WaitingForUpgrade : UpgradeState.Progressing);
        hud.EnableInClassList("egg-laying", presentedAsChicken);
        bar.Segments = presentedAsChicken ? 1 : StageCount;
        fromProgress = DisplayProgress;
        bar.Progress = DisplayProgress;
        changeTime = Time.unscaledTime;
        glowUntil = float.NegativeInfinity;
        UpdateRightUpgradeIcon();
        PaintUpgradeUI();
    }

    private void UpdateRightUpgradeIcon()
    {
        if (rightIconImage == null) return;
        rightIconGlow?.Dispose();
        rightIconGlow = null;
        if (rightSprite != null) Destroy(rightSprite);
        Texture2D texture = IsChickForm ? chickenIcon : GetEggIcon(NextEggIsGolden);
        if (texture == null) { rightIconImage.sprite = null; return; }
        rightSprite = IconSprite(texture, IsChickForm ? chickenIconUV : EggIconUV(NextEggIsGolden));
        rightIconImage.sprite = rightSprite;
        rightIconImage.tooltip = IsChickForm ? "Tavuk" : NextEggIsGolden ? "Sıradaki: altın yumurta" : "Sıradaki: beyaz yumurta";
        // Egg art is reused directly; avoid rebuilding an expensive silhouette mask every cycle.
        if (IsChickForm) rightIconGlow = new GrowthIconGlow(rightIconImage);
    }

    private static readonly string[] StageUpTitles = { "", "YEME HIZI ARTTI!", "STAMİNA ARTTI!", "YEME MENZİLİ ARTTI!", "YÜRÜME HIZI ARTTI!" };
    private static readonly string[] StageUpDetails = { "", "Gagalama hızı 0.5x → 1x", "Koşma süresi 0.5x → 1x",
        "Yeme menzili 0.5x → 1x", "Yürüme hızı 0.75x → 1.5x" };
    private VisualElement stageUpBanner;
    private Label stageUpNumber, stageUpTitle, stageUpDetail;

    private void BeginStageUpMessage()
    {
        if (CurrentUpgradeState != UpgradeState.Progressing && CurrentUpgradeState != UpgradeState.StageUpMessage) return;
        ChangeUpgradeState(UpgradeState.StageUpMessage);
    }

    private void BeginGrowthReadyMessage()
    {
        if (CurrentUpgradeState != UpgradeState.Progressing && CurrentUpgradeState != UpgradeState.EggLaidMessage &&
            CurrentUpgradeState != UpgradeState.StageUpMessage) return;
        CancelUpgradeHold(true);
        ChangeUpgradeState(UpgradeState.GrowthReadyMessage);
    }

    private void Update()
    {
        if (!initialized) return;
        var key = Keyboard.current?.zKey;
        bool available = Application.isFocused && Time.timeScale > 0f &&
            (gameplayGate == null || gameplayGate.IsReleased) &&
            (timeManager == null || (!timeManager.IsPaused && !timeManager.IsTransitioning)) &&
            !GameplayJournalController.IsAnyOpen &&
            (playerGrowth == null || !playerGrowth.IsTransforming) &&
            upgradePlayer != null && upgradePlayer.isActiveAndEnabled;
        bool pressed = key != null && key.wasPressedThisFrame;
        bool held = key != null && key.isPressed;
        if (!held) requireZRelease = false;
        bool handled = HandleEggPickup(pressed, available);
        TickUpgrade(Time.unscaledDeltaTime, pressed && !handled, held, available);
        PaintUpgradeUI();
        PaintStationPrompt(available);
    }

    private void OnApplicationFocus(bool focus)
    {
        if (!focus) CancelUpgradeHold(true);
    }

    // Keeps the prompt consistent with form changes from save loading or the developer panel.
    private void SyncUpgradeWithForm()
    {
        if (!formPresentationBound || presentedAsChicken != !IsChickForm)
        {
            RestoreUpgradePresentation();
        }
        else if (CurrentUpgradeState == UpgradeState.Progressing && ActiveBarReady)
            ChangeUpgradeState(UpgradeState.WaitingForUpgrade);
    }

    private void TickUpgrade(float delta, bool pressed, bool held, bool available)
    {
        SyncUpgradeWithForm();
        if (!available)
        {
            if (eggPickupRoot != null) eggPickupRoot.style.display = DisplayStyle.None;
            CancelUpgradeHold(true);
            return;
        }
        if (!held) requireZRelease = false;
        delta = Mathf.Max(0f, delta);
        stateTime += delta;
        promptTime += delta;
        switch (CurrentUpgradeState)
        {
            case UpgradeState.GrowthReadyMessage:
                if (stateTime >= ReadyMessageDuration) ChangeUpgradeState(UpgradeState.WaitingForUpgrade);
                break;
            case UpgradeState.WaitingForUpgrade:
                ringResetTime += delta;
                if (holdIndicator != null)
                    holdIndicator.Progress = Mathf.Lerp(ringResetStart, 0f,
                        Mathf.SmoothStep(0f, 1f, ringResetTime / RingResetDuration));
                if (pressed && held && !requireZRelease)
                {
                    if (!IsChickForm && (upgradePlayer == null || !upgradePlayer.TryBeginEggLaying())) break;
                    holdTime = 0f;
                    ChangeUpgradeState(UpgradeState.HoldingUpgrade);
                    AdvanceUpgradeHold(delta);
                }
                break;
            case UpgradeState.HoldingUpgrade:
                if (!held || (!IsChickForm && (upgradePlayer == null || !upgradePlayer.EggLayingPose))) CancelUpgradeHold(false);
                else AdvanceUpgradeHold(delta);
                break;
            case UpgradeState.UpgradeCompleted:
                // Growth waits for a safe moment; form sync then selects the egg cycle.
                if (playerGrowth == null || !playerGrowth.GrowthPending) ChangeUpgradeState(UpgradeState.Progressing);
                break;
            case UpgradeState.StageUpMessage:
                if (stateTime >= ReadyMessageDuration) ChangeUpgradeState(UpgradeState.Progressing);
                break;
            case UpgradeState.EggLaidMessage:
                if (stateTime >= ReadyMessageDuration)
                    ChangeUpgradeState(ActiveBarReady ? UpgradeState.WaitingForUpgrade : UpgradeState.Progressing);
                break;
        }
    }

    private void AdvanceUpgradeHold(float delta)
    {
        holdTime = Mathf.Min(Mathf.Max(.1f, upgradeHoldDuration), holdTime + delta);
        if (holdIndicator != null) holdIndicator.Progress = UpgradeHoldProgress;
        if (UpgradeHoldProgress >= 1f) ClaimUpgrade();
    }

    private void ClaimUpgrade()
    {
        if (CurrentUpgradeState != UpgradeState.HoldingUpgrade) return;
        if (!IsChickForm) { ClaimEgg(); return; }
        if (EatenSeedCount < SeedGoal || playerGrowth == null) return;
        // Move state first, making continued holding and duplicate calls harmless.
        ChangeUpgradeState(UpgradeState.UpgradeCompleted);
        CancelUpgradeHold(true);
        playerGrowth.RequestChickenGrowth();
        PaintUpgradeUI();
    }

    private void CancelUpgradeHold(bool immediate)
    {
        if (upgradePlayer != null) upgradePlayer.EndEggLaying();
        ringResetStart = holdIndicator != null ? holdIndicator.Progress : UpgradeHoldProgress;
        ringResetTime = 0f;
        holdTime = 0f;
        if (immediate)
        {
            requireZRelease = true;
            ringResetStart = 0f;
            if (holdIndicator != null) holdIndicator.Progress = 0f;
        }
        if (CurrentUpgradeState == UpgradeState.HoldingUpgrade)
            ChangeUpgradeState(UpgradeState.WaitingForUpgrade);
    }

    private void ChangeUpgradeState(UpgradeState next)
    {
        CurrentUpgradeState = next;
        stateTime = 0f;
    }

    private void PaintUpgradeUI()
    {
        if (notificationRoot == null || readyLabel == null || upgradePrompt == null) return;
        bool laid = CurrentUpgradeState == UpgradeState.EggLaidMessage;
        bool stageUp = CurrentUpgradeState == UpgradeState.StageUpMessage;
        bool ready = CurrentUpgradeState == UpgradeState.GrowthReadyMessage || laid || stageUp;
        readyLabel.text = laid ? (lastEggWasGolden ? "ALTIN YUMURTA " : "YUMURTA ") + (lastEggToBasket ? "SEPETE EKLENDİ" : "BIRAKILDI")
            : IsChickForm ? "GELİŞİM HAZIR!" : "YUMURTLAMA HAZIR!";
        if (actionLabel != null) actionLabel.text = IsChickForm ? "GELİŞTİR" : "YUMURTLA";
        bool prompt = CurrentUpgradeState == UpgradeState.WaitingForUpgrade ||
            CurrentUpgradeState == UpgradeState.HoldingUpgrade;
        if (!IsChickForm && (nearbyEgg != null || atBasket) && CurrentUpgradeState != UpgradeState.HoldingUpgrade)
            prompt = false; // The nearby pickup owns Z until collected or walked away from.
        notificationRoot.style.display = ready || prompt ? DisplayStyle.Flex : DisplayStyle.None;
        // The stage banner replaces the plain label so passive stages read as their own reward.
        bool banner = stageUp && stageUpBanner != null;
        readyLabel.style.display = ready && !banner ? DisplayStyle.Flex : DisplayStyle.None;
        if (stageUpBanner != null) stageUpBanner.style.display = banner ? DisplayStyle.Flex : DisplayStyle.None;
        if (banner)
        {
            int stage = Mathf.Clamp(ChickStage, 1, StageUpTitles.Length - 1);
            stageUpNumber.text = stage.ToString();
            stageUpTitle.text = StageUpTitles[stage];
            stageUpDetail.text = "GELİŞİM " + stage + "/" + StageCount + "  ·  " + StageUpDetails[stage];
        }
        VisualElement pop = banner ? stageUpBanner : readyLabel;
        upgradePrompt.style.display = prompt ? DisplayStyle.Flex : DisplayStyle.None;
        if (ready)
        {
            float scale = stateTime < .16f ? Mathf.Lerp(.75f, 1.10f, Mathf.SmoothStep(0f, 1f, stateTime / .16f))
                : Mathf.Lerp(1.10f, 1f, Mathf.SmoothStep(0f, 1f, (stateTime - .16f) / .16f));
            pop.style.scale = new Scale(new Vector3(scale, scale, 1f));
            pop.style.opacity = Mathf.Clamp01(stateTime / .12f) *
                (1f - Mathf.Clamp01((stateTime - 1.25f) / .25f));
        }
        if (prompt)
        {
            float scale = CurrentUpgradeState == UpgradeState.HoldingUpgrade
                ? Mathf.Lerp(1.02f, 1.08f, UpgradeHoldProgress)
                : 1f + .025f * (1f - Mathf.Cos(promptTime * Mathf.PI * 2f));
            upgradePrompt.style.scale = new Scale(new Vector3(scale, scale, 1f));
        }
    }
}
