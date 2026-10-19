using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Lays the small ground details modelled in Blender
    // (Assets/Art/GroundCover/Models/GroundCoverKit.fbx) over the farm terrain as instanced terrain details, beside the
    // two existing grass details: small stones on earth, sand and path edges, and wet mud clods round the pond. Nothing
    // goes in the pond or under fences, buildings and rocks.
    // The kit's ground plants (leafy tufts, seedling patches, flat rosettes) and soil grains were tried and taken out
    // again; their models and prefabs are still in the kit, but this tool no longer lays them and clears them away.
    public static class GroundCoverV1
    {
        const string Menu = "Tools/Chick/Ground/";
        const string Root = "Assets/Art/GroundCover/";
        const string KitPath = Root + "Models/GroundCoverKit.fbx";
        const string AtlasPath = Root + "Textures/GroundCoverAtlas.png";
        const string PrefabFolder = Root + "Prefabs/";
        static readonly string[] Pebbles = { "GC_Pebbles_0", "GC_Pebbles_1", "GC_Pebbles_2" };
        static readonly string[] Muds = { "GC_MudClods_0", "GC_MudClods_1" };
        const string StoneMaterialPath = Root + "Materials/M_GroundCoverStones.mat";
        // per square metre: stones on full earth, mud clods on the pond's damp bank
        const float PebbleDensity = 2.2f, MudDensity = 1.8f;
        const float DetailDistance = 65f;   // the terrain's own detail distance, as before the ground plants

        [MenuItem(Menu + "Lay Small Stones And Mud (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map") ?? throw new InvalidOperationException("Farm Reference Map missing.");
            var terrain = root.GetComponentInChildren<Terrain>() ?? throw new InvalidOperationException("Farm terrain missing.");
            var data = terrain.terrainData;
            if (data.terrainLayers.Length < 16) throw new InvalidOperationException("Expected the farm terrain with at least 16 layers.");
            if (data.detailScatterMode != DetailScatterMode.InstanceCountMode) throw new InvalidOperationException("Details are not in instance-count mode.");

            var stoneMaterial = EnsureStoneMaterial(EnsureAtlas());
            var meshes = LoadKit();
            Undo.RegisterCompleteObjectUndo(data, "Lay ground details");

            // prototypes: keep the existing (foreign) ones, replace ours
            var prototypes = new List<DetailPrototype>();
            var keep = new List<int>();
            for (int i = 0; i < data.detailPrototypes.Length; i++)
            {
                var p = data.detailPrototypes[i];
                if (p.prototype != null && AssetDatabase.GetAssetPath(p.prototype).StartsWith(PrefabFolder, StringComparison.Ordinal)) continue;
                prototypes.Add(p); keep.Add(i);
            }
            var keptLayers = new List<int[,]>();
            foreach (int i in keep) keptLayers.Add(data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, i));
            var names = new List<string>();
            names.AddRange(Pebbles); names.AddRange(Muds);
            int first = prototypes.Count;
            foreach (var n in names) prototypes.Add(StonePrototype(EnsurePrefab(n, meshes[n], stoneMaterial)));
            data.detailPrototypes = prototypes.ToArray();
            for (int i = 0; i < keptLayers.Count; i++) data.SetDetailLayer(0, 0, i, keptLayers[i]);

            int res = data.detailResolution;
            Vector3 tp = terrain.transform.position, size = data.size;
            float cx = size.x/res, cz = size.z/res, area = cx*cz;
            int ares = data.alphamapResolution;
            var alpha = data.GetAlphamaps(0, 0, ares, ares);
            var shore = PondRelocationV1.NewShoreline();
            Vector2 sMin = shore[0], sMax = shore[0];
            foreach (var p in shore) { sMin = Vector2.Min(sMin, p); sMax = Vector2.Max(sMax, p); }
            sMin -= Vector2.one*2; sMax += Vector2.one*2;

            var layers = new int[names.Count][,];
            for (int i = 0; i < layers.Length; i++) layers[i] = new int[res, res];
            long total = 0; var counts = new long[names.Count];
            int blockedByObjects = 0;
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float wx = tp.x + (x + .5f)*cx, wz = tp.z + (z + .5f)*cz;
                int ax = Mathf.Min(ares - 1, (int)((x + .5f)/res*ares)), az = Mathf.Min(ares - 1, (int)((z + .5f)/res*ares));
                float W(int l) => alpha[az, ax, l];
                float grassy = W(0) + W(1) + W(2) + W(3) + W(7) + W(9) + W(10) + W(11) + W(12);
                // small stones belong to the bare ground: earth, sand, the pebble and old-earth paint, path edges
                float stony = W(13) + 1.2f*W(14) + 1.5f*W(6) + .8f*W(5) + .45f*W(4) + .4f*W(15) + .05f*grassy;
                float muddy = 1.2f*W(15);
                if (wx > sMin.x && wx < sMax.x && wz > sMin.y && wz < sMax.y)
                {
                    float sd = FarmPondV1.Signed(shore, wx, wz);
                    if (sd < .15f) continue;
                    muddy = Mathf.Max(muddy, 1 - Mathf.SmoothStep(0, 1, (sd - .3f)/1.8f));   // the wet bank
                }
                if (stony <= .01f && muddy <= .01f) continue;
                float y = terrain.SampleHeight(new Vector3(wx, 0, wz)) + tp.y;
                // only things standing on the ground block a detail (posts, trunks, rocks, floors): the ray starts just
                // above detail height, under fence rails and tree crowns, and camera blockers do not count
                if (Physics.Raycast(new Vector3(wx, y + .12f, wz), Vector3.down, out var hit, .3f, ~LayerMask.GetMask("CameraBlocker"), QueryTriggerInteraction.Ignore)
                    && !(hit.collider is TerrainCollider) && hit.point.y > y + .03f)
                { blockedByObjects++; continue; }

                float n1 = Mathf.PerlinNoise(wx/4.3f + 17, wz/4.3f + 3), n4 = Mathf.PerlinNoise(wx/3.1f + 83, wz/3.1f + 11);
                uint h = Hash(x, z);
                Place(layers, counts, 0, Pebbles.Length, PebbleDensity*stony*Mathf.Clamp01(.2f + 1.4f*(n4 - .25f))*area, h*7919u + 3, x, z);
                Place(layers, counts, Pebbles.Length, Muds.Length, MudDensity*muddy*Mathf.Clamp01(.3f + 1.2f*(n1 - .2f))*area, h*104729u + 5, x, z);
            }
            Undo.RecordObject(terrain, "Lay ground details");
            terrain.detailObjectDistance = DetailDistance;
            for (int i = 0; i < layers.Length; i++) { data.SetDetailLayer(0, 0, first + i, layers[i]); total += counts[i]; }
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            terrain.Flush();
            EditorSceneManager.MarkSceneDirty(root.scene);

            var sb = new System.Text.StringBuilder("Ground details laid: " + total + " over the terrain (" + blockedByObjects + " cells under fences, buildings and rocks left bare).");
            for (int i = 0; i < names.Count; i++) sb.Append("\n  " + names[i] + " " + counts[i]);
            return sb.ToString();
        }

        // Spreads a fractional expected count over a cell: whole pieces, the remainder by a hashed chance, each to
        // one of the type's variants.
        static void Place(int[][,] layers, long[] counts, int start, int variants, float expected, uint h, int x, int z)
        {
            int n = Mathf.FloorToInt(expected);
            if ((h & 0xffff)/65535f < expected - n) n++;
            for (int k = 0; k < n; k++)
            {
                int v = start + (int)((h >> (16 + k*3)) % (uint)variants);
                layers[v][z, x]++; counts[v]++;
            }
        }

        static uint Hash(int x, int z)
        {
            uint h = (uint)(x*73856093) ^ (uint)(z*19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return h;
        }

        static Texture2D EnsureAtlas()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.SaveAndReimport();
            var mi = (ModelImporter)AssetImporter.GetAtPath(KitPath);
            mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false; mi.importLights = false; mi.importNormals = ModelImporterNormals.Import; mi.isReadable = true; mi.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        }

        static DetailPrototype StonePrototype(GameObject prefab) => new DetailPrototype
        {
            prototype = prefab,
            usePrototypeMesh = true,
            useInstancing = true,
            renderMode = DetailRenderMode.VertexLit,
            minWidth = .8f, maxWidth = 1.5f, minHeight = .8f, maxHeight = 1.3f,
            noiseSpread = .5f,
            healthyColor = Color.white,
            dryColor = new Color(.9f, .86f, .8f),
            alignToGround = 1f,
            positionJitter = 1f,
            density = 1f,
        };

        static Material EnsureStoneMaterial(Texture atlas)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(StoneMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_GroundCoverStones" };
                AssetDatabase.CreateAsset(material, StoneMaterialPath);
            }
            material.SetTexture("_BaseMap", atlas);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", .22f);         // a faint sheen: wet mud and smooth pebbles
            material.SetFloat("_EnvironmentReflections", 0f); material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Dictionary<string, Mesh> LoadKit()
        {
            var meshes = new Dictionary<string, Mesh>();
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(KitPath)) if (a is Mesh m) meshes[m.name] = m;
            foreach (var n in Pebbles) if (!meshes.ContainsKey(n)) throw new InvalidOperationException("Missing " + n);
            foreach (var n in Muds) if (!meshes.ContainsKey(n)) throw new InvalidOperationException("Missing " + n);
            return meshes;
        }

        static GameObject EnsurePrefab(string name, Mesh mesh, Material material)
        {
            System.IO.Directory.CreateDirectory(PrefabFolder);
            string path = PrefabFolder + name + ".prefab";
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // ground-hugging leaves: no shadow cost
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }
    }
}
