using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Pictures of the open scene at chosen times of day, under the game's sky (TimeOfDayVisualController), read line by
/// line from Temp/TimeShot/request.txt:
/// - "name time fromX fromY fromZ atX atY atZ [fov]": a picture (time 0 = 07:00, 1 = 19:00, so 0.0417 is 07:30). A
///   height written "g+1.5" is 1.5 m over the ground there.
/// - "size width height [supersample]": the size of the pictures that follow (default 1200 675), drawn that many times
///   larger and scaled down for smooth edges.
/// - "bird chick|chicken x z yaw clip second [scale]": the chick or the grown chicken (Animals_3D/Chicken) standing on
///   the ground there, posed as in that clip (idle, walk, run, peck, eat, peep, flapping, jump...) at that second, for
///   the pictures that follow; "clear" takes them away again.
/// - "hide object name": that object switched off for the pictures that follow.
/// - "shader material path|shader name": that material drawn with another shader.
/// Everything is put back afterwards; nothing is saved. Picked up while the Editor is open; writes
/// Temp/TimeShot/name.png and report.txt.
/// </summary>
[InitializeOnLoad]
public static class TimeShot
{
    private const string Out = "Temp/TimeShot";
    private const string Request = Out + "/request.txt";
    private const string Birds = "Assets/Animals_3D/Chicken/";
    private static double nextPoll;

    static TimeShot() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string[] lines = File.ReadAllLines(Request).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToArray();
        File.Delete(Request);
        var report = new StringBuilder();
        var sky = new GameViewCapture.SkyState();
        var swapped = new List<(Material, Shader)>();
        var hidden = new List<GameObject>();
        var placed = new List<GameObject>();
        int width = 1200, height = 675, super = 1;
        try
        {
            sky.Apply(report);
            var clock = Object.FindFirstObjectByType<TimeOfDayVisualController>();
            foreach (string line in lines)
            {
                string[] p = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                float F(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
                switch (p[0])
                {
                    case "shader":
                    {
                        string[] parts = line.Substring(7).Split('|');
                        var m = AssetDatabase.LoadAssetAtPath<Material>(parts[0].Trim());
                        var s = Shader.Find(parts[1].Trim());
                        if (m == null || s == null) { report.AppendLine("cannot swap: " + line); break; }
                        swapped.Add((m, m.shader));
                        m.shader = s;
                        break;
                    }
                    case "size":
                        width = (int)F(1); height = (int)F(2); super = p.Length > 3 ? Mathf.Max(1, (int)F(3)) : 1;
                        break;
                    case "hide":
                    {
                        GameObject go = GameObject.Find(line.Substring(5).Trim());
                        if (go == null || !go.activeSelf) { report.AppendLine("nothing to hide: " + line); break; }
                        go.SetActive(false);
                        hidden.Add(go);
                        break;
                    }
                    case "bird":
                        placed.Add(Bird(p[1], F(2), F(3), F(4), p[5], F(6), p.Length > 7 ? F(7) : 1f, report));
                        break;
                    case "clear":
                        foreach (GameObject go in placed) if (go != null) Object.DestroyImmediate(go);
                        placed.Clear();
                        break;
                    default:
                    {
                        if (clock != null) clock.Evaluate(F(1));
                        float x0 = F(2), z0 = F(4), x1 = F(5), z1 = F(7);
                        var from = new Vector3(x0, Height(p[3], x0, z0), z0);
                        var at = new Vector3(x1, Height(p[6], x1, z1), z1);
                        Texture2D tex = Shot(from, at, p.Length > 8 ? F(8) : 60f, width, height, super);
                        File.WriteAllBytes(Path.Combine(Out, p[0] + ".png"), tex.EncodeToPNG());
                        Object.DestroyImmediate(tex);
                        report.AppendLine(p[0]);
                        break;
                    }
                }
            }
        }
        catch (System.Exception e) { report.AppendLine("FAILED: " + e); }
        finally
        {
            foreach (GameObject go in placed) if (go != null) Object.DestroyImmediate(go);
            foreach (GameObject go in hidden) if (go != null) go.SetActive(true);
            sky.Restore();
            foreach (var (m, s) in swapped) m.shader = s;
        }
        File.WriteAllText(Path.Combine(Out, "report.txt"), report.ToString());
    }

    private static float Height(string token, float x, float z) =>
        token.StartsWith("g") ? Ground(x, z) + (token.Length > 1 ? float.Parse(token.Substring(1), CultureInfo.InvariantCulture) : 0f)
                              : float.Parse(token, CultureInfo.InvariantCulture);

    // The ground (or whatever solid stands there: a rock, the bridge) at x, z.
    internal static float Ground(float x, float z)
    {
        float best = float.NegativeInfinity;
        foreach (Terrain t in Terrain.activeTerrains)
        {
            Vector3 o = t.transform.position, s = t.terrainData.size;
            if (x >= o.x && x <= o.x + s.x && z >= o.z && z <= o.z + s.z) best = Mathf.Max(best, t.SampleHeight(new Vector3(x, 0f, z)) + o.y);
        }
        if (Physics.Raycast(new Vector3(x, (float.IsInfinity(best) ? 0f : best) + 30f, z), Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore))
            best = Mathf.Max(best, hit.point.y);
        return float.IsInfinity(best) ? 0f : best;
    }

    internal static GameObject Bird(string kind, float x, float z, float yaw, string clipName, float second, float scale, StringBuilder report)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Birds + "Resources/Prefabs/" + kind + ".prefab");
        var go = (GameObject)Object.Instantiate(prefab);
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetPositionAndRotation(new Vector3(x, Ground(x, z), z), Quaternion.Euler(0f, yaw, 0f));
        go.transform.localScale *= scale;
        foreach (string clip in new[] { clipName, "wing_idle" })
        {
            var anim = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Birds}AnimationClips/{kind}/twoLayer/{clip}.anim");
            if (anim == null) { report.AppendLine($"no clip {kind}/{clip}"); continue; }
            if (clip == "wing_idle" && clipName.Contains("flap")) continue;
            anim.SampleAnimation(go, Mathf.Repeat(second, Mathf.Max(anim.length, .001f)));
        }
        return go;
    }

    internal static Texture2D Shot(Vector3 from, Vector3 at, float fov, int width, int height, int super)
    {
        Camera camera = MapSurvey.TempCamera();
        try
        {
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = fov;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 1500f;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.volumeLayerMask = ~0;
            data.renderShadows = true;
            camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
            Texture2D big = MapSurvey.Render(camera, width * super, height * super);
            if (super == 1) return big;
            // Scaled down by averaging each super x super block.
            Color[] src = big.GetPixels();
            var dst = new Color[width * height];
            float k = 1f / (super * super);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Color sum = Color.clear;
                    for (int j = 0; j < super; j++)
                        for (int i = 0; i < super; i++) sum += src[(y * super + j) * width * super + x * super + i];
                    dst[y * width + x] = sum * k;
                }
            Object.DestroyImmediate(big);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.SetPixels(dst);
            tex.Apply();
            return tex;
        }
        finally { Object.DestroyImmediate(camera.gameObject); }
    }
}
