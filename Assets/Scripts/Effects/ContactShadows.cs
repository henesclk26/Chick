using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Soft contact shadows that keep things visibly on the ground: under the fruit and vegetables lying on the ground
/// (and, if birdShadows is on, under the chick, the grown chicken and the helper chicks). The sun's own shadow falls far to the side for most of the
/// day, and on the smooth painted ground nothing else shows where a foot or a fallen fruit touches it.
/// One instanced draw per frame (Chick/ContactShadow); a shadow fades and spreads as its owner rises off the ground.
/// Presentation only.
/// </summary>
[DefaultExecutionOrder(200)]
public sealed class ContactShadows : MonoBehaviour
{
    [SerializeField, Tooltip("Chick/ContactShadow material (instancing on).")]
    private Material material;
    [SerializeField, Tooltip("A round shadow under the birds too. Off: their own sun shadow is enough.")]
    private bool birdShadows;
    [SerializeField, Min(.5f), Tooltip("Bird shadow radius, in CharacterController radii.")]
    private float birdSize = 1.9f;
    [SerializeField, Min(.5f), Tooltip("Food shadow radius, in the food's half-width.")]
    private float foodSize = 1.35f;
    [SerializeField, Min(.05f), Tooltip("Height above the ground at which a shadow has faded out.")]
    private float fadeHeight = .45f;
    [SerializeField, Min(1f), Tooltip("Food further than this from the camera gets no contact shadow.")]
    private float foodRange = 28f;
    [SerializeField, Min(.005f), Tooltip("Food smaller than this (seeds, grains) gets no contact shadow.")]
    private float minimumFoodSize = .035f;

    private const int Batch = 1023;
    private static int edibleLayer = -1;

    private sealed class Food
    {
        public Transform root;
        public Renderer[] renderers;
    }

    private readonly List<CharacterController> birds = new List<CharacterController>();
    private readonly List<Food> foods = new List<Food>();
    private readonly Matrix4x4[] matrices = new Matrix4x4[Batch];
    private int count;
    private Mesh quad;
    private float nextScan;

    private void Awake()
    {
        if (edibleLayer < 0) edibleLayer = LayerMask.NameToLayer("Edible");
        quad = BuildQuad();
    }

    // drawn for each camera as it renders (game and scene views alike)
    private void OnEnable() => UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += Draw;
    private void OnDisable() => UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= Draw;

    private void OnDestroy()
    {
        if (quad != null) Destroy(quad);
    }

    private void Draw(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
    {
        if (count == 0 || material == null || quad == null) return;
        if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return;
        var rp = new RenderParams(material)
        {
            camera = camera,
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows = false,
            worldBounds = new Bounds(Vector3.zero, Vector3.one*10000f)
        };
        Graphics.RenderMeshInstanced(rp, quad, 0, matrices, count);
    }

    private void LateUpdate()
    {
        count = 0;
        if (material == null) return;
        if (Time.unscaledTime >= nextScan) { Scan(); nextScan = Time.unscaledTime + 2f; }

        foreach (var bird in birds)
        {
            if (bird == null || !bird.enabled || !bird.gameObject.activeInHierarchy) continue;
            Transform t = bird.transform;
            float scale = Mathf.Max(t.lossyScale.x, t.lossyScale.z);
            Vector3 feet = t.TransformPoint(bird.center - Vector3.up * (bird.height * .5f));
            // from just inside the bird's own capsule, which a ray started there does not hit
            if (!Physics.Raycast(feet + Vector3.up * .03f, Vector3.down, out RaycastHit hit, 3f, GroundMask, QueryTriggerInteraction.Ignore)) continue;
            float height = Mathf.Max(0f, feet.y - hit.point.y);
            Add(hit.point, hit.normal, bird.radius * scale * birdSize * (1f + height*1.5f), height, fadeHeight);
        }

        var terrain = Terrain.activeTerrain;
        var cam = Camera.main;
        if (cam == null) return;
        if (terrain != null && cam != null)
        {
            Vector3 eye = cam.transform.position;
            float range2 = foodRange * foodRange;
            var data = terrain.terrainData; Vector3 tp = terrain.transform.position;
            foreach (var food in foods)
            {
                if (count >= Batch) break;
                if (food.root == null || !food.root.gameObject.activeInHierarchy) continue;
                if ((food.root.position - eye).sqrMagnitude > range2) continue;
                if (!Bounds(food, out Bounds b)) continue;
                float half = Mathf.Max(b.extents.x, b.extents.z);
                if (half*2f < minimumFoodSize) continue;
                Vector3 c = b.center;
                float ground = terrain.SampleHeight(c) + tp.y;
                float height = b.min.y - ground;
                // only what lies on the ground (or is about to land): nothing under fruit still hanging on its plant
                if (height > FoodFade || height < -.25f) continue;
                Vector3 normal = data.GetInterpolatedNormal((c.x - tp.x)/data.size.x, (c.z - tp.z)/data.size.z);
                Add(new Vector3(c.x, ground, c.z), normal, Mathf.Min(half*foodSize, MaxFoodRadius), Mathf.Max(0f, height), FoodFade);
            }
        }
    }

    private const float FoodFade = .12f, MaxFoodRadius = .3f;

    private void Add(Vector3 at, Vector3 normal, float radius, float height, float fadeOver)
    {
        if (count >= Batch) return;
        float fade = 1f - Mathf.Clamp01(height / fadeOver);
        if (fade <= .01f) return;
        var rotation = Quaternion.FromToRotation(Vector3.up, normal);
        // the fade rides in the Y scale (the quad is flat); the shader reads it back
        matrices[count++] = Matrix4x4.TRS(at + normal*.004f, rotation, new Vector3(radius*2f, fade, radius*2f));
    }

    private static int GroundMask => ~(edibleLayer >= 0 ? 1 << edibleLayer : 0);

    private static bool Bounds(Food food, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var r in food.renderers)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    // the birds (player and helpers) and the fruit and vegetables of the trees and plants
    private void Scan()
    {
        birds.Clear();
        if (birdShadows)
        {
            foreach (var p in FindObjectsByType<ChickPlayerController>(FindObjectsSortMode.None))
                if (p.TryGetComponent(out CharacterController c)) birds.Add(c);
            foreach (var h in FindObjectsByType<HelperChickController>(FindObjectsSortMode.None))
            {
                var c = h.GetComponentInChildren<CharacterController>();
                if (c != null) birds.Add(c);
            }
        }

        if (foods.Count > 0) return;      // the food objects stay; only their state changes
        foreach (var e in FindObjectsByType<EdibleObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (e.GetComponentInParent<FruitTree>() == null && e.GetComponentInParent<BerryPlant>() == null) continue;
            var renderers = e.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) continue;
            foods.Add(new Food { root = e.transform, renderers = renderers });
        }
    }

    private static Mesh BuildQuad()
    {
        var mesh = new Mesh { name = "Contact Shadow Quad" };
        mesh.vertices = new[] { new Vector3(-.5f, 0f, -.5f), new Vector3(.5f, 0f, -.5f), new Vector3(.5f, 0f, .5f), new Vector3(-.5f, 0f, .5f) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.RecalculateBounds();
        return mesh;
    }
}
