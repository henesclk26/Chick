using UnityEngine;

/// <summary>
/// Wading through shallow pond water: slows walking with depth, rings the surface at every step
/// and splashes when the bird lands in or runs into the water. Movement itself stays with
/// <see cref="ChickPlayerController"/>, which reads <see cref="SpeedMultiplier"/>.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(90)]
[RequireComponent(typeof(ChickPlayerController), typeof(CharacterController))]
public sealed class ChickWaterWading : MonoBehaviour
{
    [Tooltip("Walking speed multiplier once the water reaches the belly.")]
    [SerializeField, Range(.3f, 1f)] private float bellyDeepSpeed = .6f;
    [Tooltip("Belly-deep water for the chick (m); scales with the body for the grown chicken.")]
    [SerializeField, Min(.01f)] private float chickBellyDepth = .07f;
    [SerializeField, Min(.05f)] private float stepRippleInterval = .3f;
    [SerializeField, Min(.2f)] private float idleRippleInterval = 1.7f;

    private const float ChickHeight = .22f;
    private ChickPlayerController player;
    private CharacterController body;
    private FarmPond pond;
    private float depth;
    private float rippleTimer;
    private bool wasInWater;
    private bool wasGrounded = true;

    public bool InWater => pond != null && depth > .004f;
    public float Depth => depth;
    public FarmPond Pond => pond;
    public float SpeedMultiplier { get; private set; } = 1f;
    private float BodyScale => body.height / ChickHeight;

    private void Awake()
    {
        player = GetComponent<ChickPlayerController>();
        body = GetComponent<CharacterController>();
    }

    private void OnDisable()
    {
        SpeedMultiplier = 1f;
        pond = null;
        depth = 0f;
    }

    private void Update()
    {
        pond = FarmPond.Find(transform.position, out depth);
        bool grounded = body.isGrounded;
        bool inWater = InWater && grounded;
        float scale = BodyScale;
        float immersion = inWater ? Mathf.Clamp01(depth / (chickBellyDepth * scale)) : 0f;
        SpeedMultiplier = Mathf.Lerp(1f, bellyDeepSpeed, Mathf.SmoothStep(0f, 1f, immersion));
        if (pond == null)
        {
            wasInWater = false;
            wasGrounded = grounded;
            return;
        }

        Vector3 surface = new Vector3(transform.position.x, pond.SurfaceHeight + .002f, transform.position.z);
        Vector3 velocity = body.velocity;
        float planarSpeed = new Vector2(velocity.x, velocity.z).magnitude;
        // Landing from a jump or striding in quickly throws water up around the feet.
        if (inWater && (!wasGrounded || (!wasInWater && planarSpeed > .5f)))
        {
            float strength = Mathf.Clamp01(-velocity.y * .25f + planarSpeed * .3f + .35f);
            pond.Ripple(surface, 1f);
            pond.Splash(surface, Mathf.RoundToInt(Mathf.Lerp(6f, 16f, strength) * Mathf.Sqrt(scale)), Mathf.Lerp(.6f, 1.1f, strength), scale);
            rippleTimer = stepRippleInterval;
        }

        rippleTimer -= Time.deltaTime;
        if (inWater && rippleTimer <= 0f)
        {
            bool moving = planarSpeed > .05f;
            pond.Ripple(surface + transform.forward * (.03f * scale), moving ? Mathf.Lerp(.3f, .6f, immersion) : .18f);
            if (moving && player.IsRunning) pond.Splash(surface, 2, .45f, scale);
            rippleTimer = moving ? stepRippleInterval * Mathf.Lerp(1.2f, .8f, Mathf.Clamp01(planarSpeed / 1.4f)) : idleRippleInterval;
        }
        wasInWater = inWater;
        wasGrounded = grounded;
    }
}
