using System;
using UnityEditor;
using UnityEngine;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. The IL3DN grass under Chick_Grass_Green and Chick_Grass_Dry comes with three
    // LOD meshes that are not simplified copies of each other: LOD1 is taller and sparser, LOD2 a tuft of tall, spiky
    // blades. With the pack's thresholds the clumps switched to LOD1 some 5-7 m from the camera, right where the player
    // looks, so the grass visibly changed shape as the chick walked. Here each prefab keeps only its LOD0 mesh, at every
    // distance, and is still culled where its LOD2 used to be culled (the LOD1 and LOD2 children are switched off, not
    // deleted). The two grass textures also keep their alpha coverage in their mipmaps, so the blades do not thin out
    // and go see-through with distance. "Grass Far LODs (V2)" keeps the three LODs for performance but moves the
    // switches far out (FarHeights), "Restore Grass LODs (V1)" puts the pack's thresholds back.
    public static class GrassLodV1
    {
        const string Menu = "Tools/Chick/Ground/";
        static readonly string[] Prefabs = { "Assets/Art/Grass/Chick_Grass_Green.prefab", "Assets/Art/Grass/Chick_Grass_Dry.prefab" };
        static readonly string[] Textures = { "Assets/IL3DN/Graphics/Textures/IL3DN_Grass_01.psd", "Assets/IL3DN/Graphics/Textures/IL3DN_Grass_02.psd" };
        // the pack's screen-relative transition heights, LOD0..LOD2, per prefab (the last one is the cull height)
        static readonly float[][] PackHeights = { new[] { .5017237f, .1968613f, .04989976f }, new[] { .4969922f, .2028568f, .04585641f } };

        // Far LODs (V2): LOD0 out to about 20 m and LOD1 out to about 40 m (for a clump at 0.8 scale, with the chick
        // camera's 58 degree view and the PC quality's LOD bias of 2), then LOD2 until the pack's cull height. The shape
        // change then happens where a clump is a few pixels tall; from the chick camera it changed only a few pixels
        // on the horizon.
        static readonly float[][] FarHeights = { new[] { .137f, .069f, .04989976f }, new[] { .152f, .076f, .04585641f } };

        public enum Mode { Single, Far, Pack }

        [MenuItem(Menu + "Grass Single LOD (V1)")]
        static void MenuApply() => Debug.Log(Apply(Mode.Single));

        [MenuItem(Menu + "Grass Far LODs (V2)")]
        static void MenuApplyFar() => Debug.Log(Apply(Mode.Far));

        [MenuItem(Menu + "Restore Grass LODs (V1)")]
        static void MenuRestore() => Debug.Log(Apply(Mode.Pack));

        public static string Apply(Mode mode)
        {
            bool single = mode == Mode.Single, keepCoverage = mode != Mode.Pack;
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var report = new System.Text.StringBuilder();
            for (int p = 0; p < Prefabs.Length; p++)
            {
                var root = PrefabUtility.LoadPrefabContents(Prefabs[p]);
                try
                {
                    var group = root.GetComponent<LODGroup>() ?? throw new InvalidOperationException("No LODGroup on " + Prefabs[p]);
                    var renderers = new Renderer[3];
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                        for (int l = 0; l < 3; l++) if (r.name.EndsWith("_LOD" + l)) renderers[l] = r;
                    for (int l = 0; l < 3; l++) if (renderers[l] == null) throw new InvalidOperationException("LOD" + l + " mesh missing in " + Prefabs[p]);
                    var h = mode == Mode.Far ? FarHeights[p] : PackHeights[p];
                    if (single)
                    {
                        group.SetLODs(new[] { new LOD(h[2], new[] { renderers[0] }) });
                        renderers[1].gameObject.SetActive(false); renderers[2].gameObject.SetActive(false);
                    }
                    else
                    {
                        renderers[1].gameObject.SetActive(true); renderers[2].gameObject.SetActive(true);
                        group.SetLODs(new[] { new LOD(h[0], new[] { renderers[0] }), new LOD(h[1], new[] { renderers[1] }), new LOD(h[2], new[] { renderers[2] }) });
                    }
                    group.RecalculateBounds();
                    PrefabUtility.SaveAsPrefabAsset(root, Prefabs[p]);
                    report.Append(System.IO.Path.GetFileNameWithoutExtension(Prefabs[p]) + (single ? ": LOD0 only, culled below " + h[2] :
                        ": three LODs at " + h[0] + " / " + h[1] + " / " + h[2]) + ". ");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (var path in Textures)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer == null) { report.Append("Missing " + path + ". "); continue; }
                importer.mipMapsPreserveCoverage = keepCoverage;
                importer.alphaTestReferenceValue = .5f;   // the grass materials' cutoff
                importer.SaveAndReimport();
            }
            report.Append(keepCoverage ? "Grass textures keep their alpha coverage in the mipmaps." : "Grass texture mipmaps as imported.");
            return report.ToString();
        }
    }
}
