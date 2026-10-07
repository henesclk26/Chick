using UnityEngine;

/// <summary>
/// The water in a trough. Sits on the water surface object: the surface is this transform's height and the
/// water fills a box around it in local XZ. A chick hops onto the rim to drink (a grown chicken reaches over
/// it from the ground) and can splash about inside. The pump spout drips into it now and then.
/// </summary>
[DisallowMultipleComponent]
public sealed class FarmWaterTrough : FarmWaterBody
{
    [Tooltip("Half size of the water surface in local X and Z (m).")]
    [SerializeField] private Vector2 halfSize = new Vector2(.8f, .2f);
    [Tooltip("Water depth over the trough floor (m).")]
    [SerializeField, Min(.01f)] private float depth = .075f;
    [Tooltip("Height of the rim above the water surface (m).")]
    [SerializeField, Min(0f)] private float rimAbove = .03f;
    [Tooltip("How far above its feet a bird reaches over an edge, per unit of chick size (m).")]
    [SerializeField, Min(0f)] private float reachOverRim = .11f;
    [Header("Spout")]
    [Tooltip("Optional pump spout tip; drops fall from here into the water.")]
    [SerializeField] private Transform spout;
    [SerializeField] private Vector2 dripInterval = new Vector2(.7f, 1.6f);

    private float dripTimer;
    private float rippleAt = -1f;
    private Vector3 rippleSpot;

    public void Configure(Vector2 waterHalfSize, float waterDepth, float rimHeightAboveWater, Transform spoutTip,
        Material droplet)
    {
        halfSize = waterHalfSize;
        depth = waterDepth;
        rimAbove = rimHeightAboveWater;
        spout = spoutTip;
        dropletMaterial = droplet;
    }

    public override bool TryGetDepth(Vector3 position, out float waterDepth)
    {
        Vector3 local = transform.InverseTransformPoint(position);
        bool inside = Mathf.Abs(local.x) <= halfSize.x && Mathf.Abs(local.z) <= halfSize.y;
        waterDepth = inside ? depth : 0f;
        return inside;
    }

    public override bool CanReachFrom(float feetHeight, float bodyScale) =>
        SurfaceHeight + rimAbove - feetHeight <= reachOverRim * bodyScale;

    protected override void OnEnable()
    {
        base.OnEnable();
        dripTimer = Random.Range(dripInterval.x, dripInterval.y);
    }

    private void Update()
    {
        if (spout == null) return;
        float fall = Mathf.Max(0f, spout.position.y - SurfaceHeight);
        if ((dripTimer -= Time.deltaTime) <= 0f)
        {
            dripTimer = Random.Range(dripInterval.x, dripInterval.y);
            // Lifetime ends as the drop reaches the water; the ring starts when it lands.
            float landing = Mathf.Sqrt(2f * fall / Mathf.Abs(Physics.gravity.y));
            Drip(spout.position, 1.3f, landing);
            rippleAt = landing;
            rippleSpot = new Vector3(spout.position.x, SurfaceHeight, spout.position.z);
        }
        if (rippleAt >= 0f && (rippleAt -= Time.deltaTime) < 0f) Ripple(rippleSpot, .35f);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(.3f, .7f, 1f, .8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(new Vector3(0f, -depth / 2f, 0f), new Vector3(halfSize.x * 2f, depth, halfSize.y * 2f));
    }
}
