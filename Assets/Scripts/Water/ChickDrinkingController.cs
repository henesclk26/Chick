using UnityEngine;

/// <summary>
/// Hold the eat button at the water's edge (or standing in it) to drink, the way real chicks do:
/// dip the beak (the eat clip's peck), scoop for a moment, then tip the head back to swallow.
/// The swallow is a procedural neck/head tilt layered over the Animator, so it works on both the
/// chick and the grown chicken rig. <see cref="ChickPlayerController"/> starts it and locks movement.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(95)]
[RequireComponent(typeof(ChickPlayerController), typeof(ChickEatingController))]
public sealed class ChickDrinkingController : MonoBehaviour
{
    [Header("Dip")]
    [SerializeField, Range(.6f, 1.6f)] private float dipSpeed = 1.15f;
    [Tooltip("How long the beak stays in the water scooping.")]
    [SerializeField, Min(0f)] private float scoopSeconds = .3f;
    [Tooltip("Furthest the water surface may sit below the beak's ground contact (m, chick size).")]
    [SerializeField, Min(0f)] private float maxReachBelow = .035f;
    [Header("Swallow")]
    [SerializeField, Min(.2f)] private float swallowSeconds = .65f;
    [SerializeField, Range(0f, 70f)] private float swallowTilt = 38f;
    [SerializeField, Min(0f)] private float pauseBetweenSips = .08f;

    private enum Phase { Ready, Dip, Scoop, Rise, Swallow }
    private const float ChickHeight = .22f;
    private static readonly int EatSpeed = Animator.StringToHash("EatPlaybackSpeed");
    private ChickPlayerController player;
    private ChickEatingController eater;
    private CharacterController body;
    private Phase phase;
    private float phaseTime;
    private bool enteredEat;
    private FarmPond pond;
    private Vector3 sipPoint;
    private Animator boundAnimator;
    private Transform chest, neck, head;
    private float tilt;
    // 0..1: how strongly the beak is guided onto the water surface this frame.
    private float reach;

    public bool IsBusy => phase != Phase.Ready;
    /// <summary>Base-layer state the player controller should hold while drinking.</summary>
    public string BaseState => phase == Phase.Dip || phase == Phase.Scoop || phase == Phase.Rise ? "eat" : "idle";
    public int SipCount { get; private set; }
    public static event System.Action<ChickDrinkingController> Sipped;

    private float BodyScale => body.height / ChickHeight;

    private void Awake()
    {
        player = GetComponent<ChickPlayerController>();
        eater = GetComponent<ChickEatingController>();
        body = GetComponent<CharacterController>();
    }

    private void OnDisable() => Cancel();

    /// <summary>Whether the beak can reach pond water from where the bird stands.</summary>
    public bool CanDrinkHere()
    {
        if (!isActiveAndEnabled) return false;
        float scale = BodyScale;
        Vector3 contact = eater.PredictedContactPoint;
        Vector3 ahead = contact + transform.forward * (.06f * scale);
        return Probe(ahead, scale) || Probe(contact, scale);
    }

    private bool Probe(Vector3 point, float scale)
    {
        var found = FarmPond.Find(point, out float depth);
        if (found == null || depth < .004f || found.SurfaceHeight < point.y - maxReachBelow * scale) return false;
        pond = found;
        sipPoint = new Vector3(point.x, found.SurfaceHeight + .002f, point.z);
        return true;
    }

    public bool TryBegin()
    {
        if (IsBusy || !CanDrinkHere()) return false;
        BeginDip();
        return true;
    }

    public void Cancel()
    {
        phase = Phase.Ready;
        tilt = 0f;
        reach = 0f;
    }

    private void BeginDip()
    {
        var animator = player.ActiveAnimator;
        animator.SetFloat(EatSpeed, dipSpeed);
        animator.CrossFadeInFixedTime("eat", .08f, 0, 0f);
        enteredEat = false;
        phase = Phase.Dip;
        phaseTime = 0f;
    }

    private void Update()
    {
        if (!IsBusy) return;
        var animator = player.ActiveAnimator;
        if (animator == null || !player.isActiveAndEnabled || player.GrowthControlsLocked || !body.isGrounded)
        {
            if (animator != null) animator.SetFloat(EatSpeed, dipSpeed);
            Cancel();
            return;
        }
        phaseTime += Time.deltaTime;
        var state = animator.GetCurrentAnimatorStateInfo(0);
        bool inEat = state.IsName("eat");
        enteredEat |= inEat;
        float scale = BodyScale;

        switch (phase)
        {
            case Phase.Dip:
                reach = inEat ? Mathf.SmoothStep(0f, 1f, state.normalizedTime / Mathf.Max(.01f, eater.ImpactNormalizedTime)) : 0f;
                if (!enteredEat && phaseTime > .3f) { Cancel(); return; }
                if (inEat && !animator.IsInTransition(0) && state.normalizedTime >= eater.ImpactNormalizedTime)
                {
                    // Beak touches the water: hold the pose while it scoops.
                    animator.SetFloat(EatSpeed, .04f);
                    if (pond != null)
                    {
                        pond.Ripple(sipPoint, 1f);
                        pond.Splash(sipPoint, 3, .35f, scale);
                    }
                    phase = Phase.Scoop;
                    phaseTime = 0f;
                }
                break;
            case Phase.Scoop:
                reach = 1f;
                if (phaseTime >= scoopSeconds)
                {
                    animator.SetFloat(EatSpeed, dipSpeed);
                    if (pond != null) pond.Ripple(sipPoint, .45f);
                    phase = Phase.Rise;
                    phaseTime = 0f;
                }
                break;
            case Phase.Rise:
                reach = 1f - Mathf.SmoothStep(0f, 1f, phaseTime / .18f);
                if (!inEat || state.normalizedTime >= eater.RecoveryNormalizedTime || phaseTime > .6f)
                {
                    SipCount++;
                    Sipped?.Invoke(this);
                    phase = Phase.Swallow;
                    phaseTime = 0f;
                }
                break;
            case Phase.Swallow:
                reach = 0f;
                if (pond != null && head != null && phaseTime < swallowSeconds * .5f && Random.value < Time.deltaTime * 9f)
                    pond.Drip(BeakTip(), scale);
                if (phaseTime >= swallowSeconds + pauseBetweenSips)
                {
                    // Keep drinking while the button stays held and the water is still in reach.
                    if (eater.EatHeld && CanDrinkHere()) BeginDip();
                    else Cancel();
                }
                break;
        }
    }

    // Runs after the Animator has posed the rig. The eat clip aims the beak at the ground, so lean the
    // upper body until the beak just breaks the water surface (also lifting it when standing deep),
    // then tip the head back to swallow.
    private void LateUpdate()
    {
        tilt = phase == Phase.Swallow ? SwallowCurve(phaseTime / swallowSeconds) : 0f;
        if (tilt <= .0001f && reach <= .0001f) return;
        BindRig();
        if (neck == null || head == null) return;
        Vector3 axis = transform.right;
        var beak = eater.BeakEatPoint;
        if (reach > .0001f && chest != null && beak != null && pond != null)
        {
            Vector3 offset = beak.position - chest.position;
            float along = Vector3.Dot(offset, transform.forward);
            float length = Mathf.Sqrt(along * along + offset.y * offset.y);
            if (along > .001f && length > .001f)
            {
                float targetHeight = pond.SurfaceHeight - .006f * BodyScale - chest.position.y;
                float now = Mathf.Atan2(offset.y, along);
                float wanted = Mathf.Asin(Mathf.Clamp(targetHeight / length, -1f, 1f));
                float lean = Mathf.Clamp((now - wanted) * Mathf.Rad2Deg, -45f, 35f) * reach;
                chest.rotation = Quaternion.AngleAxis(lean, axis) * chest.rotation;
            }
        }
        if (tilt <= .0001f) return;
        float angle = swallowTilt * tilt;
        if (chest != null) chest.rotation = Quaternion.AngleAxis(-angle * .2f, axis) * chest.rotation;
        neck.rotation = Quaternion.AngleAxis(-angle * .45f, axis) * neck.rotation;
        head.rotation = Quaternion.AngleAxis(-angle * .55f, axis) * head.rotation;
    }

    // Quick lift, two small gulps at the top, then an easy return to idle.
    private static float SwallowCurve(float t)
    {
        if (t <= 0f || t >= 1f) return 0f;
        float up = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .28f, t));
        float down = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.68f, 1f, t));
        float gulp = Mathf.InverseLerp(.28f, .68f, t);
        float bob = gulp > 0f && gulp < 1f ? .12f * Mathf.Sin(gulp * Mathf.PI * 4f) : 0f;
        return Mathf.Clamp01(up * down - bob * up * down);
    }

    private Vector3 BeakTip()
    {
        var beak = eater.BeakEatPoint;
        return beak != null ? beak.position : head.position + transform.forward * .03f;
    }

    private void BindRig()
    {
        var animator = player.ActiveAnimator;
        if (animator == boundAnimator && head != null) return;
        boundAnimator = animator;
        chest = neck = head = null;
        if (animator == null) return;
        neck = FindBone(animator.transform, "neck");
        head = neck != null ? FindBone(neck, "head") : null;
        chest = neck != null ? neck.parent : null;
    }

    private static Transform FindBone(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindBone(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
