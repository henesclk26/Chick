using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor-only, explicit application. Makes the textures and particle materials GroundEffects uses
// (Assets/Art/GroundEffects) and puts the component on the player chick in the open scene.
public static class GroundEffectsSetup
{
    const string Folder = "Assets/Art/GroundEffects/";
    const string DefaultParticles = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";

    [MenuItem("Tools/Chick/Effects/Set Up Ground Effects")]
    static void MenuApply() => Debug.Log(Apply());

    public static string Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
        var player = GameObject.FindWithTag("Player") ?? throw new InvalidOperationException("No Player in the scene.");
        Directory.CreateDirectory(Folder + "Textures");
        var puff = Texture(Folder + "Textures/SoftPuff.png", 64, (u, v) => SoftDot(u, v, .5f, 1.6f, .18f));
        var chip = Texture(Folder + "Textures/Chip.png", 32, Chip);
        var dot = Texture(Folder + "Textures/PollenDot.png", 32, (u, v) => SoftDot(u, v, .32f, 2.4f, 0f));
        var effects = player.GetComponent<GroundEffects>() ?? Undo.AddComponent<GroundEffects>(player);
        var so = new SerializedObject(effects);
        so.FindProperty("puffMaterial").objectReferenceValue = Material(Folder + "M_GroundPuff.mat", puff);
        so.FindProperty("chipMaterial").objectReferenceValue = Material(Folder + "M_GroundChip.mat", chip);
        so.FindProperty("pollenMaterial").objectReferenceValue = Material(Folder + "M_Pollen.mat", dot);
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(player.scene);
        return "Ground effects set up on " + player.name + ".";
    }

    // A soft round dot with a little painted irregularity at its rim.
    static float SoftDot(float u, float v, float radius, float power, float wobble)
    {
        float dx = u - .5f, dy = v - .5f;
        float a = Mathf.Atan2(dy, dx);
        float r = Mathf.Sqrt(dx*dx + dy*dy)/(radius*(1 + wobble*(.5f*Mathf.Sin(a*3 + 1) + .3f*Mathf.Sin(a*5 + 2))));
        return Mathf.Clamp01(1 - Mathf.Pow(Mathf.Clamp01(r), power));
    }

    // An irregular, fairly hard-edged chip (a stone fleck, a leaf bit, a drop of mud).
    static float Chip(float u, float v)
    {
        float dx = (u - .5f)*1.25f, dy = v - .5f;
        float a = Mathf.Atan2(dy, dx);
        float edge = .38f*(1 + .18f*Mathf.Sin(a*3 + .7f) + .1f*Mathf.Sin(a*7 + 2.1f));
        float r = Mathf.Sqrt(dx*dx + dy*dy);
        return Mathf.Clamp01((edge - r)/.05f);
    }

    static Texture2D Texture(string path, int size, Func<float, float, float> alpha)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float a = alpha((x + .5f)/size, (y + .5f)/size);
            tex.SetPixel(x, y, new Color(1, 1, 1, a));
        }
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp; importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material Material(string path, Texture2D tex)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var template = AssetDatabase.LoadAssetAtPath<Material>(DefaultParticles) ?? throw new InvalidOperationException("URP ParticlesUnlit missing.");
            material = new Material(template);
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetTexture("_BaseMap", tex);
        material.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }
}
