using System;
using System.IO;
using MicahW.PointGrass;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Grows Point Grass Renderer blades over the chicken yard (inside the fence, x 5-35 and z 0-36 in Farm Reference Map
/// space, see AntColonyPlacement) as a test patch:
/// - A base mesh follows the terrain on a 25 cm grid. Its vertex colours drive the package's distribution: red is the
///   density (the painted grass layers of the terrain, a little patchy, zero where something stands on the ground such
///   as the coop, troughs, posts or ant nests), green the blade length.
/// - The blades take the terrain's colour under them from a ground texture baked from the terrain layers, so they grow
///   out of the ground without a seam; the material's bottom and top colours shade them darker at the root and lighter
///   at the tip.
/// - A PointGrassWind and a PointGrassDisplacementManager drive wind and pushing; the player chick gets a
///   PointGrassChickPusher so it parts the blades it walks through.
/// Run again after reshaping or repainting the yard; the material is only created once, so tweaks to it are kept.
/// </summary>
public static class ChickenYardPointGrass
{
    private const string Folder = "Assets/Art/PointGrass";
    private const string MeshPath = Folder + "/ChickenYardGrass_Base.asset";
    private const string GroundPath = Folder + "/T_ChickenYardGrass_Ground.png";
    private const string MaterialPath = Folder + "/M_ChickenYardGrass.mat";
    // The package's flat-blade demo material: already set up for its shader graph, copied and then retuned.
    private const string TemplateMaterial = "Assets/Packages/Point-Grass-Renderer/Runtime/Demo/Materials/PGDemo_Blade.mat";
    private const string ObjectName = "Chicken Yard Point Grass";

    private static readonly Vector2 YardMin = new Vector2(5f, 0f);
    private static readonly Vector2 YardMax = new Vector2(35f, 36f);
    private const float FenceInset = 0.2f;          // keeps the blades from poking through the fence boards
    private const float Step = 0.25f;               // base mesh grid
    private const int GroundPixelsPerMetre = 8;
    private const float BladesPerSquareMetre = 180f;
    private const float DensityCutoff = 0.2f;
    private const int Seed = 1905;
    private const float BlockProbeHeight = 0.12f, BlockProbeRadius = 0.1f;

    [MenuItem("Tools/Chick/Grass/Add Point Grass To Chicken Yard")]
    private static void Add()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Point grass", "Exit play mode first.", "OK");
            return;
        }
        var root = GameObject.Find("Farm Reference Map");
        Terrain terrain = root != null ? root.GetComponentInChildren<Terrain>() : null;
        var template = AssetDatabase.LoadAssetAtPath<Material>(TemplateMaterial);
        if (root == null || terrain == null || terrain.terrainData == null || template == null)
        {
            EditorUtility.DisplayDialog("Point grass", root == null ? "The open scene has no 'Farm Reference Map'."
                : terrain == null ? "No terrain under 'Farm Reference Map'."
                : "Point Grass Renderer package (Assets/Packages/Point-Grass-Renderer) not found.", "OK");
            return;
        }
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art", "PointGrass");

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Add point grass to chicken yard");
        int group = Undo.GetCurrentGroup();

        Transform old = SceneLookup.FindAnywhere(root, ObjectName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var go = new GameObject(ObjectName);
        Undo.RegisterCreatedObjectUndo(go, "Add point grass to chicken yard");
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = new Vector3(YardMin.x, 0f, YardMin.y);
        go.transform.localRotation = Quaternion.identity;
        go.layer = 0;

        var yard = new Yard(root.transform, go.transform, terrain);
        Mesh mesh = SaveMesh(yard.BuildMesh(out float blocked, out float grassShare));
        Texture2D ground = SaveGround(yard.BakeGround());
        Material material = EnsureMaterial(template, ground);

        var grass = Undo.AddComponent<PointGrassRenderer>(go);
        grass.distSource = PointGrassCommon.DistributionSource.Mesh;
        grass.baseMesh = mesh;
        grass.bladeType = PointGrassCommon.BladeType.Flat;
        grass.multipleMeshes = false;
        grass.multipleMaterials = false;
        grass.material = material;
        grass.shadowMode = ShadowCastingMode.Off;   // a hundred thousand blades: cheaper, and they still receive shadows
        grass.pointCount = BladesPerSquareMetre;
        grass.multiplyByArea = true;
        grass.pointLODFactor = 1f;
        grass.randomiseSeed = false;
        grass.seed = Seed;
        grass.overwriteNormalDirection = false;      // blades stand along the ground's normal
        grass.useDensity = true;
        grass.densityCutoff = DensityCutoff;
        grass.useLength = true;
        grass.lengthMapping = new Vector2(0.45f, 1f);
        grass.projectType = PointGrassCommon.ProjectionType.None;
        grass.boundingBoxOffset = new Bounds(new Vector3(0f, 0.1f, 0f), new Vector3(0.4f, 0.4f, 0.4f));
        EditorUtility.SetDirty(grass);
        grass.BuildPoints();

        if (UnityEngine.Object.FindFirstObjectByType<PointGrassWind>() == null)
        {
            var wind = Undo.AddComponent<PointGrassWind>(go);
            wind.windScale = 0.3f;
            wind.noiseRange = new Vector2(-0.3f, 1f);
            wind.windDirection = new Vector3(0.25f, -0.05f, 0.4f);
            wind.windScroll = new Vector3(0.4f, 0f, 0.6f);
            wind.RefreshValues();
        }
        if (UnityEngine.Object.FindFirstObjectByType<PointGrassDisplacementManager>() == null)
            Undo.AddComponent<PointGrassDisplacementManager>(go);

        string player = "no player chick found";
        var chick = UnityEngine.Object.FindFirstObjectByType<ChickPlayerController>();
        if (chick != null && chick.GetComponent<CharacterController>() != null)
        {
            if (chick.GetComponent<PointGrassChickPusher>() == null) Undo.AddComponent<PointGrassChickPusher>(chick.gameObject);
            player = "'" + chick.name + "' parts the blades";
        }

        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(root.scene);
        PointGrassRenderer.DebugInformation info = grass.GetDebugInfo();
        string report = $"Point grass grown over the chicken yard: {info.totalPointCount:N0} blades, grass layers on " +
                        $"{grassShare:P0} of the yard, {blocked:P0} left bare under objects; {player}. " +
                        "Save the scene to keep it (Ctrl+Z undoes it).";
        Debug.Log(report, go);
        if (blocked > 0.6f)
            EditorUtility.DisplayDialog("Point grass", $"{blocked:P0} of the yard was left bare because colliders sit " +
                                        "on the ground there. Check the Console for the details.", "OK");
    }

    [MenuItem("Tools/Chick/Grass/Remove Point Grass From Chicken Yard")]
    private static void Remove()
    {
        var root = GameObject.Find("Farm Reference Map");
        Transform grass = root != null ? SceneLookup.FindAnywhere(root, ObjectName) : null;
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Remove point grass from chicken yard");
        if (grass != null) Undo.DestroyObjectImmediate(grass.gameObject);
        foreach (var pusher in UnityEngine.Object.FindObjectsByType<PointGrassChickPusher>(FindObjectsSortMode.None))
        {
            var displacer = pusher.GetComponent<PointGrassDisplacer>();
            Undo.DestroyObjectImmediate(pusher);
            if (displacer != null) Undo.DestroyObjectImmediate(displacer);
        }
        if (root != null) EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log(grass != null ? "Point grass removed from the chicken yard (its assets in " + Folder + " are kept)."
                                : "No point grass in the chicken yard.");
    }

    // Saves into the existing asset when there is one, so the renderer's reference survives a rebuild.
    private static Mesh SaveMesh(Mesh built)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (mesh == null)
        {
            AssetDatabase.CreateAsset(built, MeshPath);
            return built;
        }
        mesh.Clear();
        mesh.indexFormat = built.indexFormat;
        mesh.vertices = built.vertices;
        mesh.normals = built.normals;
        mesh.uv = built.uv;
        mesh.colors = built.colors;
        mesh.triangles = built.triangles;
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        AssetDatabase.SaveAssetIfDirty(mesh);
        UnityEngine.Object.DestroyImmediate(built);
        return mesh;
    }

    private static Texture2D SaveGround(Texture2D baked)
    {
        File.WriteAllBytes(GroundPath, baked.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(baked);
        AssetDatabase.ImportAsset(GroundPath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(GroundPath);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = true;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 512;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(GroundPath);
    }

    private static Material EnsureMaterial(Material template, Texture2D ground)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(template) { name = "M_ChickenYardGrass" };
            // Colour = (bottom..top gradient along the blade) x (ground texture at the blade's root).
            SetToggle(material, "BOOLEAN_ALBEDOTEX", false);
            SetToggle(material, "BOOLEAN_BLADECOL", false);
            SetToggle(material, "BOOLEAN_GROUNDCOL", true);
            SetToggle(material, "BOOLEAN_USEMESHNORMALS", false);
            material.SetColor("_BottomCol", new Color(0.52f, 0.58f, 0.46f));
            material.SetColor("_TopCol", new Color(1.12f, 1.18f, 0.96f));
            material.SetFloat("_TopBright", 1f);
            material.SetFloat("_Height", 0.16f);      // chick-sized: the tallest blades reach its belly
            material.SetFloat("_Width", 0.018f);
            material.SetFloat("_WindStr", 0.35f);
            material.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.SetTexture("_GroundTex", ground);
        material.SetTextureScale("_GroundTex", Vector2.one);
        material.SetTextureOffset("_GroundTex", Vector2.zero);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }

    // Shader Graph boolean keywords: the material stores the toggle as a float and the keyword as <reference>_ON.
    private static void SetToggle(Material material, string reference, bool on)
    {
        material.SetFloat(reference, on ? 1f : 0f);
        if (on) material.EnableKeyword(reference + "_ON"); else material.DisableKeyword(reference + "_ON");
    }

    // The yard sampled from the terrain, in the grass object's space.
    private sealed class Yard
    {
        private readonly Transform root, grass;
        private readonly Terrain terrain;
        private readonly TerrainData data;
        private readonly float[,,] alphamaps;
        private readonly float[] grassWeights;
        private readonly Vector2 min, size;

        public Yard(Transform root, Transform grass, Terrain terrain)
        {
            this.root = root;
            this.grass = grass;
            this.terrain = terrain;
            data = terrain.terrainData;
            alphamaps = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            TerrainLayer[] layers = data.terrainLayers;
            grassWeights = new float[layers.Length];
            for (int i = 0; i < layers.Length; i++) grassWeights[i] = GrassWeight(layers[i] != null ? layers[i].name : "");
            min = YardMin + Vector2.one * FenceInset;
            size = YardMax - YardMin - 2f * FenceInset * Vector2.one;
        }

        // How much a terrain layer counts as grass, by name (MeadowVarietyV3's layers, with fallbacks).
        private static float GrassWeight(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("worn")) return 0.5f;
            if (n.Contains("green") || n.Contains("meadow") || n.Contains("grass")) return 1f;
            return 0f;
        }

        public Mesh BuildMesh(out float blockedShare, out float grassShare)
        {
            Physics.SyncTransforms();
            int nx = Mathf.RoundToInt(size.x / Step) + 1, nz = Mathf.RoundToInt(size.y / Step) + 1;
            var vertices = new Vector3[nx * nz];
            var normals = new Vector3[nx * nz];
            var uvs = new Vector2[nx * nz];
            var colours = new Color[nx * nz];
            int blocked = 0, grassy = 0;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = i + j * nx;
                    float u = i / (nx - 1f), v = j / (nz - 1f);
                    Vector3 world = Ground(new Vector2(min.x + u * size.x, min.y + v * size.y), out Vector2 n);
                    float grassiness = Grassiness(n);
                    if (grassiness > DensityCutoff) grassy++;
                    bool covered = Blocked(world);
                    if (covered) blocked++;
                    // Patchy, as real grass: thicker and thinner clumps a couple of metres across.
                    float patch = Mathf.Lerp(0.7f, 1f, Mathf.PerlinNoise(world.x * 0.45f + 13.1f, world.z * 0.45f + 7.7f));
                    float length = Mathf.Clamp01(Mathf.PerlinNoise(world.x * 0.9f + 41.3f, world.z * 0.9f + 3.9f) * 1.2f - 0.1f);
                    vertices[k] = grass.InverseTransformPoint(world);
                    normals[k] = grass.InverseTransformDirection(data.GetInterpolatedNormal(n.x, n.y)).normalized;
                    uvs[k] = new Vector2(u, v);
                    colours[k] = new Color(covered ? 0f : grassiness * patch, length, 0f, 1f);
                }
            var triangles = new int[(nx - 1) * (nz - 1) * 6];
            int t = 0;
            for (int j = 0; j < nz - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    int a = i + j * nx, b = a + 1, c = a + nx, d = c + 1;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
            var mesh = new Mesh { name = "ChickenYardGrass_Base" };
            mesh.indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.colors = colours;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            blockedShare = (float)blocked / vertices.Length;
            grassShare = (float)grassy / vertices.Length;
            return mesh;
        }

        // The ground's colour across the yard, mixed from the terrain layers like the terrain shader does.
        public Texture2D BakeGround()
        {
            TerrainLayer[] layers = data.terrainLayers;
            var copies = new Texture2D[layers.Length];
            for (int i = 0; i < layers.Length; i++)
                copies[i] = layers[i] != null && layers[i].diffuseTexture != null ? ReadableCopy(layers[i].diffuseTexture, 128) : null;
            int w = Mathf.RoundToInt(size.x * GroundPixelsPerMetre), h = Mathf.RoundToInt(size.y * GroundPixelsPerMetre);
            var pixels = new Color[w * h];
            Vector3 terrainOrigin = terrain.transform.position;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Vector3 world = Ground(new Vector2(min.x + (x + 0.5f) / w * size.x, min.y + (y + 0.5f) / h * size.y), out Vector2 n);
                    Vector2 local = new Vector2(world.x - terrainOrigin.x, world.z - terrainOrigin.z);
                    Color colour = Color.black;
                    float total = 0f;
                    for (int l = 0; l < layers.Length; l++)
                    {
                        float weight = Alpha(n, l);
                        if (weight <= 0f || copies[l] == null) continue;
                        TerrainLayer layer = layers[l];
                        Vector2 uv = new Vector2((local.x + layer.tileOffset.x) / layer.tileSize.x,
                                                 (local.y + layer.tileOffset.y) / layer.tileSize.y);
                        Color c = copies[l].GetPixelBilinear(uv.x, uv.y);
                        Vector4 remap = layer.diffuseRemapMax;
                        colour += new Color(c.r * remap.x, c.g * remap.y, c.b * remap.z) * weight;
                        total += weight;
                    }
                    colour = total > 0f ? colour / total : new Color(0.4f, 0.55f, 0.3f);
                    colour.a = 1f;
                    pixels[x + y * w] = colour;
                }
            foreach (Texture2D copy in copies) if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            var texture = new Texture2D(w, h, TextureFormat.RGB24, false);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        // A point of the yard (Farm Reference Map x/z) on the terrain, and its normalised terrain coordinate.
        private Vector3 Ground(Vector2 yardPoint, out Vector2 normalised)
        {
            Vector3 world = root.TransformPoint(new Vector3(yardPoint.x, 0f, yardPoint.y));
            Vector3 origin = terrain.transform.position;
            normalised = new Vector2(Mathf.Clamp01((world.x - origin.x) / data.size.x), Mathf.Clamp01((world.z - origin.z) / data.size.z));
            world.y = origin.y + data.GetInterpolatedHeight(normalised.x, normalised.y);
            return world;
        }

        private float Grassiness(Vector2 normalised)
        {
            float sum = 0f;
            for (int l = 0; l < grassWeights.Length; l++) if (grassWeights[l] > 0f) sum += Alpha(normalised, l) * grassWeights[l];
            return Mathf.Clamp01(sum);
        }

        // Bilinear alphamap weight of one layer.
        private float Alpha(Vector2 normalised, int layer)
        {
            float fx = normalised.x * (data.alphamapWidth - 1), fz = normalised.y * (data.alphamapHeight - 1);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, data.alphamapWidth - 2), z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, data.alphamapHeight - 2);
            float tx = fx - x0, tz = fz - z0;
            float bottom = Mathf.Lerp(alphamaps[z0, x0, layer], alphamaps[z0, x0 + 1, layer], tx);
            float top = Mathf.Lerp(alphamaps[z0 + 1, x0, layer], alphamaps[z0 + 1, x0 + 1, layer], tx);
            return Mathf.Lerp(bottom, top, tz);
        }

        // Something standing on the ground here (coop, trough, post, nest...): no grass through it. Moving things
        // (the chick, food that can be eaten or pushed) do not count.
        private static bool Blocked(Vector3 world)
        {
            foreach (Collider c in Physics.OverlapSphere(world + Vector3.up * BlockProbeHeight, BlockProbeRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c is TerrainCollider || c is CharacterController) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
                return true;
            }
            return false;
        }

        // Terrain layer textures are usually not CPU-readable: blit a small copy (kept in sRGB, like the PNG it feeds).
        private static Texture2D ReadableCopy(Texture2D source, int resolution)
        {
            RenderTexture rt = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(resolution, resolution, TextureFormat.RGB24, false, false);
            copy.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            copy.Apply();
            copy.wrapMode = source.wrapMode;
            copy.filterMode = FilterMode.Bilinear;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }
    }
}
