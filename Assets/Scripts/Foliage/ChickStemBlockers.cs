using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// For the chick, the tall flowers with a single thick stalk (sunflowers, hyacinths, roses, poppies, daisies) are poles it bumps
/// into; the grown chicken just pushes through them. Each such flower gets a thin capsule around its stalk, as thick as
/// the stalk itself (measured from the meshes), so a chick still slips between two flowers. The capsules only reach the
/// lower part of the flower: well above the chick, but below the blossom, where the butterflies look for a perch; and
/// they start a little above the ground, so the controller never takes their rounded foot for a step to climb, and
/// while the chick is in the air the stalks around it let it through, so it can never land or catch on one.
/// They are on Ignore Raycast, so the camera (CameraBlocker only) never meets them, and are switched on only while the
/// player is a chick. Presentation and movement only; the flowers themselves are not changed.
/// </summary>
[DefaultExecutionOrder(260)]
public sealed class ChickStemBlockers : MonoBehaviour
{
    [System.Serializable]
    private struct Stalk
    {
        [Tooltip("Mesh name of the flower, or the start of its object's name (static batching renames the mesh).")] public string mesh;
        [Tooltip("Stalk radius in mesh units (scaled with the flower).")] public float radius;
        [Tooltip("Stalk centre in mesh units (x, z).")] public Vector2 centre;
    }

    [SerializeField] private Stalk[] stalks =
    {
        new Stalk { mesh = "SM_Prop_Sunflower_01", radius = .025f, centre = new Vector2(.025f, -.03f) },
        new Stalk { mesh = "SM_Prop_Sunflower_02", radius = .03f, centre = Vector2.zero },
        new Stalk { mesh = "Hyacinth_PastellBlue_Big", radius = .045f, centre = new Vector2(.036f, -.03f) },
        new Stalk { mesh = "Rose_Red_Open", radius = .022f, centre = Vector2.zero },
        new Stalk { mesh = "Rose_Black_Open", radius = .022f, centre = Vector2.zero },
        new Stalk { mesh = "Flower_Poppy", radius = .03f, centre = new Vector2(.025f, -.006f) },
        new Stalk { mesh = "Daisy_Single", radius = .035f, centre = new Vector2(.03f, .02f) },
    };
    [SerializeField, Range(.2f, .5f), Tooltip("Share of the flower's height the stalk capsule covers, from the ground.")]
    private float heightShare = .45f;
    [SerializeField, Min(.005f), Tooltip("Thinnest a stalk capsule gets, in metres.")]
    private float minimumRadius = .012f;
    [SerializeField, Min(0f), Tooltip("Gap under the capsule, in metres: above the birds' step height, so the chick bumps " +
                                      "into the stalk instead of stepping up onto its rounded foot.")]
    private float liftOffGround = .06f;

    private readonly List<Collider> colliders = new List<Collider>();
    private readonly List<Vector3> tops = new List<Vector3>();   // each capsule's top, world
    private PlayerGrowthController growth;
    private bool on;
    private const float Near = .45f;   // a stalk this close (m, sideways) to the bird can be under it
    private const float Airborne = .05f;   // feet this far (m) above the ground: the chick is in the air

    private void Start()
    {
        int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        var terrain = Terrain.activeTerrain;
        foreach (var filter in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (filter.sharedMesh == null || renderer == null || filter.name.Contains("LOD1") || filter.name.Contains("LOD2")) continue;
            if (!Match(filter, out var stalk)) continue;

            // measured on the renderer's world bounds (a statically batched mesh no longer has its own)
            var t = filter.transform;
            var b = renderer.bounds;
            float scale = Mathf.Max(Mathf.Abs(t.lossyScale.x), 1e-4f);
            Vector3 foot = t.TransformPoint(new Vector3(stalk.centre.x, 0f, stalk.centre.y));
            // the lift counts from the ground itself: a flower model often sinks a little into it
            float ground = terrain != null ? terrain.SampleHeight(foot) + terrain.transform.position.y : b.min.y;
            float bottom = Mathf.Max(b.min.y, ground) + liftOffGround, top = b.min.y + b.size.y*heightShare;
            float height = top - bottom, radius = Mathf.Max(stalk.radius*scale, minimumRadius);
            if (height < radius*2f) continue;
            foot.y = bottom + height*.5f;

            // Upright in the world and not under the flower: the flowers lean as the bird brushes them (FlowerSway), and
            // a capsule leaning with them turns its side up towards the bird, which then gets pushed up and climbs it.
            var go = new GameObject("Chick Stalk Blocker") { layer = ignoreRaycast };
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(foot, Quaternion.identity);
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = radius;
            capsule.height = height;
            capsule.center = Vector3.zero;
            capsule.enabled = false;
            colliders.Add(capsule);
            tops.Add(new Vector3(foot.x, top, foot.z));
        }
        on = false;
        Apply(IsChick());
    }

    private bool Match(MeshFilter filter, out Stalk found)
    {
        string mesh = filter.sharedMesh.name, name = filter.name;
        foreach (var s in stalks)
        {
            if (string.IsNullOrEmpty(s.mesh) || (mesh != s.mesh && !name.StartsWith(s.mesh))) continue;
            found = s;
            return true;
        }
        found = default;
        return false;
    }

    private void Update()
    {
        bool chick = IsChick();
        if (chick != on) Apply(chick);
        if (!chick) return;

        // A stalk is only something to bump into while walking: as soon as the chick is off the ground (a jump), the
        // stalks around it let it through, so it can never land on one or catch on the rounded top beside it; they
        // close again once it is back on the ground (one it then stands in pushes it aside).
        Vector3 feet = growth.transform.position;
        var terrain = Terrain.activeTerrain;
        bool airborne = terrain != null && feet.y - (terrain.SampleHeight(feet) + terrain.transform.position.y) > Airborne;
        for (int i = 0; i < colliders.Count; i++)
        {
            var c = colliders[i];
            if (c == null) continue;
            Vector3 top = tops[i];
            float dx = top.x - feet.x, dz = top.z - feet.z;
            bool near = dx*dx + dz*dz < Near*Near;
            bool through = near && (airborne || feet.y > top.y - .03f);
            if (c.enabled == through) c.enabled = !through;
        }
    }

    private bool IsChick()
    {
        if (growth == null) growth = FindFirstObjectByType<PlayerGrowthController>();
        return growth != null && growth.CurrentForm == PlayerGrowthController.Form.Chick;
    }

    private void Apply(bool chick)
    {
        on = chick;
        foreach (var c in colliders) if (c != null) c.enabled = chick;
    }
}
