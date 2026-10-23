using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Scatters eleven flower and small plant models (BluBlu, Emilulz, PolyRonin
    // packs) evenly and sparingly over the farm's grass, so they bend under the chick like the grass and the meadow
    // flowers do:
    //  - materials: one Chick/InteractiveFoliage copy per source material (Assets/Art/Flowers), same texture, colour,
    //    cut-out and culling, so they take the player's bend (FoliageBend); the pack materials are not touched;
    //  - prefabs: a variant of each pack prefab using those materials (Assets/Art/Flowers/Prefabs);
    //  - placement: inside the fenced farm only (the outer rim is never seen up close), on grassy ground (no paths,
    //    earth patches, tilled field or pond), clear of fences, buildings, rocks and trunks, at least Spacing apart and a
    //    little apart from the flowers already there; each kind scaled to the height of the farm's own flowers.
    // One undo step; "Remove Scattered Flowers (V1)" deletes the group again.
    public static class FlowerScatterV1
    {
        const string Menu = "Tools/Chick/Ground/";
        internal const string Folder = "Assets/Art/Flowers", PrefabFolder = Folder + "/Prefabs";
        const string GroupName = "Scattered Flowers (V1)";
        const float Spacing = 2.4f, ClearOfOthers = .6f, ClearOfSolid = .45f;
        const int Target = 170, Seed = 9137;
        static readonly Rect Farm = Rect.MinMaxRect(59.6f, -31.4f, 128.4f, 35.4f);   // inside the outer fence
        static readonly int[] Earth = { 4, 5, 6, 13, 14, 15, 19 };
        const int Tilled = 8;

        internal sealed class Kind
        {
            public string source; public float height, weight;
            public Kind(string s, float h, float w) { source = s; height = h; weight = w; }
        }

        // source prefab, height on the farm (m), share
        internal static readonly Kind[] Kinds =
        {
            new Kind("Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/SM_Foliage_01.prefab", .42f, 2f),
            new Kind("Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/SM_Flower_01.prefab", .5f, 2f),
            new Kind("Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/SM_Flower_02.prefab", .5f, 2f),
            new Kind("Assets/Emilulz_Assets/DEMOLowPolyFlowers/Prefabs/SM_Daisy_Single.prefab", .62f, 2f),
            new Kind("Assets/Emilulz_Assets/DEMOLowPolyFlowers/Prefabs/SM_Dandelion_Small.prefab", .55f, 1.5f),
            new Kind("Assets/PolyRonin/Pine Forest Pack/Prefabs/Flower1.prefab", .6f, 1.5f),
            new Kind("Assets/PolyRonin/Pine Forest Pack/Prefabs/Flower2.prefab", .6f, 1.5f),
            new Kind("Assets/PolyRonin/Pine Forest Pack/Prefabs/Flower3.prefab", .5f, 1.5f),
            new Kind("Assets/Emilulz_Assets/DEMOLowPolyFlowers/Prefabs/SM_Hyacinth_PastellBlue_Big.prefab", .72f, 1f),
            new Kind("Assets/Emilulz_Assets/DEMOLowPolyFlowers/Prefabs/SM_Rose_Red_Open.prefab", .7f, .7f),
            new Kind("Assets/Emilulz_Assets/DEMOLowPolyFlowers/Prefabs/SM_Rose_Black_Open.prefab", .7f, .5f),
        };

        [MenuItem(Menu + "Scatter Flowers (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        [MenuItem(Menu + "Remove Scattered Flowers (V1)")]
        static void MenuRemove()
        {
            var group = GameObject.Find(GroupName);
            if (group != null) Undo.DestroyObjectImmediate(group);
            Debug.Log(group != null ? "Scattered flowers removed." : "No scattered flowers.");
        }

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map") ?? throw new InvalidOperationException("Farm Reference Map missing.");
            var terrain = root.GetComponentInChildren<Terrain>() ?? throw new InvalidOperationException("Farm terrain missing.");
            if (GameObject.Find(GroupName) != null) throw new InvalidOperationException(GroupName + " already exists; remove it first.");
            EnsureFolder(Folder); EnsureFolder(PrefabFolder);

            // 1. bendable materials and prefab variants
            var converted = new Dictionary<Material, Material>();
            var prefabs = new GameObject[Kinds.Length];
            var heights = new float[Kinds.Length];
            for (int k = 0; k < Kinds.Length; k++)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Kinds[k].source) ?? throw new InvalidOperationException("Missing " + Kinds[k].source);
                prefabs[k] = Variant(source, converted);
                heights[k] = ModelHeight(prefabs[k]);
            }

            // 2. what is already there: flowers and small plants to keep a little apart from
            var others = new List<Vector3>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                string n = r.name + (r.transform.parent ? r.transform.parent.name : "");
                if (n.IndexOf("Flower", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Mushroom", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Fern", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Plant", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Grass", StringComparison.OrdinalIgnoreCase) < 0)
                    others.Add(r.bounds.center);
            }

            // 3. scatter
            var parentOf = GameObject.Find("Flowers")?.transform;
            var group = new GameObject(GroupName).transform;
            if (parentOf != null) group.SetParent(parentOf, false);
            Undo.RegisterCreatedObjectUndo(group.gameObject, "Scatter flowers (V1)");

            var data = terrain.terrainData; Vector3 tp = terrain.transform.position; int res = data.alphamapResolution;
            var rng = new System.Random(Seed);
            float totalWeight = 0; foreach (var kd in Kinds) totalWeight += kd.weight;
            var placed = new List<Vector3>();
            var count = new int[Kinds.Length];
            int attempts = 0, rejectedGround = 0, rejectedSolid = 0;
            while (placed.Count < Target && attempts++ < Target*400)
            {
                var p = new Vector3(Lerp(Farm.xMin, Farm.xMax, rng.NextDouble()), 0, Lerp(Farm.yMin, Farm.yMax, rng.NextDouble()));
                if (Near(placed, p, Spacing) || Near(others, p, ClearOfOthers)) continue;
                var a = data.GetAlphamaps(Mathf.Clamp((int)((p.x - tp.x)/data.size.x*res), 0, res - 1), Mathf.Clamp((int)((p.z - tp.z)/data.size.z*res), 0, res - 1), 1, 1);
                float earth = 0; foreach (int l in Earth) earth += a[0, 0, l];
                if (earth > .25f || a[0, 0, Tilled] > .05f || FarmWaterBody.Find(p + Vector3.up*20f, out _) != null) { rejectedGround++; continue; }
                p.y = terrain.SampleHeight(p) + tp.y;
                if (Solid(p)) { rejectedSolid++; continue; }

                double pick = rng.NextDouble()*totalWeight; int kind = 0;
                for (; kind < Kinds.Length - 1; kind++) { pick -= Kinds[kind].weight; if (pick <= 0) break; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[kind], group);
                float scale = Kinds[kind].height/Mathf.Max(heights[kind], .01f)*Lerp(.85f, 1.15f, rng.NextDouble());
                go.transform.localScale = Vector3.one*scale;
                go.transform.SetPositionAndRotation(p - Vector3.up*.01f, Quaternion.Euler(0, (float)rng.NextDouble()*360f, 0));
                Undo.RegisterCreatedObjectUndo(go, "Scatter flowers (V1)");
                placed.Add(p); count[kind]++;
            }

            EditorSceneManager.MarkSceneDirty(root.scene);
            var report = new System.Text.StringBuilder("Scattered " + placed.Count + " flowers (" + Spacing + " m apart) on the farm's grass under " + GroupName + ": ");
            for (int k = 0; k < Kinds.Length; k++) report.Append(prefabs[k].name + " " + count[k] + ", ");
            report.Append("materials " + converted.Count + " (" + Folder + "); rejected: ground " + rejectedGround + ", solid " + rejectedSolid + ".");
            return report.ToString();
        }

        // a variant of the pack prefab with bendable materials (made once, reused)
        internal static GameObject Variant(GameObject source, Dictionary<Material, Material> converted)
        {
            string path = PrefabFolder + "/Chick_" + source.name + ".prefab";
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                foreach (var r in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) if (mats[i] != null) mats[i] = Bendable(mats[i], converted);
                    r.sharedMaterials = mats;
                }
                instance.name = "Chick_" + source.name;
                return PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        // Chick/InteractiveFoliage copy of a pack material: same texture and colour, cut-out and culling as the pack
        // draws it (the BluBlu shader graphs and URP Lit alike)
        static Material Bendable(Material source, Dictionary<Material, Material> converted)
        {
            if (converted.TryGetValue(source, out var done)) return done;
            // the BluBlu LOD material shares its atlas with the full one: one material for both
            string key = source.name.Replace("_LOD", "");
            foreach (var kv in converted) if (kv.Key.name.Replace("_LOD", "") == key && kv.Key.shader.name.StartsWith("BluBlu") == source.shader.name.StartsWith("BluBlu")) { converted[source] = kv.Value; return kv.Value; }

            string path = Folder + "/Chick_" + key + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Chick/InteractiveFoliage") ?? throw new InvalidOperationException("Chick/InteractiveFoliage missing.");
            if (mat == null) { mat = new Material(shader) { name = "Chick_" + key }; AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader;
            Texture tex = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.HasProperty("Texture2D_CAD82441") ? source.GetTexture("Texture2D_CAD82441") : null;
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.white);
            bool clip = source.IsKeywordEnabled("_ALPHATEST_ON");
            mat.SetFloat("_AlphaClip", clip ? 1 : 0);
            if (clip) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cutoff", source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : .5f);
            // pack foliage is drawn from both sides; URP Lit materials keep their own setting
            mat.SetFloat("_Cull", source.HasProperty("_Cull") ? source.GetFloat("_Cull") : 0);
            mat.SetFloat("_Smoothness", source.HasProperty("_Smoothness") ? Mathf.Min(source.GetFloat("_Smoothness"), .25f) : .05f);
            mat.renderQueue = clip ? 2450 : -1;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            converted[source] = mat;
            return mat;
        }

        internal static float ModelHeight(GameObject prefab)
        {
            float top = float.NegativeInfinity, bottom = float.PositiveInfinity;
            foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null || f.name.Contains("LOD1") || f.name.Contains("LOD2")) continue;
                var b = f.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    float y = f.transform.TransformPoint(c).y - prefab.transform.position.y;
                    top = Mathf.Max(top, y); bottom = Mathf.Min(bottom, y);
                }
            }
            return Mathf.Max(top - bottom, .01f);
        }

        // fences, buildings, rocks, trunks: any solid collider other than the ground near the plant's base
        internal static bool Solid(Vector3 p, float clear = ClearOfSolid)
        {
            foreach (var c in Physics.OverlapSphere(p + Vector3.up*(clear + .05f), clear, ~0, QueryTriggerInteraction.Ignore))
                if (!(c is TerrainCollider)) return true;
            return false;
        }

        internal static bool Near(List<Vector3> points, Vector3 p, float r)
        {
            float r2 = r*r;
            foreach (var q in points) { float dx = q.x - p.x, dz = q.z - p.z; if (dx*dx + dz*dz < r2) return true; }
            return false;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        internal static float Lerp(float a, float b, double t) => a + (b - a)*(float)t;
    }
}
