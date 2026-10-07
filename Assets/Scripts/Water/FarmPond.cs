using UnityEngine;

/// <summary>
/// Shallow farm pond: a level water surface (this transform's height) over a carved terrain basin.
/// Ripples, splashes and the wading/drinking lookups come from <see cref="FarmWaterBody"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class FarmPond : FarmWaterBody
{
    [SerializeField] private Terrain terrain;
    [Tooltip("Shoreline in world XZ as a closed loop. Written by Tools/Chick/Pond.")]
    [SerializeField] private Vector2[] shoreline = new Vector2[0];
    [Tooltip("Water may reach this far past the shoreline where the bank dips below the surface.")]
    [SerializeField, Min(0f)] private float shoreMargin = .3f;
    [Tooltip("Scene objects the pond covers; the authoring tool re-enables them when the pond is removed.")]
    [SerializeField] private GameObject[] hiddenForPond = new GameObject[0];

    private Rect area;

    public GameObject[] HiddenForPond => hiddenForPond;

    public override bool TryGetDepth(Vector3 position, out float depth)
    {
        depth = 0f;
        if (terrain == null || shoreline.Length < 3 || !area.Contains(new Vector2(position.x, position.z))) return false;
        if (!IsWithinShore(position.x, position.z)) return false;
        depth = SurfaceHeight - (terrain.SampleHeight(position) + terrain.transform.position.y);
        return depth > 0f;
    }

    public void Configure(Terrain basinTerrain, Vector2[] shore, float margin, Material droplet, GameObject[] hidden)
    {
        terrain = basinTerrain;
        shoreline = shore;
        shoreMargin = margin;
        dropletMaterial = droplet;
        hiddenForPond = hidden;
        RebuildArea();
    }

    protected override void OnEnable()
    {
        RebuildArea();
        base.OnEnable();
    }

    private void RebuildArea()
    {
        if (shoreline == null || shoreline.Length == 0) { area = Rect.zero; return; }
        Vector2 min = shoreline[0], max = shoreline[0];
        foreach (var p in shoreline) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        area = Rect.MinMaxRect(min.x - shoreMargin, min.y - shoreMargin, max.x + shoreMargin, max.y + shoreMargin);
    }

    private bool IsWithinShore(float x, float z)
    {
        bool inside = false;
        float nearest = float.PositiveInfinity;
        var p = new Vector2(x, z);
        for (int i = 0, j = shoreline.Length - 1; i < shoreline.Length; j = i++)
        {
            Vector2 a = shoreline[i], b = shoreline[j];
            if ((a.y > z) != (b.y > z) && x < (b.x - a.x) * (z - a.y) / (b.y - a.y) + a.x) inside = !inside;
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            nearest = Mathf.Min(nearest, (a + ab * t - p).sqrMagnitude);
        }
        return inside || nearest <= shoreMargin * shoreMargin;
    }
}
