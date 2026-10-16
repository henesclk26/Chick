using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Fence pieces moved by hand keep the height of where they stood, so on new
    // ground some float (up to ~0.5 m) and some sink (up to ~1 m). A chick walking along or under them then catches on
    // the bottoms of floating posts and rails. This drops or lifts each piece, kept upright, until its lowest end
    // sits just into the ground. Gate leaves hang above the ground and are left alone; their posts are snapped.
    public static class FenceGroundSnapV1
    {
        const string Menu = "Tools/Chick/Fences/";
        const float Sink = .02f;        // posts a little into the ground, as the builder placed them
        const float Tolerance = .03f;   // pieces already this close are left untouched
        static readonly string[] Groups = { "Farm Reference Map/04 Fences and Gates", "Farm Reference Map/07 Sheep Pasture/Pasture Fence" };

        [MenuItem(Menu + "Snap Fences To Ground (V1)")]
        static void MenuApply() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var terrain = Terrain.activeTerrain;
            if (terrain == null) throw new InvalidOperationException("Terrain missing.");
            int moved = 0, checkedPieces = 0;
            float largest = 0;
            foreach (var group in Groups)
            {
                var root = GameObject.Find(group);
                if (root == null) continue;
                foreach (var piece in Pieces(root.transform))
                {
                    var renderer = piece.GetComponent<Renderer>();
                    var b = renderer.bounds;
                    float ground = LowestGround(terrain, b);
                    float delta = ground - Sink - b.min.y;
                    checkedPieces++;
                    if (Mathf.Abs(delta) <= Tolerance) continue;
                    Undo.RecordObject(piece, "Snap fence to ground");
                    piece.position += Vector3.up*delta;
                    largest = Mathf.Max(largest, Mathf.Abs(delta));
                    moved++;
                }
            }
            if (moved > 0) EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return "Fences snapped to ground: " + moved + " of " + checkedPieces + " pieces moved (largest " + (largest*100).ToString("F0") + " cm).";
        }

        // Fence segments (their own mesh, upright) and gate posts; not gate leaves, hinges or helper colliders.
        static IEnumerable<Transform> Pieces(Transform root)
        {
            foreach (Transform child in root)
            {
                if (child.name.StartsWith("Gate", StringComparison.Ordinal))
                {
                    foreach (Transform part in child)
                        if (part.name == "Gate Post" && part.GetComponent<Renderer>() != null) yield return part;
                    continue;
                }
                var renderer = child.GetComponent<Renderer>();
                if (renderer != null && renderer.bounds.size.y > .5f) yield return child;
            }
        }

        // The lowest ground along the piece's long axis: that end's posts sit on it, the rest go a little deeper.
        static float LowestGround(Terrain terrain, Bounds b)
        {
            float ground = float.MaxValue;
            bool alongX = b.size.x >= b.size.z;
            for (int i = 0; i <= 6; i++)
            {
                float t = i/6f;
                var p = alongX ? new Vector3(Mathf.Lerp(b.min.x + .05f, b.max.x - .05f, t), 0, b.center.z)
                               : new Vector3(b.center.x, 0, Mathf.Lerp(b.min.z + .05f, b.max.z - .05f, t));
                ground = Mathf.Min(ground, terrain.SampleHeight(p) + terrain.transform.position.y);
            }
            return ground;
        }
    }
}
