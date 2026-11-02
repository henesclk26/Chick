using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The hand-painted rocks of stone_pack.glb (Assets/Assets/ROCKS) as one prefab each, ready to place: the rock's mesh
/// from the pack with its painting on a mossy Farm/Rock Moss material (Assets/Art/RockyFalls/Materials), turned and
/// scaled as the pack has it, its pivot at the bottom middle, a collider that matches its shape and a LOD group that stops drawing it when it is tiny on screen. Big_* are tall cliff blocks, Mid_* layered
/// ledges, Small_* boulders; the carved runestones and the flat planks are left out. Prefabs in
/// Assets/Art/RockyFalls/Prefabs, made again (from the pack) whenever <see cref="Ensure"/> runs.
/// </summary>
public static class RockKit
{
    public const string Source = "Assets/Assets/ROCKS/stone_pack.glb";
    public const string Folder = "Assets/Art/RockyFalls/Prefabs";
    private static readonly string[] Skip = { "Runic", "p1_", "p2_" };

    /// <summary>Makes (or refreshes) the prefabs; returns them by kind ("Big", "Mid", "Small").</summary>
    public static Dictionary<string, List<GameObject>> Ensure(StringBuilder report)
    {
        var kinds = new Dictionary<string, List<GameObject>> { ["Big"] = new List<GameObject>(), ["Mid"] = new List<GameObject>(), ["Small"] = new List<GameObject>() };
        var pack = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        if (pack == null) { report.AppendLine("rock pack missing: " + Source); return kinds; }
        if (!AssetDatabase.IsValidFolder("Assets/Art/RockyFalls")) AssetDatabase.CreateFolder("Assets/Art", "RockyFalls");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art/RockyFalls", "Prefabs");
        var seen = new HashSet<string>();
        foreach (MeshFilter filter in pack.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || Skip.Any(s => filter.name.StartsWith(s))) continue;
            string name = Clean(filter.name);
            string kind = name.StartsWith("Big", System.StringComparison.OrdinalIgnoreCase) ? "Big" : name.StartsWith("Mid", System.StringComparison.OrdinalIgnoreCase) ? "Mid" : "Small";
            if (!seen.Add(name)) continue;
            var renderer = filter.GetComponent<MeshRenderer>();
            // The rock as the pack stands it up (the pack's own turn included), without its place in the pack.
            Matrix4x4 m = filter.transform.localToWorldMatrix;
            var root = new GameObject("Rock_" + name);
            var body = new GameObject("Mesh");
            body.transform.SetParent(root.transform, false);
            // glTF stands its models up with a mirror (a negative scale); Matrix4x4.rotation loses it, so take it apart here.
            Vector3 scale = new Vector3(m.GetColumn(0).magnitude, m.GetColumn(1).magnitude, m.GetColumn(2).magnitude);
            if (m.determinant < 0f) scale.x = -scale.x;
            body.transform.localRotation = Quaternion.LookRotation(m.GetColumn(2) / scale.z, m.GetColumn(1) / scale.y);
            body.transform.localScale = scale;
            body.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var r = body.AddComponent<MeshRenderer>();
            r.sharedMaterials = renderer.sharedMaterials.Select(MossCopy).ToArray();
            body.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            Bounds b = r.bounds;
            body.transform.localPosition = -new Vector3(b.center.x, b.min.y, b.center.z);
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.015f, new Renderer[] { r }) });
            lod.RecalculateBounds();
            string path = $"{Folder}/Rock_{name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            kinds[kind].Add(prefab);
            report.AppendLine($"  {kind,-5} {name}: {b.size.x:0.0} x {b.size.y:0.0} x {b.size.z:0.0} m, {filter.sharedMesh.triangles.Length / 3} tris");
        }
        AssetDatabase.ImportAsset(MaterialFolder, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(Folder, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        foreach (var k in kinds.Values) k.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        report.AppendLine($"rock kit: {kinds["Big"].Count} big, {kinds["Mid"].Count} ledges, {kinds["Small"].Count} boulders");
        return kinds;
    }

    public const string MaterialFolder = "Assets/Art/RockyFalls/Materials";

    // The pack's material as a Farm/Rock Moss one (its painted colour and normal map, moss on top): the boulders a
    // little less mossy than the cliffs. Made once; its settings are kept if it is tuned by hand afterwards.
    private static Material MossCopy(Material source)
    {
        if (source == null) return null;
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Art/RockyFalls", "Materials");
        string path = $"{MaterialFolder}/M_Rock_{source.name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Farm/Rock Moss"));
        if (source.HasProperty("baseColorTexture")) m.SetTexture("_BaseMap", source.GetTexture("baseColorTexture"));
        if (source.HasProperty("normalTexture")) m.SetTexture("_BumpMap", source.GetTexture("normalTexture"));
        if (source.name == "SMall") m.SetFloat("_Moss", .4f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private static string Clean(string meshName)
    {
        // "Big_5_BiG_0" -> "Big_5"
        string[] parts = meshName.Split('_');
        return parts.Length >= 2 ? parts[0] + "_" + parts[1] : meshName;
    }
}
