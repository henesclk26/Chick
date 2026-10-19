using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Scatters the project's own small stones (Synty PolygonFarm
    // SM_Generic_Small_Rocks_01-04: little groups of flat stones lying in the ground) over the farm's bare ground:
    // along and on the trodden paths, on the earth, sand and pebble paint, and at the foot of the fences. Never in the
    // tilled field or the pond. They are flat and carry no collider, so the chick walks over them. Re-running replaces
    // the earlier set.
    public static class SmallStonesV1
    {
        const string Menu = "Tools/Chick/Ground/";
        const string Group = "09 Small Stones";
        static readonly string[] Small = { "Assets/Synty/PolygonFarm/Prefabs/Generic/SM_Generic_Small_Rocks_03.prefab", "Assets/Synty/PolygonFarm/Prefabs/Generic/SM_Generic_Small_Rocks_04.prefab" };
        static readonly string[] Wide = { "Assets/Synty/PolygonFarm/Prefabs/Generic/SM_Generic_Small_Rocks_01.prefab", "Assets/Synty/PolygonFarm/Prefabs/Generic/SM_Generic_Small_Rocks_02.prefab" };
        const int SmallCount = 230, WideCount = 45;
        const float MinGap = 1.6f;

        [MenuItem(Menu + "Scatter Small Stones (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map") ?? throw new InvalidOperationException("Farm Reference Map missing.");
            var terrain = root.GetComponentInChildren<Terrain>() ?? throw new InvalidOperationException("Farm terrain missing.");
            var data = terrain.terrainData;
            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Scatter small stones"); int undo = Undo.GetCurrentGroup();
            var old = root.transform.Find(Group);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var group = new GameObject(Group);
            Undo.RegisterCreatedObjectUndo(group, "Scatter small stones");
            group.transform.SetParent(root.transform, false);

            // the farm and a margin round it
            var fences = GameObject.Find("Farm Reference Map/04 Fences and Gates/Fence Bays");
            var fenceBounds = new Bounds(new Vector3(94, -12, -14), new Vector3(90, 10, 120));
            var shore = PondRelocationV1.NewShoreline();
            var rng = new System.Random(4711);
            var placed = new List<Vector2>();
            int small = 0, wide = 0, tries = 0;
            while ((small < SmallCount || wide < WideCount) && tries++ < 40000)
            {
                bool isWide = wide < WideCount && (small >= SmallCount || rng.NextDouble() < .17);
                float x = fenceBounds.min.x + (float)rng.NextDouble()*fenceBounds.size.x;
                float z = fenceBounds.min.z + (float)rng.NextDouble()*fenceBounds.size.z;
                var p = new Vector2(x, z);
                if (placed.Exists(q => (q - p).sqrMagnitude < MinGap*MinGap)) continue;
                var w = Weights(terrain, data, x, z);
                if (w == null) continue;
                float bare = w[4] + w[5] + w[6] + w[13] + w[14] + .6f*w[15];
                if (w[8] > .2f) continue;                                       // not in the tilled field
                if (FarmPondV1.Signed(shore, x, z) < .6f) continue;            // not in or at the water
                // most on bare ground, a few out in the grass by the fences
                double chance = Mathf.Clamp01((bare - .25f)*1.8f) + (NearFence(fences, p) ? .25 : .02);
                if (rng.NextDouble() > chance) continue;
                float y = terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y;
                if (Physics.Raycast(new Vector3(x, y + .2f, z), Vector3.down, out var hit, .4f, ~LayerMask.GetMask("CameraBlocker"), QueryTriggerInteraction.Ignore)
                    && !(hit.collider is TerrainCollider)) continue;               // not under posts, trunks or buildings

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>((isWide ? Wide : Small)[rng.Next(2)]);
                var stone = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                Undo.RegisterCreatedObjectUndo(stone, "Scatter small stones");
                Vector3 normal = data.GetInterpolatedNormal((x - terrain.transform.position.x)/data.size.x, (z - terrain.transform.position.z)/data.size.z);
                stone.transform.position = new Vector3(x, y - .015f, z);
                stone.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal)*Quaternion.Euler(0, (float)rng.NextDouble()*360f, 0);
                float scale = Mathf.Lerp(.65f, 1.15f, (float)rng.NextDouble());
                stone.transform.localScale = Vector3.one*scale;
                foreach (var c in stone.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
                GameObjectUtility.SetStaticEditorFlags(stone, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                placed.Add(p);
                if (isWide) wide++; else small++;
            }
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(root.scene);
            return "Small stones scattered: " + small + " small groups and " + wide + " wider beds under " + Group + ".";
        }

        static float[] Weights(Terrain terrain, TerrainData data, float x, float z)
        {
            int res = data.alphamapResolution;
            int ax = Mathf.FloorToInt((x - terrain.transform.position.x)/data.size.x*res), az = Mathf.FloorToInt((z - terrain.transform.position.z)/data.size.z*res);
            if (ax < 0 || az < 0 || ax >= res || az >= res) return null;
            var a = data.GetAlphamaps(ax, az, 1, 1);
            var w = new float[16];
            for (int l = 0; l < Mathf.Min(16, a.GetLength(2)); l++) w[l] = a[0, 0, l];
            return w;
        }

        static bool NearFence(GameObject bays, Vector2 p)
        {
            if (bays == null) return false;
            foreach (Transform bay in bays.transform)
            {
                var a = bay.position; var f = bay.GetComponent<MeshFilter>();
                if (f == null) continue;
                var b = bay.TransformPoint(new Vector3(f.sharedMesh.bounds.min.x, 0, 0));
                Vector2 a2 = new Vector2(a.x, a.z), b2 = new Vector2(b.x, b.z), ab = b2 - a2;
                float t = Mathf.Clamp01(Vector2.Dot(p - a2, ab)/Mathf.Max(ab.sqrMagnitude, 1e-4f));
                if ((a2 + ab*t - p).sqrMagnitude < 1.2f*1.2f) return true;
            }
            return false;
        }
    }
}
