using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A contact sheet of models, to choose from a pack: every model (.fbx, .glb, .prefab) under the folders listed in
/// Temp/ModelGallery/request.txt (one per line; a line "skip: text" leaves out paths containing the text) is stood
/// out of sight, lit by the scene's sun and framed on its own, and the pictures are laid out numbered left to right,
/// top to bottom in Temp/ModelGallery/sheet_N.png, 24 a sheet. report.txt lists each number with the model's path, its
/// size in metres, triangles and materials (with their shaders). Picked up while the Editor is open.
/// </summary>
[InitializeOnLoad]
public static class ModelGallery
{
    private const string Out = "Temp/ModelGallery";
    private const string Request = Out + "/request.txt";
    private const int Columns = 6, Rows = 4, CellW = 320, CellH = 240;
    private static readonly Vector3 Stage = new Vector3(3000f, 400f, 3000f);
    private static double nextPoll;

    static ModelGallery() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string[] lines = File.ReadAllLines(Request).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        File.Delete(Request);
        var report = new StringBuilder();
        try { Make(lines, report); }
        catch (System.Exception e) { report.AppendLine("FAILED: " + e); }
        File.WriteAllText(Path.Combine(Out, "report.txt"), report.ToString());
    }

    private static void Make(string[] lines, StringBuilder report)
    {
        Directory.CreateDirectory(Out);
        foreach (string old in Directory.GetFiles(Out, "sheet_*.png")) File.Delete(old);
        string[] skips = lines.Where(l => l.StartsWith("skip:")).Select(l => l.Substring(5).Trim()).ToArray();
        // "material: path" draws everything with that material; "split: path" draws each mesh of that model on its own.
        string matLine = lines.FirstOrDefault(l => l.StartsWith("material:"));
        Material overrideMat = matLine != null ? AssetDatabase.LoadAssetAtPath<Material>(matLine.Substring(9).Trim()) : null;
        string[] splits = lines.Where(l => l.StartsWith("split:")).Select(l => l.Substring(6).Trim()).ToArray();
        var paths = new List<string>();
        foreach (string folder in lines.Where(l => !l.Contains(":")))
            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string ext = Path.GetExtension(p).ToLowerInvariant();
                if ((ext == ".fbx" || ext == ".glb" || ext == ".gltf" || ext == ".prefab") && !skips.Any(s => p.Contains(s)) && !paths.Contains(p)) paths.Add(p);
            }
        paths.Sort(System.StringComparer.OrdinalIgnoreCase);
        var jobs = new List<(string path, string part)>();
        foreach (string p in paths) jobs.Add((p, null));
        foreach (string s in splits)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(s);
            if (model != null) foreach (MeshFilter f in model.GetComponentsInChildren<MeshFilter>(true)) jobs.Add((s, f.name));
        }
        report.AppendLine($"{jobs.Count} models");

        var sky = new GameViewCapture.SkyState();
        Texture2D sheet = null;
        int sheetIndex = 0, n = 0;
        try
        {
            sky.Apply(new StringBuilder());
            foreach (var (path, part) in jobs)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                if (go == null) go = Object.Instantiate(asset);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.transform.position = Stage;
                if (part != null)
                    foreach (MeshFilter f in go.GetComponentsInChildren<MeshFilter>(true))
                        if (f.name != part && f.TryGetComponent(out Renderer hide)) hide.enabled = false;
                if (overrideMat != null) foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(_ => overrideMat).ToArray();
                try
                {
                    var rs = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                    if (rs.Length == 0) { report.AppendLine($"  -  {path}: no renderers"); continue; }
                    Bounds b = rs[0].bounds;
                    foreach (Renderer r in rs) b.Encapsulate(r.bounds);
                    int tris = go.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh != null ? f.sharedMesh.triangles.Length / 3 : 0) +
                               go.GetComponentsInChildren<SkinnedMeshRenderer>().Sum(s => s.sharedMesh != null ? s.sharedMesh.triangles.Length / 3 : 0);
                    var mats = rs.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Select(m => $"{m.name} [{m.shader.name}]");
                    int parts = go.GetComponentsInChildren<MeshFilter>().Length;
                    report.AppendLine($"{n + 1,3}  {path}{(part != null ? " :: " + part : "")}  size {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m, {tris} tris, {parts} meshes, {string.Join(", ", mats)}");

                    if (n % (Columns * Rows) == 0)
                    {
                        if (sheet != null) Save(sheet, sheetIndex++);
                        sheet = new Texture2D(Columns * CellW, Rows * CellH, TextureFormat.RGB24, false);
                    }
                    Texture2D cell = Shoot(b);
                    int slot = n % (Columns * Rows), cx = slot % Columns, cy = Rows - 1 - slot / Columns;
                    sheet.SetPixels(cx * CellW, cy * CellH, CellW, CellH, cell.GetPixels());
                    Number(sheet, cx * CellW + 6, cy * CellH + CellH - 22, n + 1);
                    Object.DestroyImmediate(cell);
                    n++;
                }
                finally { Object.DestroyImmediate(go); }
            }
            if (sheet != null) Save(sheet, sheetIndex);
        }
        finally { sky.Restore(); }
    }

    private static Texture2D Shoot(Bounds b)
    {
        Camera camera = MapSurvey.TempCamera();
        try
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.62f, .72f, .8f);
            camera.fieldOfView = 35f;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 2000f;
            float radius = b.extents.magnitude;
            Vector3 dir = new Vector3(-.75f, .45f, -.6f).normalized;
            Vector3 from = b.center + dir * radius / Mathf.Sin(camera.fieldOfView * .5f * Mathf.Deg2Rad) * 1.05f;
            camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(b.center - from));
            return MapSurvey.Render(camera, CellW, CellH);
        }
        finally { Object.DestroyImmediate(camera.gameObject); }
    }

    private static void Save(Texture2D sheet, int index)
    {
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(Out, $"sheet_{index + 1}.png"), sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
    }

    // A small number in a dark box, drawn with a 3x5 pixel font scaled 3x.
    private static readonly string[] Digits =
    {
        "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
        "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111",
    };

    private static void Number(Texture2D tex, int x, int y, int value)
    {
        string s = value.ToString();
        int w = s.Length * 12 + 6;
        for (int i = 0; i < w; i++) for (int j = 0; j < 19; j++) tex.SetPixel(x - 3 + i, y - 2 + j, new Color(.1f, .1f, .1f));
        for (int k = 0; k < s.Length; k++)
        {
            string g = Digits[s[k] - '0'];
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 3; c++)
                    if (g[r * 3 + c] == '1')
                        for (int a = 0; a < 3; a++) for (int d = 0; d < 3; d++)
                            tex.SetPixel(x + k * 12 + c * 3 + a, y + (4 - r) * 3 + d, Color.white);
        }
    }
}
