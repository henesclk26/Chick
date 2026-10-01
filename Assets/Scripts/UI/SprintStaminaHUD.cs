using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Drives the lower-left sprint gauge: stamina ring, refill twinkle and the chick-to-chicken icon change.
/// Visible during gameplay; hidden in menus, pause, day transitions and while the journal is open.
/// </summary>
[RequireComponent(typeof(UIDocument)), DisallowMultipleComponent]
public sealed class SprintStaminaHUD : MonoBehaviour
{
    [SerializeField] private ChickPlayerController player;
    [SerializeField] private PlayerGrowthController growth;
    [SerializeField] private MainMenuGameplayGate gameplayGate;
    [SerializeField] private GameTimeManager timeManager;
    [Header("Runner icons (transparent discs)")]
    [SerializeField] private Texture2D chickIcon;
    [SerializeField] private Texture2D chickenIcon;
    [SerializeField, Min(.01f)] private float fadeSeconds = .2f;
    [Tooltip("Length of the chick-to-chicken icon change during natural growth.")]
    [SerializeField, Min(.05f)] private float morphSeconds = .7f;

    private VisualElement hud;
    private SprintStaminaGauge gauge;
    private float opacity;
    private float morph;
    private float lastFill = 1f;

    public bool IsShown => opacity > 0f;
    public float FillFraction { get; private set; } = 1f;
    public float IconMorph => morph;

    private void OnEnable()
    {
        if (player == null) player = FindFirstObjectByType<ChickPlayerController>(FindObjectsInactive.Include);
        if (growth == null && player != null) growth = player.GetComponent<PlayerGrowthController>();
        if (gameplayGate == null) gameplayGate = FindFirstObjectByType<MainMenuGameplayGate>(FindObjectsInactive.Include);
        if (timeManager == null) timeManager = FindFirstObjectByType<GameTimeManager>(FindObjectsInactive.Include);
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;
        root.pickingMode = PickingMode.Ignore;
        hud = root.Q("SprintStaminaHUD");
        gauge = root.Q<SprintStaminaGauge>("SprintStaminaGauge");
        if (hud == null || gauge == null)
        {
            Debug.LogError("Sprint stamina HUD needs SprintStaminaHUD.uxml on its UIDocument.", this);
            return;
        }
        gauge.ChickIcon.image = chickIcon;
        gauge.ChickenIcon.image = chickenIcon;
        morph = IsChicken ? 1f : 0f;
        gauge.Morph = morph;
        opacity = 0f;
    }

    private bool IsChicken => growth != null && growth.CurrentForm == PlayerGrowthController.Form.Chicken;

    private void Update()
    {
        if (hud == null || gauge == null) return;
        float dt = Time.unscaledDeltaTime;
        bool gameplay = player != null && player.isActiveAndEnabled &&
            (gameplayGate == null || gameplayGate.IsReleased) && !GameplayJournalController.IsAnyOpen &&
            (timeManager == null || (!timeManager.IsPaused && !timeManager.IsTransitioning));
        opacity = gameplay ? Mathf.MoveTowards(opacity, 1f, dt / fadeSeconds) : 0f;
        hud.style.opacity = opacity;
        hud.style.display = opacity > 0f ? DisplayStyle.Flex : DisplayStyle.None;

        FillFraction = player != null ? player.SprintStamina01 : 1f;
        gauge.Fill = FillFraction;
        // Twinkle only while the ring is climbing back up.
        bool refilling = FillFraction < .999f && FillFraction > lastFill + .00001f;
        lastFill = FillFraction;
        gauge.Sparkle = refilling ? .55f + .45f * Mathf.Sin(Time.unscaledTime * 9f) : 0f;

        // Natural growth animates the icon; instant form changes (save load, developer panel) snap.
        float target = IsChicken ? 1f : 0f;
        bool animate = growth != null && growth.IsTransforming && gameplay;
        morph = animate ? Mathf.MoveTowards(morph, target, dt / morphSeconds) : target;
        gauge.Morph = morph;
    }
}
