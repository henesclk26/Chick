using UnityEngine;

/// <summary>
/// Running water that carries the bird with it: standing or walking in the stream, it is pushed along the way the water
/// runs (it can still wade upstream, slowly, and running beats it); over the edge of the waterfall it is pushed out and
/// falls with the water; where the fall lands, it is pushed out away from it. The flow is a line of points down the
/// middle of the water with its width and speed at each (written by Tools/Chick/Map/Waterfall).
/// </summary>
[DefaultExecutionOrder(100)]   // after the bird's own move
public sealed class FlowingWater : MonoBehaviour
{
    [SerializeField] private Vector3[] points = new Vector3[0];
    [SerializeField] private float[] widths = new float[0];
    [SerializeField] private float[] speeds = new float[0];
    [Tooltip("How much of the water's speed the bird is carried at.")]
    [SerializeField, Range(0f, 1f)] private float carry = .55f;
    [SerializeField, Min(0f)] private float maxPush = 2.2f;
    [Tooltip("The bird counts as in the water from this far below the surface to this far above it (m).")]
    [SerializeField] private Vector2 reachBelowAbove = new Vector2(.35f, .3f);
    [SerializeField] private Vector3 landing;
    [SerializeField, Min(0f)] private float landingRadius = 1.3f;
    [SerializeField, Min(0f)] private float landingPush = 1f;
    [SerializeField] private Vector3 landingAway = Vector3.left;

    private CharacterController bird;
    private float lookAgain;

    public void Configure(Vector3[] flow, float[] flowWidths, float[] flowSpeeds, Vector3 landingAt, Vector3 away)
    {
        points = flow; widths = flowWidths; speeds = flowSpeeds; landing = landingAt; landingAway = away.normalized;
    }

    private void Update()
    {
        if (bird == null || !bird.gameObject.activeInHierarchy)
        {
            if (Time.unscaledTime < lookAgain) return;
            lookAgain = Time.unscaledTime + 2f;
            var player = FindFirstObjectByType<ChickPlayerController>();
            bird = player != null ? player.GetComponent<CharacterController>() : null;
            if (bird == null) return;
        }
        if (!bird.enabled || points.Length < 2) return;

        Vector3 feet = bird.bounds.center + Vector3.down * bird.bounds.extents.y;
        Vector3 push = Vector3.zero;
        float best = float.PositiveInfinity;
        for (int i = 0; i + 1 < points.Length; i++)
        {
            Vector3 a = points[i], b = points[i + 1];
            Vector2 a2 = new Vector2(a.x, a.z), ab = new Vector2(b.x - a.x, b.z - a.z), p = new Vector2(feet.x, feet.z);
            float t = Mathf.Clamp01(Vector2.Dot(p - a2, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            float across = (a2 + ab * t - p).magnitude;
            float halfWidth = Mathf.Lerp(widths[i], widths[i + 1], t) * .5f + bird.radius * .5f;
            float surface = Mathf.Lerp(a.y, b.y, t);
            if (across > halfWidth || feet.y < surface - reachBelowAbove.x || feet.y > surface + reachBelowAbove.y || across >= best) continue;
            best = across;
            Vector3 along = new Vector3(ab.x, 0f, ab.y);
            // A segment that falls more than it runs is the fall: push straight out over it.
            float speed = Mathf.Min(Mathf.Lerp(speeds[i], speeds[i + 1], t) * carry, maxPush);
            push = (along.sqrMagnitude > 1e-6f ? along.normalized : landingAway) * speed;
        }
        Vector3 fromLanding = feet - landing; fromLanding.y = 0f;
        if (push == Vector3.zero && fromLanding.magnitude < landingRadius && Mathf.Abs(feet.y - landing.y) < .5f)
        {
            Vector3 away = (fromLanding.sqrMagnitude > .01f ? fromLanding.normalized : landingAway) + landingAway * .5f;
            push = away.normalized * landingPush * (1f - fromLanding.magnitude / landingRadius * .5f);
        }
        if (push == Vector3.zero) return;
        // A little down as well while on the ground, so the bird stays grounded as it is carried.
        bird.Move(push * Time.deltaTime + (bird.isGrounded ? Vector3.down * .02f : Vector3.zero));
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        for (int i = 0; i + 1 < points.Length; i++) Gizmos.DrawLine(points[i], points[i + 1]);
        Gizmos.DrawWireSphere(landing, landingRadius);
    }
}
