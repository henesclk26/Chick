using UnityEngine;

// A frog sitting on a lily pad: its throat swells and falls as it breathes, now and then it shuffles round with a
// little hop, and it turns to watch the chick when the chick comes close. It stays on its pad (nothing eats it, it
// does not flee). Only the visual child moves up and down; the root (with the collider) just turns on the spot.
[DisallowMultipleComponent]
public sealed class FrogIdle : MonoBehaviour
{
    [SerializeField, Tooltip("The model; breathes and hops. Defaults to the first child.")]
    private Transform visual;
    [SerializeField, Tooltip("Seconds per breath.")]
    private float breathPeriod = 1.6f;
    [SerializeField, Range(0f, 0.15f), Tooltip("How much the throat swells.")]
    private float breathAmount = 0.05f;
    [SerializeField, Tooltip("Watches the chick inside this distance (m).")]
    private float watchRadius = 1.6f;
    [SerializeField, Tooltip("Turning speed, degrees per second.")]
    private float turnSpeed = 160f;
    [SerializeField, Tooltip("Seconds between shuffles round when nothing is going on.")]
    private Vector2 shuffleEvery = new Vector2(5f, 12f);
    [SerializeField, Tooltip("Height of the shuffle hop (m).")]
    private float hopHeight = 0.03f;

    private Transform chick;
    private Vector3 restScale, restPosition;
    private float targetYaw, nextShuffle, hop, breathPhase;

    private void Start()
    {
        if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
        if (visual != null) { restScale = visual.localScale; restPosition = visual.localPosition; }
        var player = FindFirstObjectByType<ChickPlayerController>();
        if (player != null) chick = player.transform;
        targetYaw = transform.eulerAngles.y;
        breathPhase = Random.value * 10f;                 // the two frogs do not breathe in step
        nextShuffle = Time.time + Random.Range(shuffleEvery.x, shuffleEvery.y);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        float yaw = transform.eulerAngles.y;
        if (chick != null && FlatDistance(chick.position) < watchRadius)
        {
            Vector3 to = chick.position - transform.position;
            float wanted = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            if (Mathf.Abs(Mathf.DeltaAngle(targetYaw, wanted)) > 25f) { targetYaw = wanted; hop = Mathf.Max(hop, 0.6f); }
        }
        else if (Time.time >= nextShuffle)
        {
            targetYaw = yaw + Random.Range(-70f, 70f);
            hop = 1f;
            nextShuffle = Time.time + Random.Range(shuffleEvery.x, shuffleEvery.y);
        }
        transform.rotation = Quaternion.Euler(0f, Mathf.MoveTowardsAngle(yaw, targetYaw, turnSpeed * dt), 0f);

        if (visual == null) return;
        hop = Mathf.Max(0f, hop - dt * 3.5f);
        float lift = Mathf.Sin(hop * Mathf.PI) * hopHeight;
        // Breathing: the throat swells (a little wider and taller), sharper in than out.
        float b = Mathf.Pow(Mathf.Max(0f, Mathf.Sin((Time.time + breathPhase) * Mathf.PI * 2f / breathPeriod)), 2f) * breathAmount;
        visual.localScale = new Vector3(restScale.x * (1f + b * 0.6f), restScale.y * (1f + b), restScale.z * (1f + b * 0.3f));
        visual.localPosition = restPosition + Vector3.up * lift;
    }

    private float FlatDistance(Vector3 p)
    {
        Vector3 d = p - transform.position;
        d.y = 0f;
        return d.magnitude;
    }
}
