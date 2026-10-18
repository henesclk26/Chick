using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

/// <summary>
/// Converts the materials of two store packs that render pink under URP:
/// - polygonTrees uses the Built-in Standard shader and the old Nature/Tree Soft Occlusion bark and leaf shaders:
///   all become URP Lit (leaves double-sided), keeping colour, texture, cutoff, smoothness and normal map.
/// - IL3DN uses its own Built-in surface shaders, which URP cannot run: the grass plants move to Chick/InteractiveFoliage
///   (the meadow grass shader, so they lean away from the chick), the pine bark, pine needles and ground to URP Lit.
///   The pine needles do not bend: that shader leans a whole object from its base, which would pull them off the trunk.
/// Shaders that already work under URP (IL3DN's skybox, backdrop and particle glow) are left alone. Runs by itself after
/// each script reload when a material of these packs still needs it (so freshly imported packs fix themselves), and
/// from the menu; materials already converted are skipped.
/// </summary>
public static class ThirdPartyMaterialsToURP
{
    private const string PolygonTrees = "Assets/polygonTrees";
    private const string IL3DN = "Assets/IL3DN";
    private static readonly string[] IL3DNGrass =
        { "IL3DN_Plant_Grass_01", "IL3DN_Plant_Grass_02", "IL3DN_Plant_Grass_03" };
    private static readonly string[] IL3DNLit = { "IL3DN_Tree_Pine_Leaf", "IL3DN_Tree_Pine_Bark", "IL3DN_Ground_Grass" };
    private const string IL3DNLeaves = "IL3DN_Tree_Pine_Leaf";

    [InitializeOnLoadMethod]
    private static void ConvertWhenNeeded()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isUpdating || EditorApplication.isCompiling) return;
            foreach (Material m in Materials(PolygonTrees)) if (IsBuiltIn(m)) { Convert(); return; }
            foreach (Material m in Materials(IL3DN))
                if ((System.Array.IndexOf(IL3DNGrass, m.name) >= 0 || System.Array.IndexOf(IL3DNLit, m.name) >= 0) &&
                    m.shader != null && m.shader.name.StartsWith("IL3DN/")) { Convert(); return; }
        };
    }

    private static bool IsBuiltIn(Material m)
    {
        string shader = m.shader != null ? m.shader.name : "";
        return shader == "Standard" || shader.StartsWith("Nature/") || shader.StartsWith("Legacy Shaders/") ||
               shader == "Hidden/InternalErrorShader";
    }

    [MenuItem("Tools/Chick/Materials/Convert polygonTrees and IL3DN to URP")]
    private static void Convert()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Shader foliage = Shader.Find("Chick/InteractiveFoliage");
        if (lit == null || foliage == null)
        {
            EditorUtility.DisplayDialog("Convert materials", "URP Lit or Chick/InteractiveFoliage shader not found.", "OK");
            return;
        }

        var changed = new List<string>();
        foreach (Material m in Materials(PolygonTrees))
        {
            if (!IsBuiltIn(m)) continue;
            string shader = m.shader != null ? m.shader.name : "";
            bool leaves = shader.Contains("Leaves") || m.name.ToLowerInvariant().Contains("leaves");
            ToLit(m, lit, leaves);
            changed.Add(m.name + " (" + shader + " -> URP Lit)");
        }
        foreach (Material m in Materials(IL3DN))
        {
            if (m.shader == null || !m.shader.name.StartsWith("IL3DN/")) continue;   // already converted
            if (System.Array.IndexOf(IL3DNGrass, m.name) >= 0) { ToLit(m, foliage, true); changed.Add(m.name + " -> InteractiveFoliage"); }
            else if (System.Array.IndexOf(IL3DNLit, m.name) >= 0) { ToLit(m, lit, m.name == IL3DNLeaves); changed.Add(m.name + " -> URP Lit"); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Converted {changed.Count} materials to URP:\n" + string.Join("\n", changed));
    }

    private static IEnumerable<Material> Materials(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder)) yield break;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m != null) yield return m;
        }
    }

    // Moves a Built-in material onto a URP Lit-style shader, keeping what it looked like. Cut-out and double-sided
    // for foliage (alpha-tested cards and leaf shells are seen from both sides).
    private static void ToLit(Material m, Shader target, bool foliage)
    {
        Texture main = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
        Vector2 scale = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one;
        Vector2 offset = m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero;
        Color colour = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
        float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f;
        Texture normal = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
        float smoothness = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0.1f;
        float metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f;
        bool emissive = m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor");
        Color emission = emissive ? m.GetColor("_EmissionColor") : Color.black;
        bool cutOut = foliage && main != null;   // an untextured leaf shell has nothing to cut out

        Undo.RecordObject(m, "Convert materials to URP");
        m.shader = target;
        m.SetTexture("_BaseMap", main);
        m.SetTextureScale("_BaseMap", scale);
        m.SetTextureOffset("_BaseMap", offset);
        colour.a = 1f;
        m.SetColor("_BaseColor", colour);
        m.SetFloat("_Cutoff", cutoff);
        m.SetFloat("_AlphaClip", cutOut ? 1f : 0f);
        m.SetFloat("_Surface", 0f);
        m.SetFloat("_Cull", foliage ? 0f : 2f);
        m.SetFloat("_WorkflowMode", 1f);
        m.SetFloat("_Smoothness", Mathf.Min(smoothness, 0.5f));
        m.SetFloat("_Metallic", metallic);
        m.SetTexture("_BumpMap", normal);
        m.SetColor("_EmissionColor", emission);
        if (emissive) m.EnableKeyword("_EMISSION"); else m.DisableKeyword("_EMISSION");
        // URP's own inspector logic sets the keywords, render queue and blend state from those properties.
        BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords);
        EditorUtility.SetDirty(m);
    }
}
