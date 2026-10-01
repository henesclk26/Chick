using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Never changes heights or runs at startup.
    public static class MeadowVarietyV3
    {
        const string Folder = "Assets/Art/Terrain/MeadowVarietyV3/";
        const string Previous = "Assets/Art/Terrain/FreshMeadowV2/FarmTerrain_FreshGreen_v2.asset";
        const string Target = Folder + "FarmTerrain_MeadowVariety_v3.asset";
        static float Smooth(float a, float b, float x)
        { return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x)); }

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var root = GameObject.Find("Farm Reference Map");
            if (root == null) throw new InvalidOperationException("Farm root missing.");
            var terrain = root.GetComponentInChildren<Terrain>();
            if (terrain == null) terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
            var old = AssetDatabase.LoadAssetAtPath<TerrainData>(Previous);
            if (old == null || terrain == null) throw new InvalidOperationException("V2 terrain missing.");
            var oldLayers = old.terrainLayers;
            int gravelIndex = Array.FindIndex(oldLayers, l => l.diffuseTexture != null && AssetDatabase.GetAssetPath(l.diffuseTexture).EndsWith("/Gravel.png"));
            if (gravelIndex < 0) throw new InvalidOperationException("Old gravel layer not found by texture identity.");
            var oldAlpha = old.GetAlphamaps(0, 0, old.alphamapWidth, old.alphamapHeight);
            var images = new[] {"GrassRich", "GrassFresh", "GrassSunlit", "PathEarth", "GrassEarthBlend"};
            foreach (var name in images)
            {
                var importer = AssetImporter.GetAtPath(Folder + name + ".png") as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Texture not imported: " + name);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.None;
                // Matching reflected edges avoid seams in the hand-painted source tiles.
                importer.wrapMode = TextureWrapMode.Mirror;
                importer.filterMode = FilterMode.Trilinear;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 8;
                importer.npotScale = TextureImporterNPOTScale.ToNearest;
                importer.maxTextureSize = 2048;
                var platform = importer.GetPlatformTextureSettings("Standalone");
                platform.name = "Standalone"; platform.overridden = true;
                platform.format = TextureImporterFormat.DXT1;
                platform.maxTextureSize = 2048; platform.compressionQuality = 100;
                importer.SetPlatformTextureSettings(platform);
                importer.SaveAndReimport();
            }
            string oldFolder = "Assets/Art/Terrain/FreshMeadowV2/";
            string[] names = {"OriginalMeadow", "RichGreen", "FreshGreen", "SunlitGreen", "TroddenEarth", "OldEarthAccent", "Pebbles", "WornMeadow"};
            string[] textures = {oldFolder+"Grass.png", Folder+"GrassRich.png", Folder+"GrassFresh.png", Folder+"GrassSunlit.png", Folder+"PathEarth.png", oldFolder+"Soil.png", oldFolder+"Gravel.png", Folder+"GrassEarthBlend.png"};
            // URP Terrain shares the first layer sampler across each four-layer pass.
            // Keep every layer consistent, including the three reused V2 textures.
            foreach (string texturePath in textures)
            {
                var sourceImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                if (sourceImporter != null && sourceImporter.wrapMode != TextureWrapMode.Mirror)
                {
                    sourceImporter.wrapMode = TextureWrapMode.Mirror;
                    sourceImporter.SaveAndReimport();
                }
            }
            float[] scales = {4.3f, 5.2f, 5.7f, 5.1f, 3.4f, 3.3f, 3.1f, 4.7f};
            var layers = new TerrainLayer[8];
            for (int i = 0; i < layers.Length; i++)
            {
                string path = Folder + names[i] + ".terrainlayer";
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (layer == null) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, path); }
                layer.name = names[i];
                layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(textures[i]);
                if (layer.diffuseTexture == null) throw new InvalidOperationException(textures[i]);
                layer.tileSize = Vector2.one * scales[i];
                layer.tileOffset = new Vector2(i * 1.73f, i * .83f);
                layer.metallic = 0; layer.smoothness = 0;
                layer.diffuseRemapMin = Vector4.zero;
                layer.diffuseRemapMax = i == 0 ? new Vector4(.86f,.97f,.99f,0) : new Vector4(1,1,1,0);
                EditorUtility.SetDirty(layer); AssetDatabase.SaveAssetIfDirty(layer);
                layers[i] = layer;
            }
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(Target);
            if (data == null)
            {
                data = UnityEngine.Object.Instantiate(old);
                data.name = "FarmTerrain_MeadowVariety_v3";
                AssetDatabase.CreateAsset(data, Target);
            }
            data.terrainLayers = layers;
            var trees = new List<Vector2>();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                if (r.name.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0)
                { var p = root.transform.InverseTransformPoint(r.bounds.center); trees.Add(new Vector2(p.x,p.z)); }
            int w = data.alphamapWidth, h = data.alphamapHeight;
            var weights = new float[h,w,8];
            for (int z = 0; z < h; z++) for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w-1), v = z / (float)(h-1);
                var world = terrain.transform.position + new Vector3(u*data.size.x,0,v*data.size.z);
                var p = root.transform.InverseTransformPoint(world);
                var q = new Vector2(p.x,p.z);
                float treeDistance = 1000;
                foreach (var tree in trees) treeDistance = Mathf.Min(treeDistance,(q-tree).sqrMagnitude);
                float treeBias = 1-Smooth(3,11,Mathf.Sqrt(treeDistance));
                float n = Mathf.PerlinNoise(p.x*.043f+19.2f,p.z*.043f+31.6f);
                float n2 = Mathf.PerlinNoise(p.x*.058f+82.7f,p.z*.058f+9.3f);
                float lush = Smooth(.38f,.64f,n+treeBias*.21f);
                float sun = Smooth(.42f,.67f,n2)*(1-lush);
                float fresh = Mathf.Max(.04f,1-lush-sun);
                // Dominant regions, rather than equal mixing of all grass types.
                lush = Mathf.Pow(lush,2); sun = Mathf.Pow(sun,2); fresh = Mathf.Pow(fresh,2);
                float sumGrass = lush+sun+fresh;
                lush /= sumGrass; sun /= sumGrass; fresh /= sumGrass;
                float core, shoulder; OrganicFarmPaths.Evaluate(q,out core,out shoulder);
                float gravel = oldAlpha[z,x,gravelIndex]*(1-core*.96f);
                float sparseWear = Smooth(.69f,.83f,Mathf.PerlinNoise(p.x*.095f+71,p.z*.095f+45))*.45f;
                float wear = Mathf.Max(shoulder*.82f,sparseWear)*(1-core)*(1-gravel);
                wear += core*.10f*(1-gravel);
                float dirt = core*.85f*(1-gravel);
                float grass = Mathf.Max(0,1-gravel-wear-dirt);
                weights[z,x,0] = grass*.09f;
                weights[z,x,1] = grass*.91f*lush;
                weights[z,x,2] = grass*.91f*fresh;
                weights[z,x,3] = grass*.91f*sun;
                float oldDirt = .08f+.12f*Mathf.PerlinNoise(p.x*.17f+4,p.z*.17f+6);
                weights[z,x,4] = dirt*(1-oldDirt);
                weights[z,x,5] = dirt*oldDirt;
                weights[z,x,6] = gravel;
                weights[z,x,7] = wear;
                float total = 0; for (int i=0;i<8;i++) total += weights[z,x,i];
                for (int i=0;i<8;i++) weights[z,x,i] /= total;
            }
            // Terrain layer reassignment invalidates copied alphamaps: set all weights explicitly.
            data.SetAlphamaps(0,0,weights); data.SetBaseMapDirty();
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
            Undo.RecordObject(terrain,"Apply varied green terrain V3");
            terrain.terrainData = data;
            var collider = terrain.GetComponent<TerrainCollider>();
            if (collider != null) { Undo.RecordObject(collider,"Match terrain collider V3"); collider.terrainData = data; }
            terrain.Flush();
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            return Validate();
        }

        public static string Validate()
        {
            var terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
            var data = terrain.terrainData;
            var old = AssetDatabase.LoadAssetAtPath<TerrainData>(Previous);
            int r = data.heightmapResolution;
            var heights = data.GetHeights(0,0,r,r); var oldHeights = old.GetHeights(0,0,r,r);
            float heightDelta=0;
            for(int z=0;z<r;z++) for(int x=0;x<r;x++) heightDelta=Mathf.Max(heightDelta,Mathf.Abs(heights[z,x]-oldHeights[z,x]));
            var a=data.GetAlphamaps(0,0,data.alphamapWidth,data.alphamapHeight);
            var sums=new double[data.alphamapLayers]; var dominant=new int[data.alphamapLayers];
            float maxError=0; int invalid=0;
            for(int z=0;z<data.alphamapHeight;z++) for(int x=0;x<data.alphamapWidth;x++)
            {
                float sum=0,best=-1; int winner=0;
                for(int i=0;i<data.alphamapLayers;i++) { float f=a[z,x,i]; if(float.IsNaN(f)||f<0) invalid++; sums[i]+=f; sum+=f; if(f>best){best=f;winner=i;} }
                dominant[winner]++; maxError=Mathf.Max(maxError,Mathf.Abs(sum-1));
            }
            var sb=new StringBuilder();
            sb.AppendLine("Asset="+AssetDatabase.GetAssetPath(data));
            sb.AppendLine("Layers="+data.alphamapLayers+"; heightDelta="+heightDelta+"; invalidWeights="+invalid+"; maxWeightError="+maxError);
            sb.AppendLine("ColliderMatches="+(terrain.GetComponent<TerrainCollider>().terrainData==data));
            double count=data.alphamapWidth*data.alphamapHeight;
            for(int i=0;i<sums.Length;i++) sb.AppendLine(data.terrainLayers[i].name+": mean "+(100*sums[i]/count).ToString("F2")+"%; dominant area "+(100*dominant[i]/count).ToString("F2")+"%");
            foreach(var p in OrganicFarmPaths.GateAnchors){float c,s;OrganicFarmPaths.Evaluate(p,out c,out s);sb.AppendLine("Gate "+p+": core="+c);}
            return sb.ToString();
        }
    }
}
