using UnityEngine;

// Feeds Chick/InteractiveFoliage. Slot 0 follows the player (lightly smoothed); the other slots sample that path at a fixed
// time interval, newest first, each carrying a life value that fades from 1 to 0 over recoverTime. The shader
// measures contact against the segments between slots, so grass and flowers lean away on contact and ease
// back up behind the player instead of snapping.
[DisallowMultipleComponent]
public sealed class FoliageBendDriver : MonoBehaviour
{
    private const int TrailCount = 16;   // FOLIAGE_BEND_TRAIL in FoliageBend.hlsl
    private static readonly int TrailId = Shader.PropertyToID("_FoliageBendTrail");
    private static readonly int ParamsId = Shader.PropertyToID("_FoliageBendParams");
    private static readonly int MotionId = Shader.PropertyToID("_FoliageBendMotion");

    [SerializeField, Tooltip("Influence radius as a multiple of the CharacterController radius.")]
    private float radiusScale = 2.4f;
    [SerializeField, Range(0f, 1.4f), Tooltip("Lean at full contact, in radians.")]
    private float maxTilt = 0.65f;
    [SerializeField, Tooltip("Rough radius of an unscaled plant mesh; scaled per plant in the shader.")]
    private float plantReach = 0.3f;
    [SerializeField, Tooltip("Seconds for a flattened plant to stand back up.")]
    private float recoverTime = 1.5f;
    [SerializeField, Tooltip("Plants whose root is further than this above/below the player are ignored.")]
    private float verticalRange = 0.75f;
    [SerializeField, Tooltip("Seconds the contact point trails the player; filters frame-to-frame jitter of the " +
                             "controller so plants lean in smoothly instead of twitching.")]
    private float contactSmoothing = 0.06f;

    private readonly Vector4[] trail = new Vector4[TrailCount];
    private readonly Vector3[] samples = new Vector3[TrailCount - 1];
    private readonly float[] sampleTimes = new float[TrailCount - 1];
    private CharacterController capsule;
    private Vector3 contactPosition;
    private Vector3 lastPosition;
    private Vector2 travel;
    private float nextSampleTime;
    private int newest;

    // Samples must span recoverTime, otherwise the oldest segment drops out before it has faded.
    private float SampleInterval => recoverTime / (TrailCount - 2);

    private void Awake() => capsule = GetComponent<CharacterController>();

    private void OnEnable()
    {
        contactPosition = transform.position;
        lastPosition = contactPosition;
        travel = new Vector2(transform.forward.x, transform.forward.z).normalized;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = lastPosition;
            sampleTimes[i] = float.NegativeInfinity;
        }
        nextSampleTime = 0f;
    }

    private void OnDisable()
    {
        Shader.SetGlobalVector(ParamsId, Vector4.zero);
    }

    private void LateUpdate()
    {
        Vector3 target = transform.position;
        float now = Time.time;

        if (new Vector2(target.x - contactPosition.x, target.z - contactPosition.z).sqrMagnitude > 4f)
        {
            // Teleport (respawn, save load): drop the old path so no streak is bent between the two spots.
            OnEnable();
        }
        else if (contactSmoothing > 0f)
            contactPosition = Vector3.Lerp(contactPosition, target, 1f - Mathf.Exp(-Time.deltaTime / contactSmoothing));
        else
            contactPosition = target;
        Vector3 position = contactPosition;

        var step = new Vector2(position.x - lastPosition.x, position.z - lastPosition.z);
        if (step.sqrMagnitude > 1e-8f)
            travel = Vector2.Lerp(travel, step.normalized, 1f - Mathf.Exp(-12f * Time.deltaTime)).normalized;
        lastPosition = position;

        if (now >= nextSampleTime)
        {
            newest = (newest + 1) % samples.Length;
            samples[newest] = position;
            sampleTimes[newest] = now;
            nextSampleTime = now + SampleInterval;
        }

        trail[0] = new Vector4(position.x, position.y, position.z, 1f);
        float cullRadius = 0f;
        for (int i = 1; i < TrailCount; i++)
        {
            int index = (newest - (i - 1) + samples.Length) % samples.Length;
            Vector3 p = samples[index];
            float life = Mathf.Clamp01(1f - (now - sampleTimes[index]) / recoverTime);
            trail[i] = new Vector4(p.x, p.y, p.z, life);
            if (life > 0f)
                cullRadius = Mathf.Max(cullRadius, new Vector2(p.x - position.x, p.z - position.z).magnitude);
        }

        float radius = ContactRadius();
        Shader.SetGlobalVectorArray(TrailId, trail);
        // Cull margin covers the contact radius plus the largest scaled plant reach.
        Shader.SetGlobalVector(ParamsId, new Vector4(maxTilt, verticalRange, cullRadius + radius + 1f, 1f));
        Shader.SetGlobalVector(MotionId, new Vector4(travel.x, travel.y, plantReach, radius));
    }

    // Follows the capsule, so the chicken form parts a wider path than the chick.
    private float ContactRadius()
    {
        if (capsule == null) return 0.3f;
        Vector3 scale = transform.lossyScale;
        return capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) * radiusScale;
    }
}
