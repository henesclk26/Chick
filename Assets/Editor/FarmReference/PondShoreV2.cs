using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FarmReferenceTools
{
    // Editor-only, explicit application. Rebuilds the pond's water surface from the ground it lies on. The old surface
    // was a grid of square cells cut along the traced shoreline, and where the bank sits at (or just under) the water
    // level its cell edge stayed above the ground and showed as a staircase on the shore. The new surface takes the
    // pond's own body of water (within the traced shoreline FarmPond keeps, so a low spot of the bank outside it stays
    // dry; the largest body under the old surface) and grows out over
    // the bank until the ground stands Clearance above the water, so its outline always ends under the bank; its
    // vertex alpha also runs from 0 on the outline to 1 FadeCells cells
    // inside, so even where the far terrain's simplified mesh dips, no hard edge shows (Chick/PondWater multiplies its
    // alpha by it). First, any low spot of the bank outside the shoreline that still lies under the water level (it
    // held a puddle beside the pond) is lifted to the bank profile FarmPondV1 carved; nothing is lowered.
    // Same asset (PondWaterSurface), same object.
    public static class PondShoreV2
    {
        const string Menu = "Tools/Chick/Pond/";
        const float Step = .2f, Clearance = .04f, Reach = 3f;
        const int FadeCells = 3, MaxBank = 6;
        const float ShoreSlack = .25f;   // m past the traced shoreline still taken as pond

        [MenuItem(Menu + "Rebuild Water Surface From Ground (V2)")]
        static void MenuApply() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit play mode first.");
            var surface = GameObject.Find("Water Surface") ?? throw new InvalidOperationException("Water Surface missing.");
            var filter = surface.GetComponent<MeshFilter>();
            var old = filter.sharedMesh ?? throw new InvalidOperationException("Water Surface has no mesh.");
            var terrain = Terrain.activeTerrain ?? throw new InvalidOperationException("No terrain.");
            Vector3 origin = surface.transform.position;
            float water = origin.y;

            // the grid over the old surface and Reach around it; the seeds are the old surface's cells
            var ob = old.bounds;
            float minX = origin.x + ob.min.x - Reach, minZ = origin.z + ob.min.z - Reach;
            int nx = Mathf.CeilToInt((ob.size.x + 2*Reach)/Step), nz = Mathf.CeilToInt((ob.size.z + 2*Reach)/Step);
            // the traced shoreline the pond was carved from (FarmPond keeps it): water outside it, in a low spot of the
            // bank, is not the pond
            var pondRoot = surface.transform.parent ?? throw new InvalidOperationException("Water Surface has no pond parent.");
            var keeper = pondRoot.GetComponent("FarmPond") ?? throw new InvalidOperationException("FarmPond component missing.");
            var line = new SerializedObject(keeper).FindProperty("shoreline");
            var shore = new Vector2[line.arraySize];
            for (int i = 0; i < shore.Length; i++) shore[i] = line.GetArrayElementAtIndex(i).vector2Value;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Rebuild pond water surface (V2)");
            int raised = RaiseBank(terrain, shore, water, minX, minZ, minX + nx*Step, minZ + nz*Step);

            var ground = new float[nx, nz];
            var blocked = new bool[nx, nz];   // a low spot outside the shoreline: no surface over it at all
            for (int x = 0; x < nx; x++)
                for (int z = 0; z < nz; z++)
                {
                    ground[x, z] = Ground(terrain, minX, minZ, x, z);
                    // by the cell's centre, not its lowest corner, so a bank cell across the waterline is not taken for one
                    float cx = minX + (x + .5f)*Step, cz = minZ + (z + .5f)*Step;
                    blocked[x, z] = terrain.SampleHeight(new Vector3(cx, 0, cz)) + terrain.transform.position.y < water &&
                                    FarmPondV1.Signed(shore, cx, cz) > ShoreSlack;
                }
            var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            bool In(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < nx && c.y < nz;

            // the pond itself: the largest body of cells with ground under the water among the old surface's cells
            // (a separate hollow nearby, even one the old surface reached, stays dry)
            var label = new int[nx, nz];
            var sizes = new List<int> { 0 };
            var ov = old.vertices; var ot = old.triangles;
            for (int i = 0; i < ot.Length; i += 3)
            {
                Vector3 c = (ov[ot[i]] + ov[ot[i + 1]] + ov[ot[i + 2]])/3f + origin;
                var seed = new Vector2Int(Mathf.FloorToInt((c.x - minX)/Step), Mathf.FloorToInt((c.z - minZ)/Step));
                if (!In(seed) || label[seed.x, seed.y] != 0 || ground[seed.x, seed.y] > water || blocked[seed.x, seed.y]) continue;
                int id = sizes.Count, count = 0;
                var fill = new Queue<Vector2Int>(); fill.Enqueue(seed); label[seed.x, seed.y] = id;
                while (fill.Count > 0)
                {
                    var p = fill.Dequeue(); count++;
                    foreach (var d in dirs)
                    {
                        var n = p + d;
                        if (!In(n) || label[n.x, n.y] != 0 || ground[n.x, n.y] > water || blocked[n.x, n.y]) continue;
                        label[n.x, n.y] = id; fill.Enqueue(n);
                    }
                }
                sizes.Add(count);
            }
            int pond = 0; for (int i = 1; i < sizes.Count; i++) if (sizes[i] > sizes[pond]) pond = i;
            if (pond == 0) throw new InvalidOperationException("No water found under the old surface.");

            // the bank: out from the pond over ground above the water, up to the first cell Clearance above it
            // (that one included, so the outline lies under the bank), never more than MaxBank cells, and never
            // into another hollow
            var cells = new bool[nx, nz];
            var dist = new int[nx, nz];
            var queue = new Queue<Vector2Int>();
            for (int x = 0; x < nx; x++)
                for (int z = 0; z < nz; z++)
                    if (label[x, z] == pond) { cells[x, z] = true; queue.Enqueue(new Vector2Int(x, z)); }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                if (label[c.x, c.y] != pond && (ground[c.x, c.y] > water + Clearance || dist[c.x, c.y] >= MaxBank)) continue;
                foreach (var d in dirs)
                {
                    var n = c + d;
                    if (!In(n) || cells[n.x, n.y] || ground[n.x, n.y] <= water || blocked[n.x, n.y]) continue;
                    cells[n.x, n.y] = true; dist[n.x, n.y] = dist[c.x, c.y] + 1; queue.Enqueue(n);
                }
            }

            // the surface: shared vertices, alpha by how many cells in from the outline
            var index = new Dictionary<int, int>();
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            int Vertex(int ix, int iz)
            {
                int k = iz*(nx + 1) + ix;
                if (index.TryGetValue(k, out int found)) return found;
                float x = minX + ix*Step, z = minZ + iz*Step;
                index[k] = verts.Count; verts.Add(new Vector3(x - origin.x, 0, z - origin.z)); uvs.Add(new Vector2(x, z));
                return verts.Count - 1;
            }
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    if (!cells[x, z]) continue;
                    int a = Vertex(x, z), b = Vertex(x + 1, z), c = Vertex(x, z + 1), d = Vertex(x + 1, z + 1);
                    tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d);
                }
            // vertices on the outline: a corner some of whose four cells are dry
            var steps = new int[verts.Count];
            for (int i = 0; i < steps.Length; i++) steps[i] = int.MaxValue;
            var front = new Queue<int>();
            var corner = new Dictionary<int, Vector2Int>();
            foreach (var kv in index) corner[kv.Value] = new Vector2Int(kv.Key%(nx + 1), kv.Key/(nx + 1));
            bool Cell(int x, int z) => x >= 0 && z >= 0 && x < nx && z < nz && cells[x, z];
            foreach (var kv in corner)
            {
                var p = kv.Value;
                if (Cell(p.x - 1, p.y - 1) && Cell(p.x, p.y - 1) && Cell(p.x - 1, p.y) && Cell(p.x, p.y)) continue;
                steps[kv.Key] = 0; front.Enqueue(kv.Key);
            }
            while (front.Count > 0)
            {
                int v = front.Dequeue(); var p = corner[v];
                foreach (var d in dirs)
                {
                    if (!index.TryGetValue((p.y + d.y)*(nx + 1) + p.x + d.x, out int n) || steps[n] <= steps[v] + 1) continue;
                    steps[n] = steps[v] + 1; front.Enqueue(n);
                }
            }
            var colors = new Color[verts.Count];
            for (int i = 0; i < colors.Length; i++)
            {
                float f = Mathf.Clamp01(steps[i]/(float)FadeCells);
                colors[i] = new Color(1, 1, 1, f*f*(3 - 2*f));
            }

            var mesh = new Mesh { name = "PondWaterSurface" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0); mesh.colors = colors;
            var normals = new Vector3[verts.Count]; for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up; mesh.normals = normals;
            mesh.RecalculateBounds();

            Undo.RegisterCompleteObjectUndo(old, "Rebuild pond water surface (V2)");
            int oldCells = ot.Length/6;
            var saved = FarmPondV1.SaveMesh(mesh);
            if (filter.sharedMesh != saved) { Undo.RecordObject(filter, "Rebuild pond water surface (V2)"); filter.sharedMesh = saved; }
            Undo.CollapseUndoOperations(group);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(surface.scene);
            return "Pond water surface rebuilt from the ground: " + raised + " bank heights raised out of the water, " + tris.Count/6 + " cells (was " + oldCells + "), " +
                   "outline under ground at least " + Clearance + " m above the water, alpha fading over " + FadeCells + " cells at the edge.";
        }

        // Outside the traced shoreline the bank is meant to rise out of the water (FarmPondV1: Freeboard over the first
        // BankRise metres); where a low spot of it still lies under the water level it held a puddle beside the pond.
        // Those heights only are lifted to that bank profile; nothing is lowered.
        const float Freeboard = .035f, BankRise = .45f;
        static int RaiseBank(Terrain terrain, Vector2[] shore, float water, float x0, float z0, float x1, float z1)
        {
            var data = terrain.terrainData; Vector3 tp = terrain.transform.position, size = data.size;
            int res = data.heightmapResolution;
            int ix0 = Mathf.Max(0, Mathf.FloorToInt((x0 - tp.x)/size.x*(res - 1))), ix1 = Mathf.Min(res - 1, Mathf.CeilToInt((x1 - tp.x)/size.x*(res - 1)));
            int iz0 = Mathf.Max(0, Mathf.FloorToInt((z0 - tp.z)/size.z*(res - 1))), iz1 = Mathf.Min(res - 1, Mathf.CeilToInt((z1 - tp.z)/size.z*(res - 1)));
            var h = data.GetHeights(ix0, iz0, ix1 - ix0 + 1, iz1 - iz0 + 1);
            int raised = 0;
            for (int z = 0; z < h.GetLength(0); z++)
                for (int x = 0; x < h.GetLength(1); x++)
                {
                    float wx = tp.x + (ix0 + x)/(float)(res - 1)*size.x, wz = tp.z + (iz0 + z)/(float)(res - 1)*size.z;
                    float sd = FarmPondV1.Signed(shore, wx, wz);
                    if (sd <= ShoreSlack) continue;
                    float bank = water + Freeboard*FarmPondV1.S(0, BankRise, sd);
                    float y = h[z, x]*size.y + tp.y;
                    if (y >= bank) continue;
                    h[z, x] = (bank - tp.y)/size.y; raised++;
                }
            if (raised > 0)
            {
                Undo.RegisterCompleteObjectUndo(data, "Rebuild pond water surface (V2)");
                data.SetHeights(ix0, iz0, h);
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
            }
            return raised;
        }

        // the lowest ground at a cell's corners and centre
        static float Ground(Terrain terrain, float minX, float minZ, int cx, int cz)
        {
            float y = terrain.transform.position.y, lowest = float.PositiveInfinity;
            for (int k = 0; k < 5; k++)
            {
                float ox = k == 4 ? .5f : k%2, oz = k == 4 ? .5f : k/2;
                lowest = Mathf.Min(lowest, terrain.SampleHeight(new Vector3(minX + (cx + ox)*Step, 0, minZ + (cz + oz)*Step)) + y);
            }
            return lowest;
        }
    }
}
