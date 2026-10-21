using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Matches the IL3DN grass clumps to the ground they stand on:
    //  - two prefab variants with livelier colours that suit the handpainted ground (Assets/Art/Grass): Chick_Grass_Green
    //    (of IL3DN_Plant_Grass_01) and Chick_Grass_Dry (of IL3DN_Plant_Grass_02); the IL3DN files are not changed;
    //  - every IL3DN grass clump in the scene becomes the green one on the green ground and the dry one where the ground
    //    is earthy (path fringes, earth patches); on the bare middle of a path only a few dry clumps stay;
    //  - a scatter of extra dry clumps along the earth fringes, under "Farm IL3DN Grass".
    // One undo step. Re-running starts from what is in the scene (it also recognises its own variants).
    public static class GrassByGroundV1
    {
        const string Menu = "Tools/Chick/Ground/";
        const string Folder = "Assets/Art/Grass/";
        const string GreenSource = "Assets/IL3DN/Graphics/Prefabs/IL3DN_Plant_Grass_01.prefab";
        const string DrySource = "Assets/IL3DN/Graphics/Prefabs/IL3DN_Plant_Grass_02.prefab";
        const string GreenVariant = Folder + "Chick_Grass_Green.prefab", DryVariant = Folder + "Chick_Grass_Dry.prefab";
        const string ExtraName = "Fringe Dry Grass";
        static readonly Color GreenColor = new Color(.40f, .66f, .12f), DryColor = new Color(.86f, .68f, .26f);
        static readonly int[] Earth = { 4, 5, 6, 8, 13, 14, 15, 19 };
        const float DryFrom = .18f, BareFrom = .8f;
        const int ExtraClumps = 260;

        [MenuItem(Menu + "Grass By Ground (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map") ?? throw new InvalidOperationException("Farm Reference Map missing.");
            var terrain = root.GetComponentInChildren<Terrain>() ?? throw new InvalidOperationException("Farm terrain missing.");
            var green = Variant(GreenSource, GreenVariant, "Chick_Grass_Green", GreenColor);
            var dry = Variant(DrySource, DryVariant, "Chick_Grass_Dry", DryColor);
            var data = terrain.terrainData; int res = data.alphamapResolution;
            var alpha = data.GetAlphamaps(0, 0, res, res);

            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Grass by ground"); int undo = Undo.GetCurrentGroup();

            // the clumps already in the scene
            var srcGreen = AssetDatabase.LoadAssetAtPath<GameObject>(GreenSource); var srcDry = AssetDatabase.LoadAssetAtPath<GameObject>(DrySource);
            var clumps = new List<GameObject>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) continue;
                var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                if (src == srcGreen || src == srcDry || src == green || src == dry) clumps.Add(t.gameObject);
            }
            int toGreen = 0, toDry = 0, removed = 0;
            Transform container = null;
            foreach (var go in clumps)
            {
                var p = go.transform.position;
                float e = EarthAt(terrain, alpha, res, p);
                if (container == null && go.transform.parent != null && go.transform.parent.name.Contains("IL3DN Grass")) container = go.transform.parent;
                if (e >= BareFrom && Hash(p) > .3f) { Undo.DestroyObjectImmediate(go); removed++; continue; }
                var want = e >= DryFrom ? dry : green;
                if (PrefabUtility.GetCorrespondingObjectFromSource(go) != want) Swap(go, want);
                if (want == dry) toDry++; else toGreen++;
            }

            // extra dry clumps along the earth fringes
            if (container == null) { container = new GameObject("Farm IL3DN Grass").transform; Undo.RegisterCreatedObjectUndo(container.gameObject, "Grass by ground"); container.SetParent(root.transform, false); }
            var extra = container.Find(ExtraName);
            if (extra != null) Undo.DestroyObjectImmediate(extra.gameObject);
            extra = new GameObject(ExtraName).transform; Undo.RegisterCreatedObjectUndo(extra.gameObject, "Grass by ground"); extra.SetParent(container, false);
            int added = ScatterFringe(terrain, alpha, res, dry, extra);

            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(root.scene);
            return "Grass by ground: " + toGreen + " green, " + toDry + " dry, " + removed + " removed from bare path; " + added + " extra dry clumps on the fringes.";
        }

        static GameObject Variant(string sourcePath, string variantPath, string name, Color color)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art/Grass")) AssetDatabase.CreateFolder("Assets/Art", "Grass");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath) ?? throw new InvalidOperationException("Missing " + sourcePath);
            string matPath = Folder + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(source.GetComponentInChildren<Renderer>(true).sharedMaterial) { name = name };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);

            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
            if (variant == null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(source);
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = mat;
                inst.name = name;
                variant = PrefabUtility.SaveAsPrefabAsset(inst, variantPath);
                Object.DestroyImmediate(inst);
            }
            return variant;
        }

        static void Swap(GameObject old, GameObject prefab)
        {
            var t = old.transform;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, t.parent);
            Undo.RegisterCreatedObjectUndo(go, "Grass by ground");
            go.transform.SetLocalPositionAndRotation(t.localPosition, t.localRotation);
            go.transform.localScale = t.localScale;
            go.transform.SetSiblingIndex(t.GetSiblingIndex());
            GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(old));
            go.SetActive(old.activeSelf);
            Undo.DestroyObjectImmediate(old);
        }

        static int ScatterFringe(Terrain terrain, float[,,] alpha, int res, GameObject dry, Transform parent)
        {
            var rng = new System.Random(5150);
            var data = terrain.terrainData; Vector3 tp = terrain.transform.position, size = data.size;
            var area = Rect.MinMaxRect(34f, -77f, 158f, 60f);
            var seedYard = Rect.MinMaxRect(101f, 10f, 121f, 31f);
            var shore = PondRelocationV1.NewShoreline();
            var placed = new List<Vector2>();
            var buffer = new Collider[32];
            int count = 0;
            for (int tries = 0; tries < 60000 && count < ExtraClumps; tries++)
            {
                var c = new Vector2(Mathf.Lerp(area.xMin, area.xMax, (float)rng.NextDouble()), Mathf.Lerp(area.yMin, area.yMax, (float)rng.NextDouble()));
                float e = EarthAt(terrain, alpha, res, new Vector3(c.x, 0, c.y));
                if (e < .22f || e > .65f || TilledAt(terrain, alpha, res, c) > .1f) continue;
                int n = rng.Next(1, 4);
                for (int k = 0; k < n && count < ExtraClumps; k++)
                {
                    var p = k == 0 ? c : c + new Vector2((float)rng.NextDouble() - .5f, (float)rng.NextDouble() - .5f)*2.2f;
                    if (seedYard.Contains(p) || FarmPondV1.Signed(shore, p.x, p.y) < .8f) continue;
                    if (placed.Exists(q => (q - p).sqrMagnitude < 1.1f*1.1f)) continue;
                    float pe = EarthAt(terrain, alpha, res, new Vector3(p.x, 0, p.y));
                    if (pe < .18f || pe > .7f) continue;
                    float y = terrain.SampleHeight(new Vector3(p.x, 0, p.y)) + tp.y;
                    int hits = Physics.OverlapSphereNonAlloc(new Vector3(p.x, y + .35f, p.y), .35f, buffer, ~0, QueryTriggerInteraction.Ignore);
                    bool blocked = false;
                    for (int h = 0; h < hits; h++) if (!(buffer[h] is TerrainCollider)) { blocked = true; break; }
                    if (blocked) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(dry, parent);
                    Undo.RegisterCreatedObjectUndo(go, "Grass by ground");
                    var normal = data.GetInterpolatedNormal((p.x - tp.x)/size.x, (p.y - tp.z)/size.z);
                    go.transform.SetPositionAndRotation(new Vector3(p.x, y - .02f, p.y),
                        Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, normal, .4f))*Quaternion.Euler(0, (float)rng.NextDouble()*360f, 0));
                    go.transform.localScale = Vector3.one*Mathf.Lerp(.6f, .95f, (float)rng.NextDouble());
                    placed.Add(p); count++;
                }
            }
            return count;
        }

        static float EarthAt(Terrain terrain, float[,,] alpha, int res, Vector3 p)
        {
            Cell(terrain, res, new Vector2(p.x, p.z), out int x, out int z);
            float e = 0; foreach (int l in Earth) e += alpha[z, x, l];
            return e;
        }

        static float TilledAt(Terrain terrain, float[,,] alpha, int res, Vector2 p)
        {
            Cell(terrain, res, p, out int x, out int z);
            return alpha[z, x, 8];
        }

        static void Cell(Terrain terrain, int res, Vector2 p, out int x, out int z)
        {
            var d = terrain.terrainData; var tp = terrain.transform.position;
            x = Mathf.Clamp(Mathf.FloorToInt((p.x - tp.x)/d.size.x*res), 0, res - 1);
            z = Mathf.Clamp(Mathf.FloorToInt((p.y - tp.z)/d.size.z*res), 0, res - 1);
        }

        static float Hash(Vector3 p) => Mathf.Repeat(Mathf.Sin(p.x*12.9898f + p.z*78.233f)*43758.5453f, 1f);
    }
}
