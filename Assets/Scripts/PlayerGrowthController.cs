using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent, DefaultExecutionOrder(110)]
public sealed class PlayerGrowthController : MonoBehaviour
{
    public enum Form { Chick, Chicken }
    [SerializeField] private GameObject chickenVisualPrefab;
    [SerializeField, Range(.5f, 1f)] private float chickenBodyScale = .85f;
    [SerializeField] private Vector3 chickenImpactPoint = new Vector3(.03546f, .02624f, .35746f);
    [SerializeField] private float chickenReachScale = 2.4f;
    public Form CurrentForm { get; private set; }
    /// <summary>True while a chick-to-chicken request waits for a grounded, unobstructed moment.</summary>
    public bool GrowthPending => growthRequested || IsTransforming;
    public bool IsTransforming { get; private set; }
    [Header("Natural growth presentation")]
    [SerializeField] private GrowthSmokeBurst growthSmokePrefab;
    [SerializeField, Min(.5f)] private float growthDuration = 1.6f;
    private GrowthSmokeBurst smoke;
    private float growthElapsed;
    private bool formCommitted;
    private GameTimeManager gameTime;

    /// <summary>Progression entry point: grows once the player can safely change form.</summary>
    public void RequestChickenGrowth()
    {
        if (CurrentForm == Form.Chick && !IsTransforming) growthRequested = true;
    }

    private ChickPlayerController player;
    private ChickEatingController eater;
    private CharacterController capsule;
    private FreeOrbitThirdPersonCamera orbit;
    private Animator chickAnimator;
    private Renderer[] chickRenderers;
    private bool[] chickRendererEnabled;
    private Transform chickBeak;
    private Vector3 chickImpact, chickCenter;
    private float chickHeight, chickRadius;
    private GameObject chickenVisual;
    private Animator chickenAnimator;
    private Transform chickenBeak;
    private bool growthRequested;
    private readonly Collider[] overlaps = new Collider[32];

    private void Awake()
    {
        player = GetComponent<ChickPlayerController>();
        eater = GetComponent<ChickEatingController>();
        capsule = GetComponent<CharacterController>();
        orbit = Camera.main.GetComponent<FreeOrbitThirdPersonCamera>();
        gameTime = FindFirstObjectByType<GameTimeManager>();
        chickAnimator = player.ActiveAnimator;
        chickRenderers = GetComponentsInChildren<Renderer>(true);
        chickRendererEnabled = new bool[chickRenderers.Length];
        for (int i = 0; i < chickRenderers.Length; i++) chickRendererEnabled[i] = chickRenderers[i].enabled;
        chickBeak = eater.BeakEatPoint;
        chickImpact = eater.LocalImpactPoint;
        chickHeight = capsule.height; chickRadius = capsule.radius; chickCenter = capsule.center;
    }

    private void Update()
    {
        if (IsTransforming) { TickGrowth(); return; }
        if (!player.isActiveAndEnabled || Time.timeScale <= 0f ||
            (gameTime != null && (gameTime.IsPaused || gameTime.IsTransitioning))) return;
        var keyboard = Keyboard.current;
        // Layout lookup also supports Turkish F; Turkish Q Ü occupies RightBracket.
        var key = keyboard != null ? keyboard.FindKeyOnCurrentKeyboardLayout("ü") ?? keyboard.rightBracketKey : null;
        if (key != null && key.wasPressedThisFrame) RequestChickenGrowth();
        if (growthRequested && player.CanChangeForm && HasRoomForChicken()) BeginGrowth();
    }

    private bool HasRoomForChicken()
    {
        int count = Physics.OverlapCapsuleNonAlloc(transform.position + Vector3.up * (.215f * chickenBodyScale),
            transform.position + Vector3.up * (.395f * chickenBodyScale), .2f * chickenBodyScale, overlaps, ~0, QueryTriggerInteraction.Ignore);
        if (count == overlaps.Length) return false;
        for (int i = 0; i < count; i++)
            if (!overlaps[i].transform.IsChildOf(transform)) return false;
        return true;
    }

    private void BeginGrowth()
    {
        if (growthSmokePrefab == null) { SetForm(Form.Chicken); return; }
        if (smoke == null) smoke = Instantiate(growthSmokePrefab);
        smoke.transform.SetPositionAndRotation(transform.position, Quaternion.identity);
        smoke.gameObject.SetActive(true);
        growthElapsed = 0f;
        formCommitted = false;
        IsTransforming = true;
        player.GrowthControlsLocked = true;
        eater.CancelEat();
        smoke.Sample(0f);
    }

    private void TickGrowth()
    {
        // Pause freezes the whole presentation, including the form-switch moment.
        if (Time.timeScale <= 0f || (gameTime != null && gameTime.IsPaused)) return;
        if (!player.isActiveAndEnabled || (gameTime != null && gameTime.IsTransitioning))
        {
            StopGrowthPresentation();
            return; // an uncommitted request remains pending until gameplay resumes
        }
        growthElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(growthElapsed / Mathf.Max(.5f, growthDuration));
        if (smoke != null) smoke.Sample(t);
        float pullback = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / .16f)) *
            (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.68f, 1f, t)));
        float lift = pullback * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.42f, .7f, t)));
        if (orbit != null) orbit.SetGrowthFraming(1.45f * pullback, .13f * lift);
        if (!formCommitted && t >= .46f)
        {
            // Recheck clearance at the exact concealed switch, including moving obstacles.
            if (!capsule.isGrounded || !HasRoomForChicken())
            {
                StopGrowthPresentation();
                return;
            }
            ApplyForm(Form.Chicken);
            formCommitted = true;
            growthRequested = false;
        }
        player.GrowthControlsLocked = t < .76f;
        if (t >= 1f) StopGrowthPresentation();
    }

    private void StopGrowthPresentation()
    {
        IsTransforming = false;
        growthElapsed = 0f;
        if (player != null) player.GrowthControlsLocked = false;
        if (orbit != null) orbit.SetGrowthFraming(0f, 0f);
        if (smoke != null) smoke.gameObject.SetActive(false);
    }

    private void OnDisable() => StopGrowthPresentation();
    private void OnDestroy()
    {
        StopGrowthPresentation();
        if (smoke != null) Destroy(smoke.gameObject);
    }

    // Immediate changes (developer panel/save loading) deliberately bypass the presentation.
    public void SetForm(Form form)
    {
        StopGrowthPresentation();
        growthRequested = false;
        ApplyForm(form);
    }

    private void ApplyForm(Form form)
    {
        if (CurrentForm == form) return;
        if (form == Form.Chicken && chickenVisual == null)
        {
            chickenVisual = Instantiate(chickenVisualPrefab, transform);
            chickenVisual.name = "ChickenForm";
            chickenVisual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            chickenVisual.transform.localScale *= chickenBodyScale;
            chickenAnimator = chickenVisual.GetComponent<Animator>();
            chickenBeak = chickenVisual.transform.Find("Chicken/body/chest/neck/head/beak_top/BeakEatPoint");
            chickenVisual.GetComponent<ChickenEatEventRelay>().Bind(eater);
        }

        eater.CancelEat();
        bool grown = form == Form.Chicken;
        chickAnimator.enabled = !grown;
        for (int i = 0; i < chickRenderers.Length; i++) chickRenderers[i].enabled = !grown && chickRendererEnabled[i];
        if (chickenVisual != null) chickenVisual.SetActive(grown);
        bool capsuleEnabled = capsule.enabled;
        capsule.enabled = false;
        capsule.height = grown ? .6f * chickenBodyScale : chickHeight;
        capsule.radius = grown ? .21f * chickenBodyScale : chickRadius;
        capsule.center = grown ? new Vector3(0f, .3f * chickenBodyScale, 0f) : chickCenter;
        BirdGroundTraversal.Configure(capsule);
        capsule.enabled = capsuleEnabled;
        player.SetFormAnimator(grown ? chickenAnimator : chickAnimator);
        eater.ConfigureForm(player.ActiveAnimator, grown ? chickenBeak : chickBeak,
            grown ? chickenImpactPoint * chickenBodyScale : chickImpact, grown ? chickenReachScale : 1f);
        orbit.SetCharacterStage(grown ? FreeOrbitThirdPersonCamera.CameraProfile.Chicken : FreeOrbitThirdPersonCamera.CameraProfile.Chick);
        CurrentForm = form;
    }
}

