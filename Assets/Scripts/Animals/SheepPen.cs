using UnityEngine;

/// <summary>
/// The walkable inside of the fenced sheep pasture: a rectangle (this transform is its centre, axis aligned)
/// minus round obstacles such as the shelter, trough and hay rack. Sheep pick targets here and are kept inside it.
/// </summary>
[DisallowMultipleComponent]
public sealed class SheepPen : MonoBehaviour
{
    [SerializeField] private Terrain terrain;
    [Tooltip("Usable half size inside the fence in world X and Z (m).")]
    [SerializeField] private Vector2 halfSize = new Vector2(10f, 8f);
    [Tooltip("x, z, radius in world space. Written by Tools/Chick/Sheep Pasture.")]
    [SerializeField] private Vector3[] obstacles = new Vector3[0];

    public Vector3 Center => transform.position;
    public Vector2 HalfSize => halfSize;

    public void Configure(Terrain ground, Vector2 usableHalfSize, Vector3[] obstacleCircles)
    {
        terrain = ground;
        halfSize = usableHalfSize;
        obstacles = obstacleCircles;
    }

    public float GroundHeight(Vector3 position) =>
        terrain != null ? terrain.SampleHeight(position) + terrain.transform.position.y : transform.position.y;

    /// <summary>Pushes a body of the given radius back inside the fence and out of obstacles.</summary>
    public Vector3 Constrain(Vector3 position, float bodyRadius)
    {
        Vector2 limit = Limit(bodyRadius);
        position.x = Mathf.Clamp(position.x, Center.x - limit.x, Center.x + limit.x);
        position.z = Mathf.Clamp(position.z, Center.z - limit.y, Center.z + limit.y);
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

    /// <summary>Gentle push away from nearby obstacles and the fence, for steering before contact.</summary>
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
        Vector2 edge = Limit(bodyRadius + lookAhead);
        Vector3 offset = position - Center;
        float soft = Mathf.Max(.1f, lookAhead);
        push.x -= Mathf.Sign(offset.x) * Mathf.Clamp01((Mathf.Abs(offset.x) - edge.x) / soft);
        push.z -= Mathf.Sign(offset.z) * Mathf.Clamp01((Mathf.Abs(offset.z) - edge.y) / soft);
        return push;
    }

    public bool IsFree(Vector3 position, float bodyRadius)
    {
        Vector2 limit = Limit(bodyRadius);
        if (Mathf.Abs(position.x - Center.x) > limit.x || Mathf.Abs(position.z - Center.z) > limit.y) return false;
        foreach (var o in obstacles)
            if (new Vector2(position.x - o.x, position.z - o.y).magnitude < o.z + bodyRadius) return false;
        return true;
    }

    /// <summary>A free spot near <paramref name="around"/> (or anywhere when no point is given).</summary>
    public Vector3 RandomPoint(float bodyRadius, Vector3? around = null, float spread = 3f)
    {
        Vector2 limit = Limit(bodyRadius);
        for (int attempt = 0; attempt < 16; attempt++)
        {
            Vector3 candidate;
            if (around.HasValue)
            {
                Vector2 offset = Random.insideUnitCircle * spread;
                candidate = new Vector3(around.Value.x + offset.x, 0f, around.Value.z + offset.y);
            }
            else
                candidate = new Vector3(Center.x + Random.Range(-limit.x, limit.x), 0f, Center.z + Random.Range(-limit.y, limit.y));
            if (IsFree(candidate, bodyRadius)) return candidate;
        }
        return Constrain(around ?? Center, bodyRadius);
    }

    private Vector2 Limit(float inset) => new Vector2(Mathf.Max(.1f, halfSize.x - inset), Mathf.Max(.1f, halfSize.y - inset));

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(.95f, .85f, .4f, .9f);
        Gizmos.DrawWireCube(Center, new Vector3(halfSize.x * 2f, .1f, halfSize.y * 2f));
        Gizmos.color = new Color(.9f, .4f, .3f, .8f);
        foreach (var o in obstacles)
            Gizmos.DrawWireSphere(new Vector3(o.x, GroundHeight(new Vector3(o.x, 0, o.y)), o.y), o.z);
    }
}
