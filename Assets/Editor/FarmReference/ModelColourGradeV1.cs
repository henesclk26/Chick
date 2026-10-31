using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// Gives the farm's models the lively colour the ground and the grass were given (their textures recoloured, the grass
/// a saturation and brightness lift), so they no longer look pale beside them. Every model material in the open scene
/// gets a graded copy of its colour texture (Assets/Art/ModelGrade/Textures, the original left untouched) or, without
/// one, a graded colour. The grade is a vibrance: dull colours are lifted most, already strong ones (the barn's red)
/// hardly, so nothing burns out; alpha (leaf cut-outs) is kept. Left out: the ground and the grass (never touched),
/// water, the waterfall, mountains, the scenery around the map and shadows. What each material had is kept in
/// Assets/Art/ModelGrade/Originals.json and "Restore Model Colours" puts it back. Runs in batches (Step) so big
/// textures do not pile up in memory.
/// </summary>
public static class ModelColourGradeV1
{
    private const string Folder = "Assets/Art/ModelGrade";
    private const string TextureFolder = Folder + "/Textures";
    private const string OriginalsPath = Folder + "/Originals.json";
    // Vibrance: a colour of saturation s is pushed away from its grey by 1 + Vibrance * (1 - s).
    public const float Vibrance = .45f;
    public const float Brightness = 1.03f;

    private static readonly string[] SkipShaders =
    {
        "Ground Rich Mesh", "Terrain", "Backdrop", "PondWater", "Waterfall", "Cave Interior", "MountainBackdrop",
        "DistantMountain", "ContactShadow", "Skybox",
    };
    private static readonly string[] ColourProperties = { "_BaseColor", "_Color", "baseColorFactor" };

    [System.Serializable] private class Entry { public string material, property, original; public bool colour; public Color was; }
    // a material inside a model file, replaced by its graded copy (a .mat of its own) where it is used
    [System.Serializable] private class Swap { public string original, copy; }
    [System.Serializable] private class Book { public List<Entry> entries = new List<Entry>(); public List<Swap> swaps = new List<Swap>(); }

    private const string MaterialFolder = Folder + "/Materials";

    /// <summary>
    /// The materials that live inside model files (the cows, the coop, the mailbox, the seeds): each gets a graded copy,
    /// put in their place on the scene's renderers and in the prefabs under Assets that use them.
    /// </summary>
    public static string GradeEmbedded()
    {
        Directory.CreateDirectory(MaterialFolder);
        var book = Load();
        var copies = new Dictionary<Material, Material>();
        foreach (var s in book.swaps)
            if (GlobalObjectId.TryParse(s.original, out var oid) && GlobalObjectId.GlobalObjectIdentifierToObjectSlow(oid) is Material o)
                copies[o] = AssetDatabase.LoadAssetAtPath<Material>(s.copy);

        Material CopyOf(Material original)
        {
            if (copies.TryGetValue(original, out var known) && known != null) return known;
            string path = AssetDatabase.GenerateUniqueAssetPath(MaterialFolder + "/" + Safe(original.name) + "_G.mat");
            var copy = new Material(original) { name = Path.GetFileNameWithoutExtension(path) };
            string texture = ColourTexture(original);
            if (texture != null) copy.SetTexture(texture, GradedCopy((Texture2D)original.GetTexture(texture)));
            else foreach (string c in ColourProperties)
                    if (copy.HasProperty(c)) { copy.SetColor(c, Grade(copy.GetColor(c))); break; }
            AssetDatabase.CreateAsset(copy, path);
            copies[original] = copy;
            book.swaps.Add(new Swap { original = GlobalObjectId.GetGlobalObjectIdSlow(original).ToString(), copy = path });
            return copy;
        }

        bool Replace(Renderer r)
        {
            var list = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < list.Length; i++)
            {
                var m = list[i];
                if (m == null || Skip(m)) continue;
                string path = AssetDatabase.GetAssetPath(m);
                if (!path.StartsWith("Assets/") || path.EndsWith(".mat")) continue;
                list[i] = CopyOf(m);
                changed = true;
            }
            if (changed) r.sharedMaterials = list;
            return changed;
        }

        int sceneCount = 0, prefabCount = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".prefab")) continue;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool uses = false;
            foreach (var r in asset.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && !Skip(m) && AssetDatabase.GetAssetPath(m) is string p && p.StartsWith("Assets/") && !p.EndsWith(".mat") &&
                        !PrefabUtility.IsPartOfModelPrefab(asset)) uses = true;
            if (!uses) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) changed |= Replace(r);
            if (changed) { PrefabUtility.SaveAsPrefabAsset(root, path); prefabCount++; }
            PrefabUtility.UnloadPrefabContents(root);
        }
        // the scene's renderers after the prefabs, so instances of a changed prefab simply follow it
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r is ParticleSystemRenderer || !Embedded(r)) continue;
            Undo.RecordObject(r, "Grade model colours");
            if (Replace(r)) sceneCount++;
        }
        Save(book);
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        return "embedded materials: " + copies.Count + " graded copies, " + sceneCount + " scene renderers, " + prefabCount + " prefabs.";
    }

    private static bool Embedded(Renderer r)
    {
        foreach (var m in r.sharedMaterials)
            if (m != null && !Skip(m) && AssetDatabase.GetAssetPath(m) is string p && p.StartsWith("Assets/") && !p.EndsWith(".mat")) return true;
        return false;
    }

    /// <summary>The graded copies put back to the model files' own materials, in the scene and the prefabs.</summary>
    private static int RestoreEmbedded(Book book)
    {
        var back = new Dictionary<Material, Material>();
        foreach (var s in book.swaps)
        {
            var copy = AssetDatabase.LoadAssetAtPath<Material>(s.copy);
            if (copy != null && GlobalObjectId.TryParse(s.original, out var oid) && GlobalObjectId.GlobalObjectIdentifierToObjectSlow(oid) is Material o)
                back[copy] = o;
        }
        if (back.Count == 0) return 0;
        bool Put(Renderer r)
        {
            var list = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < list.Length; i++)
                if (list[i] != null && back.TryGetValue(list[i], out var o)) { list[i] = o; changed = true; }
            if (changed) r.sharedMaterials = list;
            return changed;
        }
        int count = 0;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool has = false;
            foreach (var m in r.sharedMaterials) if (m != null && back.ContainsKey(m)) has = true;
            if (!has) continue;
            Undo.RecordObject(r, "Restore model colours");
            if (Put(r)) count++;
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            bool uses = false;
            foreach (string dep in AssetDatabase.GetDependencies(path, false))
                if (dep.StartsWith(MaterialFolder)) uses = true;
            if (!uses || !path.EndsWith(".prefab")) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) changed |= Put(r);
            if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        return count;
    }

    [MenuItem("Tools/Chick/Look/Grade Model Colours (V1)")]
    private static void MenuApply()
    {
        string report;
        while ((report = Step(12)).StartsWith("step")) Debug.Log(report);
        Debug.Log(report);
        Debug.Log(GradeEmbedded());
    }

    [MenuItem("Tools/Chick/Look/Restore Model Colours")]
    private static void MenuRestore() => Debug.Log(Restore());

    /// <summary>The materials to grade and the colour property each is graded through.</summary>
    public static List<(Material material, string property, bool colour)> Plan()
    {
        var plan = new List<(Material, string, bool)>();
        var seen = new HashSet<Material>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r is ParticleSystemRenderer || r.GetComponent<Terrain>() != null) continue;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || !seen.Add(m) || Skip(m)) continue;
                // only material files can keep a change (materials inside a model file are rebuilt on import)
                string path = AssetDatabase.GetAssetPath(m);
                if (!path.StartsWith("Assets/") || !path.EndsWith(".mat") || path.StartsWith(Folder + "/")) continue;
                string texture = ColourTexture(m);
                if (texture != null) { plan.Add((m, texture, false)); continue; }
                foreach (string c in ColourProperties)
                    if (m.HasProperty(c)) { plan.Add((m, c, true)); break; }
            }
        }
        return plan;
    }

    // not grass and not mountains, whatever their names or shaders suggest: ferns drawn as billboards, the pond's reeds
    private static readonly string[] Keep = { "Grass_6_Bilboard", "PondReeds" };

    private static bool Skip(Material m)
    {
        if (System.Array.IndexOf(Keep, m.name) >= 0) return false;
        if (m.name.Contains("Grass")) return true;   // the grass stays exactly as it is
        foreach (string s in SkipShaders) if (m.shader.name.Contains(s)) return true;
        return false;
    }

    private static string ColourTexture(Material m)
    {
        var s = m.shader;
        for (int i = 0; i < s.GetPropertyCount(); i++)
        {
            if (s.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
            string n = s.GetPropertyName(i), l = n.ToLowerInvariant();
            if (l.Contains("normal") || l.Contains("bump") || l.Contains("mask") || l.Contains("metal") || l.Contains("occlusion") ||
                l.Contains("emission") || l.Contains("rough") || l.Contains("detail") || l.Contains("noise") || l.Contains("wind")) continue;
            if ((l.Contains("base") || l.Contains("albedo") || l.Contains("main") || l.Contains("color") || l.Contains("diffuse")) && m.GetTexture(n) is Texture2D)
                return n;
        }
        // Shader Graph materials with generated property names: the texture's own name tells
        for (int i = 0; i < s.GetPropertyCount(); i++)
        {
            if (s.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
            string n = s.GetPropertyName(i);
            if (!(m.GetTexture(n) is Texture2D t)) continue;
            string l = t.name.ToLowerInvariant();
            if (l.Contains("albedo") || n == "Texture2D_CAD82441") return n;   // BluBlu foliage's colour slot
            if (l.Contains("normal") || l.EndsWith("_e") || l.EndsWith("_ao")) continue;
            if (l.Contains("albedo") || l.Contains("bca") || l.Contains("basecolor") || l.Contains("diffuse") || l.Contains("atlas") || l.Contains("colors"))
                return n;
        }
        return null;
    }

    /// <summary>Grades up to <paramref name="count"/> materials not yet graded; "step ..." while more are left.</summary>
    public static string Step(int count)
    {
        Directory.CreateDirectory(TextureFolder);
        var book = Load();
        var done = new HashSet<string>();
        foreach (var e in book.entries) done.Add(e.material);
        var plan = Plan();
        int graded = 0, left = 0;
        var report = new StringBuilder();
        foreach (var (material, property, colour) in plan)
        {
            string id = GlobalObjectId.GetGlobalObjectIdSlow(material).ToString();
            if (done.Contains(id)) continue;
            if (graded >= count) { left++; continue; }
            var entry = new Entry { material = id, property = property, colour = colour };
            if (colour)
            {
                entry.was = material.GetColor(property);
                material.SetColor(property, Grade(entry.was));
            }
            else
            {
                var original = (Texture2D)material.GetTexture(property);
                entry.original = GlobalObjectId.GetGlobalObjectIdSlow(original).ToString();
                material.SetTexture(property, GradedCopy(original));
            }
            EditorUtility.SetDirty(material);
            book.entries.Add(entry);
            done.Add(id);
            graded++;
            report.Append(' ').Append(material.name);
        }
        Save(book);
        AssetDatabase.SaveAssets();
        return (left > 0 ? "step: " : "done: ") + graded + " graded, " + left + " left." + report;
    }

    /// <summary>Takes back grades given to this tool's own graded copies (which are graded already), keeping the rest.</summary>
    public static string UndoDoubleGrades()
    {
        var book = Load();
        int undone = 0;
        for (int i = book.entries.Count - 1; i >= 0; i--)
        {
            var e = book.entries[i];
            if (!GlobalObjectId.TryParse(e.material, out var mid)) continue;
            var material = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(mid) as Material;
            if (material == null || !AssetDatabase.GetAssetPath(material).StartsWith(Folder + "/")) continue;
            if (e.colour) material.SetColor(e.property, e.was);
            else if (GlobalObjectId.TryParse(e.original, out var tid))
                material.SetTexture(e.property, GlobalObjectId.GlobalObjectIdentifierToObjectSlow(tid) as Texture);
            EditorUtility.SetDirty(material);
            book.entries.RemoveAt(i);
            undone++;
        }
        Save(book);
        AssetDatabase.SaveAssets();
        return "double grades taken back: " + undone;
    }

    public static string Restore()
    {
        var book = Load();
        int restored = 0;
        int swapped = RestoreEmbedded(book);
        foreach (var e in book.entries)
        {
            if (!GlobalObjectId.TryParse(e.material, out var mid)) continue;
            var material = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(mid) as Material;
            if (material == null) continue;
            if (e.colour) material.SetColor(e.property, e.was);
            else if (GlobalObjectId.TryParse(e.original, out var tid))
                material.SetTexture(e.property, GlobalObjectId.GlobalObjectIdentifierToObjectSlow(tid) as Texture);
            EditorUtility.SetDirty(material);
            restored++;
        }
        AssetDatabase.DeleteAsset(OriginalsPath);
        AssetDatabase.SaveAssets();
        return "Model colours restored on " + restored + " materials and " + swapped + " renderers (the graded textures stay in " + Folder + ").";
    }

    /// <summary>The colour graded: dull colours lifted most, strong ones hardly, alpha kept.</summary>
    public static Color Grade(Color c)
    {
        float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
        float saturation = max > 1e-4f ? (max - min) / max : 0f;
        float lift = 1f + Vibrance * (1f - saturation);
        float grey = c.r * .299f + c.g * .587f + c.b * .114f;
        return new Color(
            Mathf.Clamp01((grey + (c.r - grey) * lift) * Brightness),
            Mathf.Clamp01((grey + (c.g - grey) * lift) * Brightness),
            Mathf.Clamp01((grey + (c.b - grey) * lift) * Brightness), c.a);
    }

    /// <summary>The graded copy of a texture (made once, shared by every material that uses the texture).</summary>
    private static Texture2D GradedCopy(Texture2D source)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);
        string key = source.name + "_" + Mathf.Abs((sourcePath + "/" + source.name).GetHashCode()).ToString("x");
        string path = TextureFolder + "/" + Safe(key) + "_G.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        // read through the GPU, so compressed and unreadable textures work and the stored (gamma) values come back
        bool srgb = GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat);
        var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32,
                                            srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
        Graphics.Blit(source, rt);
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, !srgb);
        copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        var pixels = copy.GetPixels32();
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Grade(pixels[i]);
        copy.SetPixels32(pixels);
        File.WriteAllBytes(path, copy.EncodeToPNG());
        Object.DestroyImmediate(copy);

        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (AssetImporter.GetAtPath(sourcePath) is TextureImporter from)
        {
            var settings = new TextureImporterSettings();
            from.ReadTextureSettings(settings);
            importer.SetTextureSettings(settings);
            importer.maxTextureSize = from.maxTextureSize;
            importer.textureCompression = from.textureCompression;
            importer.crunchedCompression = from.crunchedCompression;
            importer.compressionQuality = from.compressionQuality;
        }
        else
        {
            importer.sRGBTexture = srgb;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = source.mipmapCount > 1;
            importer.wrapMode = source.wrapMode;
            importer.filterMode = source.filterMode;
            importer.maxTextureSize = Mathf.Max(source.width, source.height);
        }
        importer.textureType = TextureImporterType.Default;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Color32 Grade(Color32 c) => (Color32)Grade((Color)c);

    private static string Safe(string name)
    {
        foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
        return name.Replace(' ', '_');
    }

    private static Book Load() =>
        File.Exists(OriginalsPath) ? JsonUtility.FromJson<Book>(File.ReadAllText(OriginalsPath)) : new Book();

    private static void Save(Book book)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(OriginalsPath, JsonUtility.ToJson(book, true));
        AssetDatabase.ImportAsset(OriginalsPath);
    }
}
