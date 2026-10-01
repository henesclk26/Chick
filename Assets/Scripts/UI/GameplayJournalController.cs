using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(UIDocument))]
public sealed class GameplayJournalController : MonoBehaviour
{
    [SerializeField] private MainMenuGameplayGate gameplayGate;
    [SerializeField] private GameTimeManager timeManager;
    [SerializeField] private ChickPlayerController player;
    [SerializeField] private FreeOrbitThirdPersonCamera orbitCamera;
    [SerializeField] private InGamePauseMenuController pauseMenu;
    [SerializeField] private FoodStatisticsTracker statistics;
    [SerializeField] private PlayerUpgrades upgrades;
    [SerializeField] private Texture2D chickSilhouette;
    [SerializeField] private Texture2D chickenSilhouette;
    [SerializeField, Min(1)] private int patternCost = 100;

    private VisualElement root;
    private VisualElement upgradesPage;
    private VisualElement statisticsPage;
    private VisualElement shopPage;
    private VisualElement statisticsList;
    private Button upgradesTab;
    private Button statisticsTab;
    private Button shopTab;
    private Button closeButton;
    private Button backButton;
    private Button patternButton;
    private Button chickPatternButton;
    private Button chickenPatternButton;
    private Label pointsLabel;
    private Label shopPointsLabel;
    private Label costLabel;
    private Label shopStatus;
    private Label headerTitle;
    private Label upgradesStatus;
    private Button peckPrice;
    private Button rangePrice;
    private Button helperPrice;
    private Button helperSpeedPrice;
    private Button helperRangePrice;
    private Button sprintSpeedPrice;
    private Button sprintDurationPrice;
    private Button doubleCollectPrice;
    private Button doubleJumpPrice;
    private Button glidePrice;
    private bool isOpen;
    private Coroutine backdropRoutine;
    private Texture2D backdropTexture;
    private float previousTimeScale = 1f;
    private bool previousPlayerEnabled;
    private bool previousOrbitEnabled;
    private CursorLockMode previousCursorLockState;
    private bool previousCursorVisible;
    private static int lastClosedFrame = -1;

    // Journal cards backed by PlayerUpgrades, found by their element-name prefix ("peck-price", "peck-cost", ...).
    private readonly struct LiveUpgrade
    {
        public readonly string Prefix, Id, Name;
        public LiveUpgrade(string prefix, string id, string name) { Prefix = prefix; Id = id; Name = name; }
    }
    private static readonly LiveUpgrade[] LiveUpgrades =
    {
        new LiveUpgrade("peck", PlayerUpgrades.FasterPeck, "Daha Hızlı Gagalama"),
        new LiveUpgrade("range", PlayerUpgrades.CollectRange, "Toplama Menzili"),
        new LiveUpgrade("sprint-speed", PlayerUpgrades.SprintSpeed, "Sprint Hızı"),
        new LiveUpgrade("sprint-duration", PlayerUpgrades.SprintDuration, "Sprint Süresi"),
        new LiveUpgrade("helper", PlayerUpgrades.HelperChick, "Yardımcı Civciv"),
        new LiveUpgrade("helper-speed", PlayerUpgrades.HelperEatSpeed, "Yardımcı Gagalama"),
        new LiveUpgrade("helper-range", PlayerUpgrades.HelperRange, "Yardımcı Menzili"),
        new LiveUpgrade("double-jump", PlayerUpgrades.DoubleJump, "Çift Zıplama"),
        new LiveUpgrade("glide", PlayerUpgrades.Glide, "Süzülme")
    };

    public static bool IsAnyOpen { get; private set; }
    public static bool BlocksEscapeThisFrame => IsAnyOpen || lastClosedFrame == Time.frameCount;

    private void Awake() => ResolveReferences();

    private void OnEnable()
    {
        ResolveReferences();
        root = GetComponent<UIDocument>().rootVisualElement;
        upgradesPage = root.Q("upgrades-page");
        statisticsPage = root.Q("statistics-page");
        shopPage = root.Q("shop-page");
        statisticsList = root.Q("statistics-list");
        upgradesTab = root.Q<Button>("upgrades-tab");
        statisticsTab = root.Q<Button>("statistics-tab");
        shopTab = root.Q<Button>("shop-tab");
        closeButton = root.Q<Button>("journal-close");
        backButton = root.Q<Button>("journal-back");
        patternButton = root.Q<Button>("pattern-card");
        chickPatternButton = root.Q<Button>("chick-pattern-card");
        chickenPatternButton = root.Q<Button>("chicken-pattern-card");
        pointsLabel = root.Q<Label>("points-label");
        shopPointsLabel = root.Q<Label>("shop-points-label");
        costLabel = root.Q<Label>("pattern-cost");
        shopStatus = root.Q<Label>("shop-status");
        headerTitle = root.Q<Label>("header-title");
        upgradesStatus = root.Q<Label>("upgrades-status");
        peckPrice = root.Q<Button>("peck-price");
        rangePrice = root.Q<Button>("range-price");
        helperPrice = root.Q<Button>("helper-price");
        helperSpeedPrice = root.Q<Button>("helper-speed-price");
        helperRangePrice = root.Q<Button>("helper-range-price");
        sprintSpeedPrice = root.Q<Button>("sprint-speed-price");
        sprintDurationPrice = root.Q<Button>("sprint-duration-price");
        doubleCollectPrice = root.Q<Button>("double-collect-price");
        doubleJumpPrice = root.Q<Button>("double-jump-price");
        glidePrice = root.Q<Button>("glide-price");

        upgradesTab.clicked += ShowUpgrades;
        statisticsTab.clicked += ShowStatistics;
        shopTab.clicked += ShowShop;
        closeButton.clicked += Close;
        if (backButton != null) backButton.clicked += Close;
        patternButton.clicked += PatternClicked;
        chickPatternButton.clicked += ChickPatternClicked;
        chickenPatternButton.clicked += ChickenPatternClicked;
        peckPrice.clicked += PeckPriceClicked;
        rangePrice.clicked += RangePriceClicked;
        helperPrice.clicked += HelperPriceClicked;
        if (helperSpeedPrice != null) helperSpeedPrice.clicked += HelperSpeedPriceClicked;
        if (helperRangePrice != null) helperRangePrice.clicked += HelperRangePriceClicked;
        if (sprintSpeedPrice != null) sprintSpeedPrice.clicked += SprintSpeedPriceClicked;
        if (sprintDurationPrice != null) sprintDurationPrice.clicked += SprintDurationPriceClicked;
        if (doubleCollectPrice != null) doubleCollectPrice.clicked += UpgradePreviewClicked;
        if (doubleJumpPrice != null) doubleJumpPrice.clicked += DoubleJumpPriceClicked;
        if (glidePrice != null) glidePrice.clicked += GlidePriceClicked;
        // Upgrades forward statistic changes too, since eaten food is the egg balance.
        if (upgrades != null) upgrades.Changed += Refresh;
        else if (statistics != null) statistics.Changed += Refresh;

        Texture2D goldEgg = Resources.Load<Texture2D>("Journal/GoldEgg");
        Texture2D egg = Resources.Load<Texture2D>("Journal/Egg");
        SetImage("balance-egg", egg);
        SetImage("egg-rate-gold", goldEgg);
        SetImage("egg-rate-white", egg);
        SetImage("peck-egg", egg);
        SetImage("range-egg", egg);
        SetImage("helper-egg", egg);
        SetImage("helper-speed-egg", egg);
        SetImage("helper-range-egg", egg);
        SetImage("sprint-speed-egg", egg);
        SetImage("sprint-duration-egg", egg);
        SetImage("double-collect-egg", egg);
        SetImage("double-jump-egg", egg);
        SetImage("glide-egg", egg);
        SetImage("range-art", goldEgg);
        SetImage("peck-art", chickenSilhouette);
        SetImage("helper-art", chickSilhouette);
        SetImage("sprint-speed-art", Resources.Load<Texture2D>("Journal/SprintSpeedArt"));
        SetImage("sprint-duration-art", Resources.Load<Texture2D>("Journal/SprintDurationArt"));
        SetImage("double-collect-art", Resources.Load<Texture2D>("Journal/DoubleCollectArt"));
        SetImage("double-jump-art", Resources.Load<Texture2D>("Journal/DoubleJumpArt"));
        SetImage("glide-art", Resources.Load<Texture2D>("Journal/GlideArt"));
        // Upgrades without mechanics yet are visual placeholders and start at level 0.
        FillLevels("double-collect-levels", 0);
        if (upgradesStatus != null) upgradesStatus.text = "";

        Image silhouette = root.Q<Image>("chicken-silhouette");
        if (silhouette != null && chickenSilhouette != null)
        {
            silhouette.image = chickenSilhouette;
            silhouette.tintColor = new Color(0.17f, 0.18f, 0.15f, 0.96f);
        }
        Image currentChick = root.Q<Image>("current-chick-icon");
        if (currentChick != null && chickSilhouette != null)
        {
            currentChick.image = chickSilhouette;
            currentChick.tintColor = Color.white;
        }
        Image currentChicken = root.Q<Image>("current-chicken-icon");
        if (currentChicken != null && chickenSilhouette != null)
        {
            currentChicken.image = chickenSilhouette;
            currentChicken.tintColor = Color.white;
        }
        if (costLabel != null) costLabel.text = patternCost + " PUAN";
        GameplayJournalReferenceTheme.Apply(root);
        root.style.display = DisplayStyle.None;
    }

    private void OnDisable()
    {
        if (upgradesTab != null) upgradesTab.clicked -= ShowUpgrades;
        if (statisticsTab != null) statisticsTab.clicked -= ShowStatistics;
        if (shopTab != null) shopTab.clicked -= ShowShop;
        if (closeButton != null) closeButton.clicked -= Close;
        if (backButton != null) backButton.clicked -= Close;
        if (patternButton != null) patternButton.clicked -= PatternClicked;
        if (chickPatternButton != null) chickPatternButton.clicked -= ChickPatternClicked;
        if (chickenPatternButton != null) chickenPatternButton.clicked -= ChickenPatternClicked;
        if (peckPrice != null) peckPrice.clicked -= PeckPriceClicked;
        if (rangePrice != null) rangePrice.clicked -= RangePriceClicked;
        if (helperPrice != null) helperPrice.clicked -= HelperPriceClicked;
        if (helperSpeedPrice != null) helperSpeedPrice.clicked -= HelperSpeedPriceClicked;
        if (helperRangePrice != null) helperRangePrice.clicked -= HelperRangePriceClicked;
        if (sprintSpeedPrice != null) sprintSpeedPrice.clicked -= SprintSpeedPriceClicked;
        if (sprintDurationPrice != null) sprintDurationPrice.clicked -= SprintDurationPriceClicked;
        if (doubleCollectPrice != null) doubleCollectPrice.clicked -= UpgradePreviewClicked;
        if (doubleJumpPrice != null) doubleJumpPrice.clicked -= DoubleJumpPriceClicked;
        if (glidePrice != null) glidePrice.clicked -= GlidePriceClicked;
        if (upgrades != null) upgrades.Changed -= Refresh;
        else if (statistics != null) statistics.Changed -= Refresh;
        if (isOpen) Close();
    }

    private void Update()
    {
        if (gameplayGate == null || !gameplayGate.IsReleased) return;
        if (timeManager != null && timeManager.IsTransitioning) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.tabKey.wasPressedThisFrame)
        {
            if (isOpen) Close(); else Open();
        }
        else if (isOpen && keyboard.escapeKey.wasPressedThisFrame)
        {
            Close();
        }
    }

    public void Open()
    {
        if (isOpen || root == null || DeveloperPanelController.IsAnyOpen || (pauseMenu != null && pauseMenu.IsPaused)) return;
        isOpen = true;
        IsAnyOpen = true;
        previousTimeScale = Time.timeScale;
        previousPlayerEnabled = player != null && player.enabled;
        previousOrbitEnabled = orbitCamera != null && orbitCamera.enabled;
        previousCursorLockState = UnityEngine.Cursor.lockState;
        previousCursorVisible = UnityEngine.Cursor.visible;

        if (timeManager != null) timeManager.IsPaused = true;
        if (player != null) player.enabled = false;
        if (orbitCamera != null) orbitCamera.enabled = false;
        Time.timeScale = 0f;
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
        root.style.display = DisplayStyle.Flex;
        root.style.visibility = Visibility.Hidden;
        ShowUpgrades();
        Refresh();
        upgradesTab?.Focus();
        backdropRoutine = StartCoroutine(RevealWithBackdrop());
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        IsAnyOpen = false;
        lastClosedFrame = Time.frameCount;
        if (backdropRoutine != null) StopCoroutine(backdropRoutine);
        backdropRoutine = null;
        ClearBackdrop();
        if (root != null) root.style.display = DisplayStyle.None;
        if (timeManager != null) timeManager.IsPaused = false;
        if (player != null) player.enabled = previousPlayerEnabled;
        if (orbitCamera != null) orbitCamera.enabled = previousOrbitEnabled;
        Time.timeScale = previousTimeScale;
        UnityEngine.Cursor.lockState = previousCursorLockState;
        UnityEngine.Cursor.visible = previousCursorVisible;
    }

    private IEnumerator RevealWithBackdrop()
    {
        // Capture the real game before the journal becomes visible. Downsampling
        // creates a soft, frozen backdrop without changing the world camera.
        yield return new WaitForEndOfFrame();
        if (!isOpen) yield break;
        Texture2D capture = null;
        RenderTexture small = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            capture = ScreenCapture.CaptureScreenshotAsTexture();
            if (capture != null)
            {
                int width = Mathf.Clamp(capture.width / 5, 160, 480);
                int height = Mathf.Max(90, Mathf.RoundToInt(width * (float)capture.height / capture.width));
                small = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(capture, small);
                RenderTexture.active = small;
                backdropTexture = new Texture2D(width, height, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                backdropTexture.ReadPixels(new Rect(0,0,width,height),0,0);
                backdropTexture.Apply(false, true);
                root.Q(className: "screen-dim").style.backgroundImage = new StyleBackground(backdropTexture);
            }
        }
        finally
        {
            RenderTexture.active = previous;
            if (small != null) RenderTexture.ReleaseTemporary(small);
            if (capture != null) Destroy(capture);
            if (isOpen) root.style.visibility = Visibility.Visible;
            backdropRoutine = null;
        }
    }

    private void ClearBackdrop()
    {
        if (root != null)
        {
            root.Q(className: "screen-dim").style.backgroundImage = StyleKeyword.None;
            root.style.visibility = Visibility.Visible;
        }
        if (backdropTexture != null) Destroy(backdropTexture);
        backdropTexture = null;
    }

    public void ShowUpgrades()
    {
        upgradesPage?.RemoveFromClassList("hidden");
        statisticsPage?.AddToClassList("hidden");
        shopPage?.AddToClassList("hidden");
        upgradesTab?.AddToClassList("side-active");
        statisticsTab?.RemoveFromClassList("side-active");
        shopTab?.RemoveFromClassList("side-active");
        if (headerTitle != null) headerTitle.text = "YÜKSELTMELER";
        GameplayJournalReferenceTheme.RefreshNavigation(root);
    }

    public void ShowStatistics()
    {
        upgradesPage?.AddToClassList("hidden");
        statisticsPage?.RemoveFromClassList("hidden");
        shopPage?.AddToClassList("hidden");
        upgradesTab?.RemoveFromClassList("side-active");
        statisticsTab?.AddToClassList("side-active");
        shopTab?.RemoveFromClassList("side-active");
        if (headerTitle != null) headerTitle.text = "İSTATİSTİK";
        GameplayJournalReferenceTheme.RefreshNavigation(root);
    }

    public void ShowShop()
    {
        upgradesPage?.AddToClassList("hidden");
        statisticsPage?.AddToClassList("hidden");
        shopPage?.RemoveFromClassList("hidden");
        upgradesTab?.RemoveFromClassList("side-active");
        statisticsTab?.RemoveFromClassList("side-active");
        shopTab?.AddToClassList("side-active");
        if (headerTitle != null) headerTitle.text = "MAĞAZA";
        GameplayJournalReferenceTheme.RefreshNavigation(root);
        RefreshShop();
    }

    // Spendable eggs: eaten food minus what upgrades have already cost.
    private int AvailableEggs => upgrades != null ? upgrades.AvailableEggs : 0;

    private void Refresh()
    {
        int total = AvailableEggs;
        if (pointsLabel != null) pointsLabel.text = total.ToString();
        if (shopPointsLabel != null) shopPointsLabel.text = "Birikmiş puanın: " + total;
        RefreshUpgrades();
        RefreshStatistics();
        RefreshShop();
    }

    private void RefreshStatistics()
    {
        if (statisticsList == null) return;
        statisticsList.Clear();
        IReadOnlyList<FoodStatSaveEntry> rows = statistics != null
            ? statistics.GetSnapshot(true)
            : new FoodStatSaveEntry[0];

        foreach (FoodStatSaveEntry entry in rows)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("stat-card");

            Label icon = new Label(FoodStatisticIdentity.ResolveBadge(entry.key));
            icon.AddToClassList("food-icon");
            icon.style.backgroundColor = FoodStatisticIdentity.ResolveColor(entry.key);

            VisualElement text = new VisualElement();
            text.AddToClassList("stat-text");
            Label name = new Label(entry.displayName);
            name.AddToClassList("stat-name");
            Label caption = new Label("Yenen miktar");
            caption.AddToClassList("stat-caption");
            text.Add(name);
            text.Add(caption);

            Label count = new Label(entry.count.ToString());
            count.AddToClassList("stat-count");
            card.Add(icon);
            card.Add(text);
            card.Add(count);
            statisticsList.Add(card);
        }
    }

    private void RefreshShop()
    {
        if (shopStatus == null) return;
        int points = AvailableEggs;
        shopStatus.text = points >= patternCost
            ? "Hazır! Desen ödülü sonraki aşamada bu karttan açılacak."
            : (patternCost - points) + " puan daha topla.";
    }

    private void RefreshUpgrades()
    {
        if (upgrades == null) return;
        foreach (LiveUpgrade card in LiveUpgrades)
        {
            Button button = root.Q<Button>(card.Prefix + "-price");
            if (button == null) continue;
            FillLevels(card.Prefix + "-levels", upgrades.GetLevel(card.Id), upgrades.GetMaxLevel(card.Id));
            int price = upgrades.GetNextPrice(card.Id);
            bool maxed = price < 0;
            button.EnableInClassList("max-price", maxed);
            button.EnableInClassList("unaffordable", !maxed && (AvailableEggs < price || upgrades.IsLocked(card.Id)));
            Label cost = root.Q<Label>(card.Prefix + "-cost");
            if (cost != null) cost.text = maxed ? "MAX" : price.ToString();
            Image egg = root.Q<Image>(card.Prefix + "-egg");
            if (egg != null) egg.style.display = maxed ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }

    private void PeckPriceClicked() => PurchaseClicked(LiveUpgrades[0]);
    private void RangePriceClicked() => PurchaseClicked(LiveUpgrades[1]);
    private void SprintSpeedPriceClicked() => PurchaseClicked(LiveUpgrades[2]);
    private void SprintDurationPriceClicked() => PurchaseClicked(LiveUpgrades[3]);
    private void HelperPriceClicked() => PurchaseClicked(LiveUpgrades[4]);
    private void HelperSpeedPriceClicked() => PurchaseClicked(LiveUpgrades[5]);
    private void HelperRangePriceClicked() => PurchaseClicked(LiveUpgrades[6]);
    private void DoubleJumpPriceClicked() => PurchaseClicked(LiveUpgrades[7]);
    private void GlidePriceClicked() => PurchaseClicked(LiveUpgrades[8]);

    private void PurchaseClicked(LiveUpgrade card)
    {
        if (upgrades == null) { UpgradePreviewClicked(); return; }
        int price = upgrades.GetNextPrice(card.Id);
        string message;
        if (price < 0) message = card.Name + " en yüksek seviyede.";
        else if (upgrades.IsLocked(card.Id)) message = "Önce Yardımcı Civciv yükseltmesini almalısın.";
        else if (upgrades.TryPurchase(card.Id))
            message = upgrades.IsMaxed(card.Id)
                ? card.Name + " en yüksek seviyeye ulaştı!"
                : card.Name + " " + upgrades.GetLevel(card.Id) + ". seviyeye yükseldi!";
        else message = "Bu yükseltme için " + (price - AvailableEggs) + " yumurta daha gerekli.";
        if (upgradesStatus != null) upgradesStatus.text = message;
        PulseCard(root.Q<Button>(card.Prefix + "-price"));
    }

    private void UpgradePreviewClicked()
    {
        if (upgradesStatus != null) upgradesStatus.text = "Yükseltme satın alımları sonraki aşamada açılacak.";
    }

    private void SetImage(string name, Texture2D texture)
    {
        Image image = root.Q<Image>(name);
        if (image == null || texture == null) return;
        image.image = texture;
        image.scaleMode = ScaleMode.ScaleToFit;
    }

    private void FillLevels(string name, int filled, int total = 4)
    {
        VisualElement row = root.Q(name);
        if (row == null) return;
        row.Clear();
        for (int i = 0; i < total; i++)
        {
            VisualElement dot = GameplayJournalReferenceTheme.CreateLevelEgg(i < filled);
            dot.AddToClassList("level-dot");
            if (i < filled) dot.AddToClassList("level-filled");
            row.Add(dot);
        }
    }

    private void PatternClicked()
    {
        int points = AvailableEggs;
        shopStatus.text = points >= patternCost
            ? "Desen sistemi hazır olduğunda ödülün burada açılacak."
            : "Bu desen kutusu için " + (patternCost - points) + " puan daha gerekli.";
        patternButton?.AddToClassList("card-pulse");
        patternButton?.schedule.Execute(() => patternButton?.RemoveFromClassList("card-pulse")).ExecuteLater(180);
    }

    private void ChickPatternClicked()
    {
        if (shopStatus != null) shopStatus.text = "Sarı civciv görünümü seçili ve kullanıma hazır.";
        PulseCard(chickPatternButton);
    }

    private void ChickenPatternClicked()
    {
        if (shopStatus != null) shopStatus.text = "Beyaz tavuk görünümü seçili ve kullanıma hazır.";
        PulseCard(chickenPatternButton);
    }

    private static void PulseCard(Button card)
    {
        if (card == null) return;
        card.AddToClassList("card-pulse");
        card.schedule.Execute(() => card.RemoveFromClassList("card-pulse")).ExecuteLater(180);
    }

    private void ResolveReferences()
    {
        if (gameplayGate == null) gameplayGate = FindFirstObjectByType<MainMenuGameplayGate>(FindObjectsInactive.Include);
        if (timeManager == null) timeManager = FindFirstObjectByType<GameTimeManager>(FindObjectsInactive.Include);
        if (player == null) player = FindFirstObjectByType<ChickPlayerController>(FindObjectsInactive.Include);
        if (orbitCamera == null) orbitCamera = FindFirstObjectByType<FreeOrbitThirdPersonCamera>(FindObjectsInactive.Include);
        if (pauseMenu == null) pauseMenu = FindFirstObjectByType<InGamePauseMenuController>(FindObjectsInactive.Include);
        if (statistics == null) statistics = FindFirstObjectByType<FoodStatisticsTracker>(FindObjectsInactive.Include);
        if (upgrades == null) upgrades = FindFirstObjectByType<PlayerUpgrades>(FindObjectsInactive.Include);
    }
}
