using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The flowers' answer to the player brushing past: each flower near the bird leans a few degrees away from it and
/// follows through a soft spring, so it eases over, sways a little and settles, and when the bird walks around it the
/// lean turns with a lag instead of flipping. The grass keeps its own shader bend (FoliageBend); the flower materials
/// listed here have that bend switched off (their _DetailNormalMapScale share is 0) and are leaned here instead, as a
/// whole plant around its root. Only flowers near the bird, or still swaying, are touched each frame.
/// Presentation only.
/// </summary>
[DefaultExecutionOrder(300)]
public sealed class FlowerSway : MonoBehaviour
{
    [SerializeField, Tooltip("Materials of the flowers that sway here (their shader bend share should be 0).")]
    private Material[] flowerMaterials;
    [SerializeField, Range(0f, 30f), Tooltip("Lean at full contact, in degrees.")]
    private float maxLean = 14f;
    [SerializeField, Min(.1f), Tooltip("Contact reach as a multiple of the player's CharacterController radius.")]
    private float radiusScale = 2.4f;
    [SerializeField, Range(.3f, 4f), Tooltip("How fast a flower swings (Hz): lower is slower and softer.")]
    private float swayFrequency = .6f;
    [SerializeField, Range(.1f, 1f), Tooltip("Damping: below 1 a flower sways past its lean a little before it settles.")]
    private float swayDamping = .4f;
    [SerializeField, Min(.1f), Tooltip("Flowers whose root is further than this above/below the player are ignored.")]
    private float verticalRange = .75f;

    private sealed class Flower
    {
        public Transform root;
        public Quaternion rest;
        public float reach;
        public Vector2 lean, velocity;   // radians, towards the lean direction (world XZ)
        public bool moving;
    }

    private readonly List<Flower> flowers = new List<Flower>();
    private Transform player;
    private CharacterController capsule;
    private float nextPlayerLookup;

    private void Start()
    {
        var materials = new HashSet<Material>();
        if (flowerMaterials != null) foreach (var m in flowerMaterials) if (m != null) materials.Add(m);
        var roots = new HashSet<Transform>();
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!materials.Contains(r.sharedMaterial)) continue;
            // a LOD group's meshes lean with the group
            Transform root = r.transform.parent != null && r.transform.parent.GetComponent<LODGroup>() != null ? r.transform.parent : r.transform;
            if (!roots.Add(root)) continue;
            var size = r.bounds.size;
            flowers.Add(new Flower { root = root, rest = root.rotation, reach = .5f*Mathf.Max(size.x, size.z) });
        }
    }

    private void OnDisable()
    {
        foreach (var f in flowers)
        {
            if (f.root == null || !f.moving) continue;
            f.root.rotation = f.rest;
            f.lean = f.velocity = Vector2.zero;
            f.moving = false;
        }
    }

    private void LateUpdate()
    {
        if ((player == null || !player.gameObject.activeInHierarchy) && Time.unscaledTime >= nextPlayerLookup)
        {
            var driver = FindFirstObjectByType<FoliageBendDriver>();
            player = driver != null ? driver.transform : null;
            capsule = driver != null ? driver.GetComponent<CharacterController>() : null;
            nextPlayerLookup = Time.unscaledTime + 1f;
        }
        float dt = Mathf.Min(Time.deltaTime, 1f/30f);
        if (dt <= 0f || flowers.Count == 0) return;

        Vector3 at = player != null ? player.position : new Vector3(float.MaxValue, 0, 0);
        float radius = .3f;
        if (capsule != null) radius = capsule.radius*Mathf.Max(Mathf.Abs(player.lossyScale.x), Mathf.Abs(player.lossyScale.z))*radiusScale;
        float maxRad = maxLean*Mathf.Deg2Rad;
        float omega = 2f*Mathf.PI*swayFrequency, stiffness = omega*omega, damping = 2f*swayDamping*omega;

        foreach (var f in flowers)
        {
            if (f.root == null) continue;
            Vector3 p = f.root.position;
            float dx = p.x - at.x, dz = p.z - at.z, d2 = dx*dx + dz*dz, touch = radius + f.reach;
            Vector2 target = Vector2.zero;
            if (d2 < touch*touch && Mathf.Abs(p.y - at.y) < verticalRange)
            {
                float d = Mathf.Sqrt(d2), contact = 1f - d/touch;
                contact = contact*contact*(3f - 2f*contact);
                // fading out towards the centre, so a bird over the flower tips it through upright, not across
                contact *= Mathf.Clamp01(d/(touch*.35f));
                if (d > 1e-4f) target = new Vector2(dx, dz)/d*(maxRad*contact);
            }
            if (!f.moving && target == Vector2.zero) continue;

            // a damped spring towards the target lean
            Vector2 accel = (target - f.lean)*stiffness - f.velocity*damping;
            f.velocity += accel*dt;
            f.lean += f.velocity*dt;
            if (f.lean.sqrMagnitude > maxRad*maxRad*2f) f.lean = f.lean.normalized*maxRad*1.41f;

            if (target == Vector2.zero && f.lean.sqrMagnitude < 1e-7f && f.velocity.sqrMagnitude < 1e-6f)
            {
                f.lean = f.velocity = Vector2.zero;
                f.root.rotation = f.rest;
                f.moving = false;
                continue;
            }
            f.moving = true;
            float angle = f.lean.magnitude;
            Vector3 axis = Vector3.Cross(Vector3.up, new Vector3(f.lean.x, 0f, f.lean.y)/Mathf.Max(angle, 1e-6f));
            f.root.rotation = Quaternion.AngleAxis(angle*Mathf.Rad2Deg, axis)*f.rest;
        }
    }
}
