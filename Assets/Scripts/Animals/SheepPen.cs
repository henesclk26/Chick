using UnityEngine;

/// <summary>
/// The walkable inside of the sheep fold: a circle (this transform is its centre) minus round obstacles
/// such as the shelter, trough and hay rack. Sheep pick targets here and are kept inside it.
/// </summary>
[DisallowMultipleComponent]
public sealed class SheepPen : MonoBehaviour
{
    [SerializeField] private Terrain terrain;
    [Tooltip("Usable radius inside the wall (m).")]
    [SerializeField, Min(1f)] private float radius = 6.2f;
    [Tooltip("x, z, radius in world space. Written by Tools/Chick/Sheep Fold.")]
    [SerializeField] private Vector3[] obstacles = new Vector3[0];

    public Vector3 Center => transform.position;
    public float Radius => radius;

    public void Configure(Terrain ground, float usableRadius, Vector3[] obstacleCircles)
    {
        terrain = ground;
        radius = usableRadius;
        obstacles = obstacleCircles;
    }

    public float GroundHeight(Vector3 position) =>
        terrain != null ? terrain.SampleHeight(position) + terrain.transform.position.y : transform.position.y;

    /// <summary>Pushes a body of the given radius back inside the wall and out of obstacles.</summary>
    public Vector3 Constrain(Vector3 position, float bodyRadius)
    {
        Vector3 offset = position - Center;
        offset.y = 0f;
        float limit = Mathf.Max(.1f, radius - bodyRadius);
        if (offset.sqrMagnitude > limit * limit)
        {
            Vector3 inside = Center + offset.normalized * limit;
            position.x = inside.x;
            position.z = inside.z;
        }
        foreach (var o in obstacles)
        {
            var away = new Vector2(position.x - o.x, position.z - o.y);
            float min = o.z + bodyRadius;
            if (away.sqrMagnitude >= min * min) continue;
            if (away.sqrMagnitude < 1e-6f) away = Vector2.right;
            away = away.normalized * min;
            position.x = o.x + away.x;
            position.z = o.y + away.y;
        }
        return position;
    }

    /// <summary>Gentle push away from nearby obstacles and the wall, for steering before contact.</summary>
    public Vector3 Avoidance(Vector3 position, float bodyRadius, float lookAhead)
    {
        Vector3 push = Vector3.zero;
        foreach (var o in obstacles)
        {
            var away = new Vector3(position.x - o.x, 0f, position.z - o.y);
            float reach = o.z + bodyRadius + lookAhead;
            float distance = away.magnitude;
            if (distance >= reach || distance < 1e-4f) continue;
            push += away / distance * (1f - distance / reach);
        }
        Vector3 fromCenter = position - Center;
        fromCenter.y = 0f;
        float edge = radius - bodyRadius - lookAhead;
        if (fromCenter.magnitude > edge && fromCenter.sqrMagnitude > 1e-4f)
            push -= fromCenter.normalized * Mathf.Clamp01((fromCenter.magnitude - edge) / Mathf.Max(.1f, lookAhead));
        return push;
    }

    public bool IsFree(Vector3 position, float bodyRadius)
    {
        Vector3 offset = position - Center;
        offset.y = 0f;
        if (offset.magnitude > radius - bodyRadius) return false;
        foreach (var o in obstacles)
            if (new Vector2(position.x - o.x, position.z - o.y).magnitude < o.z + bodyRadius) return false;
        return true;
    }

    /// <summary>A free spot near <paramref name="around"/> (or anywhere when no point is given).</summary>
    public Vector3 RandomPoint(float bodyRadius, Vector3? around = null, float spread = 3f)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * (around.HasValue ? spread : radius - bodyRadius);
            Vector3 origin = around ?? Center;
            var candidate = new Vector3(origin.x + offset.x, 0f, origin.z + offset.y);
            if (IsFree(candidate, bodyRadius)) return candidate;
        }
        return Constrain(around ?? Center, bodyRadius);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(.95f, .85f, .4f, .9f);
        const int steps = 48;
        for (int i = 0; i < steps; i++)
        {
            float a = i * Mathf.PI * 2f / steps, b = (i + 1) * Mathf.PI * 2f / steps;
            Gizmos.DrawLine(Center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius,
                Center + new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * radius);
        }
        Gizmos.color = new Color(.9f, .4f, .3f, .8f);
        foreach (var o in obstacles)
            Gizmos.DrawWireSphere(new Vector3(o.x, GroundHeight(new Vector3(o.x, 0, o.y)), o.y), o.z);
    }
}
