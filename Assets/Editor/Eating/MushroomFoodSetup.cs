using System;
using System.Collections.Generic;
using FarmReferenceTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Makes every mushroom on the farm food (EdibleMushroom: bigger ones take two or three bites and come apart in
/// even slices, seen from above):
///  - the pack prefabs get the component themselves, so a copy or a newly placed one is food too;
///  - the hand-painted ones are rebuilt (HandpaintedFloraV1): the joined brown pair comes apart (paddos_split.glb,
///    split in Blender), so each is its own food;
///  - their models are made readable (Read/Write), as the bites cut a copy of the mesh while playing;
///  - any mushroom in the scene still without one gets it.
/// Crumbs take the colour of the mushroom's cap, read from its texture. How many bites (1-3) is not stored: each
/// mushroom works it out from its size when play starts. Safe to run again.
/// </summary>
public static class MushroomFoodSetup
{
    [MenuItem("Chick/Eating/Make Mushrooms Edible")]
    private static void Menu() => Debug.Log(Apply());

    public static string Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        if (GameObject.Find("Farm Reference Map") == null) throw new InvalidOperationException("Farm Reference Map missing.");
        var report = new System.Text.StringBuilder("Mushroom food: ");

        // 1. readable meshes
        var mushrooms = SceneMushroomRenderers();
        int readable = 0;
        var importers = new HashSet<string>();
        foreach (var r in mushrooms)
        {
            var mesh = r.GetComponent<MeshFilter>().sharedMesh;
            string path = AssetDatabase.GetAssetPath(mesh);
            if (mesh.isReadable || !importers.Add(path) || !(AssetImporter.GetAtPath(path) is ModelImporter model)) continue;
            model.isReadable = true;
            model.SaveAndReimport();
            readable++;
        }
        report.Append(readable + " models made readable, ");

        // 2. the pack prefabs
        int prefabs = 0;
        var done = new HashSet<string>();
        foreach (var r in mushrooms)
        {
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject);
            if (root == null) continue;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            if (string.IsNullOrEmpty(path) || path.StartsWith("Assets/Art/Flowers/Handpainted") || !done.Add(path)) continue;
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var renderer = contents.GetComponentInChildren<MeshRenderer>(true);
                if (contents.GetComponentsInChildren<MeshRenderer>(true).Length != 1 || renderer == null) continue;
                var edible = contents.GetComponent<EdibleMushroom>() ?? contents.AddComponent<EdibleMushroom>();
                edible.Configure(CapColour(renderer));
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                prefabs++;
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
        report.Append(prefabs + " pack prefabs, ");

        // 3. the hand-painted ones
        report.Append(HandpaintedFloraV1.RebuildPrefabs() + " ");

        // 4. any mushroom left without the component
        int added = 0;
        mushrooms = SceneMushroomRenderers();
        foreach (var r in mushrooms)
        {
            if (r.GetComponentInParent<EdibleMushroom>(true) != null) continue;
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject);
            var target = root != null && root.GetComponentsInChildren<MeshRenderer>(true).Length == 1 ? root : r.gameObject;
            var edible = Undo.AddComponent<EdibleMushroom>(target);
            edible.Configure(CapColour(r));
            added++;
        }
        EditorSceneManager.MarkSceneDirty(GameObject.Find("Farm Reference Map").scene);

        // what the farm has now
        var bites = new int[4];
        var all = Object.FindObjectsByType<EdibleMushroom>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var m in all)
        {
            bites[m.BitesForCurrentSize()]++;
        }
        report.Append(added + " scene mushrooms given it directly; " + all.Length + " edible mushrooms: " +
            bites[1] + " one-bite, " + bites[2] + " two-bite, " + bites[3] + " three-bite.");
        return report.ToString();
    }

    // every mushroom's mesh in the open scene: under Map/Mushrooms, or a mesh that is called one
    private static List<MeshRenderer> SceneMushroomRenderers()
    {
        var list = new List<MeshRenderer>();
        var group = GameObject.Find("Map")?.transform.Find("Mushrooms");
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var f = r.GetComponent<MeshFilter>();
            if (f == null || f.sharedMesh == null) continue;
            string n = f.sharedMesh.name.ToLowerInvariant();
            if ((group != null && r.transform.IsChildOf(group)) || n.Contains("mushroom") || n.Contains("paddos")) list.Add(r);
        }
        return list;
    }

    private static Color CapColour(MeshRenderer r)
    {
        var mat = r.sharedMaterial;
        Texture tex = null;
        // URP Lit, then Synty's own shader
        if (mat != null) foreach (var property in new[] { "_BaseMap", "_Albedo_Map", "_MainTex" })
            if (tex == null && mat.HasProperty(property)) tex = mat.GetTexture(property);
        var tint = mat != null && mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
        var mesh = r.GetComponent<MeshFilter>().sharedMesh;
        return CapColour(tex, mesh, r.transform.InverseTransformDirection(Vector3.up)) * tint;
    }

    /// <summary>The colour of a mushroom's cap: its texture under the top third of the mesh (mesh up is +Y unless
    /// given), the more coloured texels counting most.</summary>
    public static Color CapColour(Texture texture, Mesh mesh, Vector3? meshUp = null)
    {
        var fallback = new Color(.86f, .74f, .6f);
        if (texture == null || mesh == null || !mesh.isReadable) return fallback;
        Vector3 up = (meshUp ?? Vector3.up).normalized;
        var vertices = mesh.vertices;
        var uv = mesh.uv;
        if (uv == null || uv.Length != vertices.Length) return fallback;
        float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
        foreach (var v in vertices) { float h = Vector3.Dot(v, up); lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h); }

        const int size = 256;
        var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var active = RenderTexture.active;
        var read = new Texture2D(size, size, TextureFormat.RGBA32, false);
        try
        {
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            read.Apply();
            Color sum = Color.clear; float weight = 0f;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (Vector3.Dot(vertices[i], up) < Mathf.Lerp(lo, hi, .66f)) continue;
                Color c = read.GetPixelBilinear(Mathf.Repeat(uv[i].x, 1f), Mathf.Repeat(uv[i].y, 1f));
                Color.RGBToHSV(c, out _, out float s, out _);
                float w = .15f + s*s;
                sum += c*w; weight += w;
            }
            if (weight <= 0f) return fallback;
            var colour = sum/weight;
            colour.a = 1f;
            return colour;
        }
        finally
        {
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(read);
        }
    }
}
