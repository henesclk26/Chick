using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator), typeof(ChickPlayerController))]
public sealed class ChickEatingController : MonoBehaviour
{
    [Header("Eating")]
    [SerializeField, Range(1.3f, 1.5f)] private float eatAnimationSpeed = 1.4f;
    [Tooltip("\"Daha Hızlı Gagalama\" levels speed up the peck on top of the base speed.")]
    [SerializeField] private PlayerUpgrades upgrades;
    [Header("Eat Targeting")]
    [SerializeField, Min(0.001f)] private float eatDetectionRadius = 0.12f;
    [SerializeField, Min(0f)] private float eatForwardOffset = 0.09f;
    [SerializeField, Range(0f, 89f)] private float maxTargetAngle = 80f;
    [SerializeField, Min(0.001f)] private float maxContactAssistDistance = 0.13f;
    [SerializeField, Range(0.08f, 0.15f)] private float contactAssistDuration = 0.14f;
    [SerializeField] private Transform beakEatPoint;
    // Measured contact position for ground-height validation / editor visualization only.
    // It is NOT a beak-alignment prerequisite for gameplay selection.
    [SerializeField, HideInInspector] private Vector3 localImpactPoint = new Vector3(-0.007f, 0.011f, 0.087f);
    [SerializeField] private LayerMask eatLayerMask = 1 << 9;
    [SerializeField] private InputAction eatAction =
        new InputAction("Eat", InputActionType.Button, "<Mouse>/leftButton");

    private enum EatPhase { Ready, Eating, Recovering }
    private EatPhase phase;
    private Animator animator;
    private ChickPlayerController player;
    private CharacterController characterController;
    private EdibleObject target;
    private bool impactHandled;
    private bool enteredEatState;
    private float startDeadline;
    private Transform targetVisual;
    private Vector3 originalVisualLocalPosition;
    private Vector3 originalBitePosition;
    private Vector3 targetRootAtStart;
    private Quaternion targetRotationAtStart;
    private Vector3 targetScaleAtStart;
    private bool assistInvalidated;
    private float eatClipLength = 80f / 60f;
    private float impactNormalizedTime = 21f / 80f;
    private float bufferedEatUntil = float.NegativeInfinity;
    private float formReachScale = 1f;
    // "Toplama Menzili" level; widens horizontal reach only, never the vertical contact limit.
    private float upgradeReachScale = 1f;
    private float baseDetectionRadius, baseForwardOffset, baseAssistDistance;
    private const float EatBufferSeconds = 0.12f;
    private const float MaximumAssistMultiplier = 1.5f;
    private readonly Collider[] nearby = new Collider[64];
    private readonly Collider[] denseNearby = new Collider[256];
    private static readonly int EatSpeed = Animator.StringToHash("EatPlaybackSpeed");

    public bool IsEating => phase == EatPhase.Eating;
    public bool IsBusy => phase != EatPhase.Ready;
    public bool EatPressedThisFrame => isActiveAndEnabled && eatAction.WasPressedThisFrame();

    // Called once by the player before locomotion is evaluated. One slot, no held-click auto-eat.
    public bool ReadEatRequest()
    {
        if (!isActiveAndEnabled || !player.isActiveAndEnabled) return false;
        bool pressed = EatPressedThisFrame;
        if (IsBusy)
        {
            if (pressed && phase == EatPhase.Recovering && animator.IsInTransition(0))
            {
                var transition = animator.GetAnimatorTransitionInfo(0);
                float remaining = transition.duration * (1f - transition.normalizedTime);
                if (remaining <= EatBufferSeconds) bufferedEatUntil = Time.time + EatBufferSeconds;
            }
            return false;
        }
        bool requested = pressed || Time.time <= bufferedEatUntil;
        bufferedEatUntil = float.NegativeInfinity;
        return requested;
    }
    public EdibleObject CurrentTarget => target;
    public bool LastQuerySaturated { get; private set; }
    public bool LastImpactSucceeded { get; private set; }
    public float LastContactError { get; private set; }

    private Vector3 DetectionCenter => transform.position +
        transform.forward * eatForwardOffset + Vector3.up * (0.015f * formReachScale);
    private Vector3 PredictedContact => transform.TransformPoint(localImpactPoint);
    public Transform BeakEatPoint => beakEatPoint;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        player = GetComponent<ChickPlayerController>();
        characterController = GetComponent<CharacterController>();
        baseDetectionRadius = eatDetectionRadius;
        baseForwardOffset = eatForwardOffset;
        baseAssistDistance = maxContactAssistDistance;
        if (upgrades == null) upgrades = FindFirstObjectByType<PlayerUpgrades>(FindObjectsInactive.Include);
        ReadAnimationTiming();
    }

    public void ConfigureForm(Animator formAnimator, Transform beak, Vector3 impactPoint, float reachScale)
    {
        CancelEat();
        animator = formAnimator;
        beakEatPoint = beak;
        localImpactPoint = impactPoint;
        formReachScale = Mathf.Max(1f, reachScale);
        eatForwardOffset = baseForwardOffset * formReachScale;
        ApplyReach();
        ReadAnimationTiming();
    }

    public Vector3 LocalImpactPoint => localImpactPoint;

    private void ApplyReach()
    {
        upgradeReachScale = upgrades != null ? Mathf.Max(1f, upgrades.CollectRangeMultiplier) : 1f;
        float reach = formReachScale * upgradeReachScale;
        eatDetectionRadius = baseDetectionRadius * reach;
        maxContactAssistDistance = baseAssistDistance * reach;
    }

    private void ReadAnimationTiming()
    {
        // Read the existing event once; never modify the approved clip or playback speed.
        foreach (var clip in animator.runtimeAnimatorController.animationClips)
        foreach (var clipEvent in clip.events)
        {
            if (clipEvent.functionName != nameof(OnEatImpact)) continue;
            eatClipLength = clip.length;
            impactNormalizedTime = clipEvent.time / clip.length;
        }
    }

    private void OnEnable() => eatAction.Enable();

    private void OnDisable()
    {
        eatAction.Disable();
        CancelEat();
    }

    private void OnDestroy() => eatAction.Dispose();

    // The existing player controller owns grounded/action/movement eligibility.
    public bool TryBeginEat()
    {
        if (!isActiveAndEnabled || IsBusy || !player.isActiveAndEnabled || !animator.isActiveAndEnabled || beakEatPoint == null)
            return false;
        // Refresh before targeting so a purchase in the journal applies to the very next peck.
        ApplyReach();
        target = FindTarget();
        if (target == null) return false;

        targetVisual = target.ContactVisual;
        originalVisualLocalPosition = targetVisual.localPosition;
        originalBitePosition = target.BitePosition;
        targetRootAtStart = target.transform.position;
        targetRotationAtStart = target.transform.rotation;
        targetScaleAtStart = target.transform.lossyScale;
        assistInvalidated = false;

        // Contact assist and impact timing read the live playback speed, so a faster peck stays in sync.
        float peckMultiplier = upgrades != null ? upgrades.PeckSpeedMultiplier : 1f;
        animator.SetFloat(EatSpeed, Mathf.Clamp(eatAnimationSpeed, 1.3f, 1.5f) * peckMultiplier);
        impactHandled = false;
        LastImpactSucceeded = false;
        LastContactError = float.PositiveInfinity;
        enteredEatState = false;
        startDeadline = Time.time + 0.25f;
        phase = EatPhase.Eating;
        return true;
    }

    private EdibleObject FindTarget(EdibleObject exclude = null)
    {
        Collider[] candidates = nearby;
        // Bounded broad phase covers the metadata cap; narrow phase uses each food's multiplier.
        int count = Physics.OverlapSphereNonAlloc(DetectionCenter, eatDetectionRadius * MaximumAssistMultiplier,
            candidates, eatLayerMask, QueryTriggerInteraction.Collide);
        if (count == nearby.Length)
        {
            candidates = denseNearby;
            count = Physics.OverlapSphereNonAlloc(DetectionCenter, eatDetectionRadius * MaximumAssistMultiplier,
                candidates, eatLayerMask, QueryTriggerInteraction.Collide);
        }
        LastQuerySaturated = count == candidates.Length;
        EdibleObject best = null;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var edible = candidates[i].GetComponentInParent<EdibleObject>();
            candidates[i] = null;
            if (edible == exclude || !IsTargetValid(edible)) continue;
            Vector3 delta = edible.BitePosition - transform.position;
            Vector3 planar = Vector3.ProjectOnPlane(delta, Vector3.up);
            float alignment = Vector3.Dot(planar.normalized, transform.forward);
            float distance = (edible.BitePosition - DetectionCenter).sqrMagnitude;
            float beakDistance = (edible.BitePosition - beakEatPoint.position).sqrMagnitude;
            float height = edible.BitePosition.y - PredictedContact.y;
            float score = distance + 0.2f * beakDistance + 0.0015f * (1f - alignment) + 2f * height * height;
            if (score < bestScore || (Mathf.Approximately(score, bestScore) &&
                best != null && edible.GetInstanceID() < best.GetInstanceID()))
            {
                best = edible;
                bestScore = score;
            }
        }
        return best;
    }

    private bool IsTargetValid(EdibleObject edible)
    {
        if (edible == null || beakEatPoint == null || !edible.CanBeEaten() ||
            HelperChickController.IsFoodReservedByHelper(edible) ||
            edible.ContactVisual == null || edible.ContactVisual == edible.transform ||
            !edible.ContactVisual.IsChildOf(edible.transform) ||
            !edible.IsSurfaceAccessibleFrom(characterController.bounds.center) ||
            (eatLayerMask.value & (1 << edible.gameObject.layer)) == 0) return false;
        Vector3 delta = edible.BitePosition - transform.position;
        Vector3 planar = Vector3.ProjectOnPlane(delta, Vector3.up);
        if (planar.sqrMagnitude < 0.000001f ||
            Vector3.Dot(planar.normalized, transform.forward) < Mathf.Cos(maxTargetAngle * Mathf.Deg2Rad)) return false;
        float radius = eatDetectionRadius * edible.InteractionAssistMultiplier;
        float assistReach = maxContactAssistDistance * edible.InteractionAssistMultiplier;
        // Proximity + a broad front arc, not pre-alignment with the animated beak.
        return Mathf.Abs(edible.BitePosition.y - PredictedContact.y) <= edible.MaxVerticalInteractionOffset * formReachScale &&
               planar.sqrMagnitude <= 0.25f * 0.25f * formReachScale * formReachScale * upgradeReachScale * upgradeReachScale &&
               (edible.BitePosition - PredictedContact).sqrMagnitude <= assistReach * assistReach &&
               (edible.BitePosition - DetectionCenter).sqrMagnitude <= radius * radius;
    }

    // Animation Event: first beak minimum, source frame 21/80 at 60 fps.
    public void OnEatImpact()
    {
        if (!IsEating || impactHandled || !player.isActiveAndEnabled) return;
        // Events run before LateUpdate. Finish the tiny correction here using the freshly
        // evaluated bone pose, so low frame rates cannot consume before visual contact.
        bool atContact = ApplyContactAssist(1f);
        impactHandled = true;
        if (atContact && IsTargetValid(target)) LastImpactSucceeded = target.Consume();
        RestoreContactVisual();
        // A purchased double collection can take one additional reachable seed, never a
        // companion's reserved meal or an extra bite from the same multi-bite fruit.
        if (LastImpactSucceeded && target != null && target.IsConsumed && upgrades != null &&
            upgrades.DoubleCollectChance > 0f && Random.value < upgrades.DoubleCollectChance)
        {
            EdibleObject extra = FindTarget(target);
            if (extra != null && extra.BiteHandler == null) extra.Consume();
        }
    }

    // Animation Event: first recovery crest (frame 30). Skip the two extra pecks.
    // Keep the action lock while the existing idle blend raises the head smoothly.
    public void OnEatRecovery()
    {
        if (IsEating) phase = EatPhase.Recovering;
    }

    private void LateUpdate()
    {
        if (!player.isActiveAndEnabled || !animator.isActiveAndEnabled)
        {
            CancelEat();
            return;
        }
        if (!IsBusy) return;
        var current = animator.GetCurrentAnimatorStateInfo(0);
        bool transitioning = animator.IsInTransition(0);
        bool currentEat = current.IsName("eat");
        bool nextEat = transitioning && animator.GetNextAnimatorStateInfo(0).IsName("eat");
        if (currentEat || nextEat) enteredEatState = true;
        if (IsEating && !impactHandled && currentEat)
        {
            float playback = Mathf.Max(0.01f, current.speed * current.speedMultiplier * animator.speed);
            float remaining = (impactNormalizedTime - current.normalizedTime) * eatClipLength / playback;
            if (remaining <= contactAssistDuration)
                ApplyContactAssist(Mathf.Clamp01(1f - remaining / contactAssistDuration));
        }

        // State-based cleanup also handles external interruption or missing recovery events.
        if (phase == EatPhase.Eating && currentEat && current.normalizedTime >= 1f)
            phase = EatPhase.Recovering;
        if (!currentEat && !nextEat && enteredEatState && !transitioning)
            ClearAction(phase == EatPhase.Recovering);
        else if (!enteredEatState && Time.time > startDeadline) CancelEat();
    }

    public void CancelEat()
    {
        ClearAction(false);
    }

    private void ClearAction(bool keepBufferedRequest)
    {
        RestoreContactVisual();
        phase = EatPhase.Ready;
        target = null;
        targetVisual = null;
        impactHandled = false;
        enteredEatState = false;
        if (!keepBufferedRequest) bufferedEatUntil = float.NegativeInfinity;
    }

    private bool ApplyContactAssist(float progress)
    {
        if (assistInvalidated || target == null || targetVisual == null || beakEatPoint == null ||
            !target.CanBeEaten() || !IsTargetValid(target) ||
            (target.transform.position - targetRootAtStart).sqrMagnitude > 0.000001f ||
            Quaternion.Angle(target.transform.rotation, targetRotationAtStart) > 0.1f ||
            (target.transform.lossyScale - targetScaleAtStart).sqrMagnitude > 0.000001f)
        {
            assistInvalidated = true;
            RestoreContactVisual();
            return false;
        }

        Vector3 destination = beakEatPoint.position;
        // Keep the original height until the final quarter, then reach the actual beak.
        // Selection already limits height/reach. A separate 6 mm vertical clamp left
        // valid fruit toppings short of the beak and made Consume fail after a peck.
        float verticalBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1f, progress));
        destination.y = Mathf.Lerp(originalBitePosition.y, destination.y, verticalBlend);
        Vector3 correction = Vector3.ClampMagnitude(destination - originalBitePosition,
            maxContactAssistDistance * target.InteractionAssistMultiplier);
        float blend = Mathf.SmoothStep(0f, 1f, progress);
        Vector3 visualStart = targetVisual.parent.TransformPoint(originalVisualLocalPosition);
        targetVisual.position = visualStart + correction * blend;
        // Never eat remotely if an unexpected pose change puts the beak outside contact reach.
        if (progress >= 1f)
            LastContactError = Vector3.Distance(originalBitePosition + correction, beakEatPoint.position);
        return progress >= 1f && LastContactError <= 0.012f;
    }

    private void RestoreContactVisual()
    {
        if (targetVisual != null) targetVisual.localPosition = originalVisualLocalPosition;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.75f, 0.1f, 0.8f);
        Gizmos.DrawWireSphere(DetectionCenter, eatDetectionRadius);
        Gizmos.DrawWireSphere(DetectionCenter, eatDetectionRadius * MaximumAssistMultiplier);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * (eatForwardOffset + eatDetectionRadius));
        Vector3 previous = transform.position + Quaternion.AngleAxis(-maxTargetAngle, Vector3.up) * transform.forward * (eatForwardOffset + eatDetectionRadius);
        Gizmos.DrawLine(transform.position, previous);
        for (int i = 1; i <= 24; i++)
        {
            float angle = Mathf.Lerp(-maxTargetAngle, maxTargetAngle, i / 24f);
            Vector3 next = transform.position + Quaternion.AngleAxis(angle, Vector3.up) * transform.forward * (eatForwardOffset + eatDetectionRadius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
        Gizmos.DrawLine(transform.position, previous);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(PredictedContact, maxContactAssistDistance);
        if (beakEatPoint != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(beakEatPoint.position, 0.004f);
        }
        if (target != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(target.BitePosition, 0.018f);
            if (beakEatPoint != null) Gizmos.DrawLine(target.BitePosition, beakEatPoint.position);
        }
    }
}
