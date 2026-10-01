using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Replaces the raised crop boxes with a procedural corn field,
    // a natural pumpkin patch and a ripe wheat field on tilled soil; corn and wheat sway in the wind
    // (Chick/CropWind). Repeatable: rebuilds its own group.
    public static class MixedCropFieldsV1
    {
        const string Folder = "Assets/Art/FarmReference/Fields";
        const string GroupName = "Mixed Crop Fields";
        // World-space soil areas (x0, x1, z0, z1): corn + pumpkin patch west, wheat east.
        static readonly Vector4 West = new Vector4(66.3f, 91.4f, -25.2f, -8.6f);
        static readonly Vector4 East = new Vector4(95.4f, 120.6f, -25.2f, -8.6f);
        const float LaneZ = -16.9f, LaneHalf = .8f;  // walkable lane through the wheat

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var fields = GameObject.Find("Farm Reference Map/02 Crop Fields");
            if (fields == null) throw new InvalidOperationException("Crop fields group missing.");
            var terrain = fields.transform.root.GetComponentInChildren<Terrain>();
            var vine = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BOKI/LowPolyNature/Prefabs/models/bush_1.prefab");
            var pumpkin = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Mnostva Art/Food/Prefabs/Food/Pumpkin.prefab");
            if (terrain == null || vine == null || pumpkin == null) throw new InvalidOperationException("Field sources missing.");
            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Apply mixed crop fields V1");

            var old = fields.transform.Find(GroupName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            // The raised beds stay in the scene, disabled, for easy rollback.
            foreach (Transform c in fields.transform)
                if (c.name.Contains(" Bed ") && c.gameObject.activeSelf) { Undo.RecordObject(c.gameObject, "Hide raised bed"); c.gameObject.SetActive(false); }
            var group = new GameObject(GroupName); Undo.RegisterCreatedObjectUndo(group, "Create mixed crop fields");
            group.transform.SetParent(fields.transform, true);

            PaintSoil(terrain);
            float ty = terrain.transform.position.y;
            Func<float, float, float> ground = (x, z) => ty + terrain.SampleHeight(new Vector3(x, 0, z));
            var rng = new System.Random(11);
            Func<float, float, float> range = (a, b) => a + (float)rng.NextDouble()*(b-a);

            // Corn: procedural plants in rows along x, merged into chunk meshes with the same wind shader.
            var cornGroup = Child(group, "Corn Field");
            int plants;
            var cornChunks = BuildCorn(ground, range, out plants);
            var cornMaterial = CropMaterial("CornPlant", Color.white, .11f, 1.1f, .25f, .03f);
            for (int i = 0; i < cornChunks.Count; i++)
            {
                var g = Child(cornGroup, "Corn Chunk " + i);
                g.AddComponent<MeshFilter>().sharedMesh = cornChunks[i];
                var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = cornMaterial; r.shadowCastingMode = ShadowCastingMode.Off;
            }

            // Pumpkin patch: low vine mounds in rows with pumpkins resting between them.
            var patch = Child(group, "Pumpkin Patch"); int vines = 0, pumpkins = 0;
            for (float z = -10.2f; z > -24.5f; z -= 2.3f) for (float x = 81.2f; x < 90.8f; x += range(.9f, 1.3f))
            {
                var v = (GameObject)PrefabUtility.InstantiatePrefab(vine, patch.transform);
                v.name = "Pumpkin Vine " + vines++.ToString("000");
                float vx = x, vz = z + range(-.35f, .35f);
                v.transform.SetPositionAndRotation(new Vector3(vx, ground(vx, vz), vz), Quaternion.Euler(0, range(0, 360), 0));
                v.transform.localScale = new Vector3(range(.35f, .5f), range(.16f, .24f), range(.35f, .5f));
                foreach (var c in v.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
                SetStatic(v);
                if (rng.NextDouble() >= .45) continue;
                var p = (GameObject)PrefabUtility.InstantiatePrefab(pumpkin, patch.transform);
                p.name = "Patch Pumpkin " + pumpkins++.ToString("000");
                float px = x + range(-.4f, .4f), pz = z + range(-.7f, .7f);
                p.transform.SetPositionAndRotation(new Vector3(px, ground(px, pz) - .08f, pz), Quaternion.Euler(range(-6, 6), range(0, 360), range(-6, 6)));
                p.transform.localScale = Vector3.one*range(2f, 2.9f);
                SetStatic(p);
            }

            // Wheat: ~10,000 procedural stalks merged into a few chunk meshes (straw + ear submeshes).
            var wheat = Child(group, "Wheat Field");
            int stalks;
            var chunks = BuildWheat(ground, range, out stalks);
            var materials = new[]{ CropMaterial("WheatStraw", new Color(.96f, .83f, .38f), .07f, 1.4f, .35f, .018f), CropMaterial("WheatEar", new Color(1f, .82f, .24f), .07f, 1.4f, .35f, .018f) };
            for (int i = 0; i < chunks.Count; i++)
            {
                var g = Child(wheat, "Wheat Chunk " + i);
                g.AddComponent<MeshFilter>().sharedMesh = chunks[i];
                var r = g.AddComponent<MeshRenderer>(); r.sharedMaterials = materials; r.shadowCastingMode = ShadowCastingMode.Off;
                g.isStatic = true;
            }
            if (ShadowPolishV1.IsApplied) ShadowPolishV1.ApplyCropShadows(true);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(fields.scene);
            return "Corn plants=" + plants + " in " + cornChunks.Count + " chunks; vines=" + vines + "; pumpkins=" + pumpkins + "; wheat stalks=" + stalks + " in " + chunks.Count + " chunks";
        }

        static GameObject Child(GameObject parent, string name)
        {
            var g = new GameObject(name); g.transform.SetParent(parent.transform, false); g.isStatic = true; return g;
        }

        static void SetStatic(GameObject g) { foreach (var t in g.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true; }

        static List<Mesh> BuildWheat(Func<float, float, float> ground, Func<float, float, float> range, out int stalks)
        {
            const int columns = 6;                  // chunked along x so off-screen parts can be culled
            var parts = new WheatBuilder[columns];
            for (int i = 0; i < columns; i++) parts[i] = new WheatBuilder();
            var wind = new Vector3(.045f, 0, .015f);   // shared lean gives the field one direction
            stalks = 0;
            // Chunk meshes are placed at the world origin, so their vertices are world positions.
            for (float x = East.x + .3f; x < East.y - .2f; x += .18f) for (float z = East.w - .3f; z > East.z + .2f; z -= .18f)
            {
                float px = x + range(-.08f, .08f), pz = z + range(-.08f, .08f);
                float edge = Mathf.Min(Mathf.Min(px - East.x, East.y - px), Mathf.Min(pz - East.z, East.w - pz), Mathf.Abs(pz - LaneZ) - LaneHalf);
                if (edge < .45f*Mathf.PerlinNoise(px*1.3f, pz*1.3f)) continue;   // ragged field and lane edges
                float wave = Mathf.PerlinNoise(px*.18f + 3.1f, pz*.18f + 8.7f);
                float height = .9f + .22f*wave + range(-.08f, .08f);
                var lean = wind + new Vector3(range(-.05f, .05f), 0, range(-.05f, .05f));
                int c = Mathf.Clamp((int)((px - East.x)/(East.y - East.x)*columns), 0, columns-1);
                parts[c].Stalk(new Vector3(px, ground(px, pz) - .02f, pz), height, lean, range(.25f, .6f), range(0, Mathf.PI*2), range);
                stalks++;
            }
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art/FarmReference", "Fields");
            var meshes = new List<Mesh>();
            for (int i = 0; i < columns; i++)
            {
                string path = Folder + "/WheatField_Chunk" + i + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
                mesh.Clear(); mesh.name = "WheatField_Chunk" + i; mesh.indexFormat = IndexFormat.UInt32;
                parts[i].Fill(mesh); EditorUtility.SetDirty(mesh);
                meshes.Add(mesh);
            }
            return meshes;
        }

        static List<Mesh> BuildCorn(Func<float, float, float> ground, Func<float, float, float> range, out int plants)
        {
            // Corn occupies the west part of the west soil area, where the raised corn beds used to be.
            const float x0 = 66.9f, x1 = 78.4f, z0 = -24.4f, z1 = -9.3f;
            const int columns = 2;
            var parts = new CornPlantBuilder[columns];
            for (int i = 0; i < columns; i++) parts[i] = new CornPlantBuilder();
            var wind = new Vector3(.03f, 0, .01f);
            plants = 0;
            for (float z = z1; z >= z0; z -= .9f) for (float x = x0; x <= x1; x += .38f)
            {
                if (range(0, 1) < .04f) continue;                   // occasional gaps in the rows
                float px = x + range(-.08f, .08f), pz = z + range(-.06f, .06f);
                float edge = Mathf.Min(Mathf.Min(px - x0, x1 - px), Mathf.Min(pz - z0, z1 - pz));
                float height = (2.1f + .45f*Mathf.PerlinNoise(px*.2f + 5.3f, pz*.2f + 1.7f) + range(-.12f, .12f))*Mathf.Lerp(.85f, 1, Mathf.Clamp01(edge/1.5f));
                var lean = wind + new Vector3(range(-.03f, .03f), 0, range(-.03f, .03f));
                int c = Mathf.Clamp((int)((px - x0)/(x1 - x0)*columns), 0, columns-1);
                parts[c].Plant(new Vector3(px, ground(px, pz) - .03f, pz), height, lean, range(0, Mathf.PI*2), range);
                plants++;
            }
            var meshes = new List<Mesh>();
            for (int i = 0; i < columns; i++)
            {
                string path = Folder + "/CornField_Chunk" + i + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
                mesh.Clear(); mesh.name = "CornField_Chunk" + i; mesh.indexFormat = IndexFormat.UInt32;
                parts[i].Fill(mesh); EditorUtility.SetDirty(mesh);
                meshes.Add(mesh);
            }
            return meshes;
        }

        static Material CropMaterial(string name, Color color, float windStrength, float windSpeed, float gustScale, float flutter)
        {
            string path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            // Two-sided (leaves and awns are single quads) with GPU wind driven by the mesh's UV0 sway data.
            var shader = Shader.Find("Chick/CropWind");
            if (shader == null) throw new InvalidOperationException("Chick/CropWind shader missing.");
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader; material.SetColor("_BaseColor", color); material.doubleSidedGI = true;
            material.SetFloat("_WindStrength", windStrength); material.SetFloat("_WindSpeed", windSpeed);
            material.SetFloat("_GustScale", gustScale); material.SetFloat("_Flutter", flutter);
            EditorUtility.SetDirty(material);
            return material;
        }

        // Low-poly wheat stalk: tapered three-sided stem, one drooping leaf, a two-row ear of kernels with awns.
        sealed class WheatBuilder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector2> sway = new List<Vector2>();   // x = bend weight (root 0, ear ~1), y = stalk phase
            readonly List<int> straw = new List<int>(), ear = new List<int>();

            int Add(Vector3 v) { vertices.Add(v); return vertices.Count - 1; }

            // Winds the triangle so its front faces away from the given centre.
            void Face(List<int> t, int a, int b, int c, Vector3 centre)
            {
                var n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                if (Vector3.Dot(n, (vertices[a] + vertices[b] + vertices[c])/3 - centre) < 0) { int s = b; b = c; c = s; }
                t.Add(a); t.Add(b); t.Add(c);
            }

            void Kernel(Vector3 c, Vector3 axis, Vector3 side, float half, float width, float depth)
            {
                var front = Vector3.Cross(axis, side).normalized;
                int top = Add(c + axis*half), bottom = Add(c - axis*half);
                int[] ring = { Add(c + side*width), Add(c + front*depth), Add(c - side*width), Add(c - front*depth) };
                for (int i = 0; i < 4; i++) { Face(ear, top, ring[i], ring[(i+1)%4], c); Face(ear, bottom, ring[i], ring[(i+1)%4], c); }
            }

            void Blade(List<int> t, Vector3 start, Vector3 direction, Vector3 widthAxis, float length, float width, float bend)
            {
                int prevA = -1, prevB = -1;
                for (int k = 0; k <= 3; k++)
                {
                    float u = k/3f, w = width*(1 - u*.85f);
                    var p = start + direction*length*u + Vector3.down*bend*u*u;
                    int a = Add(p + widthAxis*w*.5f), b = Add(p - widthAxis*w*.5f);
                    if (prevA >= 0) { t.Add(prevA); t.Add(a); t.Add(b); t.Add(prevA); t.Add(b); t.Add(prevB); }
                    prevA = a; prevB = b;
                }
            }

            public void Stalk(Vector3 root, float height, Vector3 lean, float droop, float yaw, Func<float, float, float> range)
            {
                int start = vertices.Count;
                // Stem along root + up*h*t + lean*h*t^2.
                float[] ts = { 0, .5f, 1 }; int prev = -1; Vector3 top = root;
                for (int k = 0; k < ts.Length; k++)
                {
                    float t = ts[k], r = Mathf.Lerp(.011f, .006f, t);
                    top = root + Vector3.up*height*t + lean*height*t*t;
                    int first = vertices.Count;
                    for (int s = 0; s < 3; s++) { float a = yaw + s*Mathf.PI*2/3; Add(top + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a))*r); }
                    if (prev >= 0) for (int s = 0; s < 3; s++)
                    {
                        var centre = root + Vector3.up*height*(ts[k-1]+t)*.5f + lean*height*Mathf.Pow((ts[k-1]+t)*.5f, 2);
                        Face(straw, prev+s, first+s, first+(s+1)%3, centre); Face(straw, prev+s, first+(s+1)%3, prev+(s+1)%3, centre);
                    }
                    prev = first;
                }
                var leanDir = new Vector3(lean.x, 0, lean.z).normalized;
                var axis = (Vector3.up + lean*2 + leanDir*droop).normalized;      // ripe ears nod over
                var side = Vector3.Cross(axis, new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw))).normalized;
                var front = Vector3.Cross(axis, side).normalized;
                // Ear: alternating kernel pairs in one plane, tip kernel, awns from the upper kernels.
                float length = range(.11f, .15f);
                for (int level = 0; level < 5; level++)
                {
                    var c = top + axis*(.014f + level*length/5.5f);
                    float shrink = 1 - level*.07f;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var kAxis = (axis + side*s*.35f).normalized;
                        var kc = c + side*s*.0085f*shrink;
                        Kernel(kc, kAxis, side, .017f*shrink, .008f*shrink, .007f*shrink);
                        if (level >= 2)
                        {
                            var awn = (axis + side*s*range(.15f, .3f)).normalized;
                            Blade(ear, kc + kAxis*.015f, awn, front, range(.05f, .08f), .003f, .004f);
                        }
                    }
                }
                Kernel(top + axis*(.014f + length*.9f), axis, side, .014f, .006f, .006f);
                // One leaf leaving the lower stem and arching down.
                var leafDir = (new Vector3(Mathf.Cos(yaw + 1.3f), .9f, Mathf.Sin(yaw + 1.3f))).normalized;
                var leafWidth = Vector3.Cross(leafDir, Vector3.up).normalized;
                Blade(straw, root + Vector3.up*height*.38f + lean*height*.14f, leafDir, leafWidth, range(.18f, .26f), .018f, .12f);
                float phase = range(0, 1);
                for (int i = start; i < vertices.Count; i++) sway.Add(new Vector2(Mathf.Clamp((vertices[i].y - root.y)/height, 0, 1.2f), phase));
            }

            public void Fill(Mesh mesh)
            {
                mesh.SetVertices(vertices); mesh.subMeshCount = 2;
                mesh.SetTriangles(straw, 0); mesh.SetTriangles(ear, 1); mesh.SetUVs(0, sway);
                var white = new Color[vertices.Count]; for (int i = 0; i < white.Length; i++) white[i] = Color.white;
                mesh.colors = white;                // Chick/CropWind multiplies by vertex colour
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var bounds = mesh.bounds; bounds.Expand(.3f); mesh.bounds = bounds;   // room for the wind offset
            }
        }

        // Tilled soil under both field areas: noisy soft edges and faint furrows. Also re-applied by NaturalHillsV4.
        public static void PaintSoil(Terrain terrain)
        {
            var data = terrain.terrainData;
            int w = data.alphamapWidth, h = data.alphamapHeight, layers = data.alphamapLayers;
            var origin = terrain.transform.position;
            int x0 = Mathf.Max(0, (int)((West.x - 2 - origin.x)/data.size.x*(w-1))), x1 = Mathf.Min(w-1, (int)((East.y + 2 - origin.x)/data.size.x*(w-1)) + 1);
            int z0 = Mathf.Max(0, (int)((West.z - 2 - origin.z)/data.size.z*(h-1))), z1 = Mathf.Min(h-1, (int)((West.w + 2 - origin.z)/data.size.z*(h-1)) + 1);
            var a = data.GetAlphamaps(x0, z0, x1-x0+1, z1-z0+1);
            for (int iz = 0; iz <= z1-z0; iz++) for (int ix = 0; ix <= x1-x0; ix++)
            {
                float x = origin.x + (ix+x0)*data.size.x/(w-1), z = origin.z + (iz+z0)*data.size.z/(h-1);
                float soil = 0;
                foreach (var r in new[]{West, East})
                {
                    float d = Mathf.Max(Mathf.Max(r.x - x, x - r.y), Mathf.Max(r.z - z, z - r.w)) + (Mathf.PerlinNoise(x*.7f, z*.7f) - .5f)*.8f;
                    soil = Mathf.Max(soil, 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.4f, .6f, d)));
                }
                if (soil <= 0) continue;
                float furrow = .5f + .5f*Mathf.Sin(z*Mathf.PI/.9f);
                for (int i = 0; i < layers; i++) a[iz, ix, i] *= 1 - soil;
                a[iz, ix, 4] += soil*(.55f + .25f*furrow);   // TroddenEarth
                a[iz, ix, 5] += soil*(.45f - .25f*furrow);   // OldEarthAccent
            }
            data.SetAlphamaps(x0, z0, a); data.SetBaseMapDirty();
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
        }
    }
}
