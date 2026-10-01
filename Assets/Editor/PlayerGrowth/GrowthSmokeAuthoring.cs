using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class GrowthSmokeAuthoring
{
    const string Folder = "Assets/Art/Effects/GrowthSmoke";
    public static string Build()
    {
        if (EditorApplication.isPlaying || Application.dataPath != "E:/Yeni klasör/Chick/Assets")
            throw new InvalidOperationException("Use the intended project in edit mode.");
        if (!AssetDatabase.IsValidFolder("Assets/Art/Effects")) AssetDatabase.CreateFolder("Assets/Art", "Effects");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art/Effects", "GrowthSmoke");
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("URP Lit shader missing.");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/WarmCreamSmoke.mat");
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, Folder + "/WarmCreamSmoke.mat"); }
        mat.SetColor("_BaseColor", new Color(.94f, .905f, .81f));
        mat.SetFloat("_Smoothness", .05f); mat.SetFloat("_Metallic", 0);
        mat.SetFloat("_SpecularHighlights", 0); mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", new Color(.10f, .085f, .06f));
        mat.enableInstancing = true; EditorUtility.SetDirty(mat);
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        const int rings = 12, segments = 20;
        for (int j = 0; j <= rings; j++) for (int i = 0; i <= segments; i++)
        {
            float theta = Mathf.PI * j / rings, phi = Mathf.PI * 2 * i / segments;
            float s = Mathf.Sin(theta);
            float r = 1f + .07f * Mathf.Cos(phi * 3 + .5f) * s * s + .045f * Mathf.Sin(theta * 4 + phi * 2) * s;
            vertices.Add(new Vector3(s * Mathf.Cos(phi), Mathf.Cos(theta), s * Mathf.Sin(phi)) * r);
        }
        for (int j = 0; j < rings; j++) for (int i = 0; i < segments; i++)
        {
            int a = j * (segments + 1) + i, b = a + 1, c = a + segments + 1, d = c + 1;
            if (j > 0) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
            if (j < rings - 1) { triangles.Add(b); triangles.Add(d); triangles.Add(c); }
        }
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/CloudPuff.asset");
        if (mesh == null) { mesh = new Mesh { name = "Soft lobed cloud puff" }; AssetDatabase.CreateAsset(mesh, Folder + "/CloudPuff.asset"); }
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        // Smooth the duplicated UV seam; no texture is needed.
        var normals = mesh.normals;
        for (int j = 0; j <= rings; j++) { int a = j * (segments + 1), b = a + segments; var n = (normals[a] + normals[b]).normalized; normals[a] = normals[b] = n; }
        mesh.normals = normals; EditorUtility.SetDirty(mesh);
        var root = new GameObject("Growth Smoke Burst"); root.layer = 2;
        try
        {
            root.AddComponent<GrowthSmokeBurst>();
            for (int i = 0; i < GrowthSmokeBurst.PuffCount; i++)
            {
                var p = new GameObject(i == 0 ? "Opaque cover puff" : "Swirling puff " + i);
                p.layer = 2; p.transform.SetParent(root.transform, false);
                p.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = p.AddComponent<MeshRenderer>(); renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/GrowthSmokeBurst.prefab");
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerGrowthController>();
            if (player == null) throw new InvalidOperationException("Player growth controller missing.");
            Undo.RecordObject(player, "Attach natural growth smoke");
            var so = new SerializedObject(player);
            so.FindProperty("growthSmokePrefab").objectReferenceValue = prefab.GetComponent<GrowthSmokeBurst>();
            so.FindProperty("growthDuration").floatValue = 1.6f; so.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(player);
            EditorUtility.SetDirty(player); EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            AssetDatabase.SaveAssets();
            return "Smoke prefab linked: " + GrowthSmokeBurst.PuffCount + " puffs, " + mesh.triangles.Length / 3 + " triangles each; shared mesh/material; no colliders.";
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
