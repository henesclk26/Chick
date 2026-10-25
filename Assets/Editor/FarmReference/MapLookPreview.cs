using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Preview only (nothing is kept): the game's look now, and with a warmer, softer grade. Turns on the game's sky in the
/// Editor (TimeOfDayVisualController) and renders with post-processing as the game camera does, first as the scene is
/// (its Global Volume), then with a temporary volume on top: warmer white balance and highlights, a little more
/// saturation and bloom, cooler shade, and lighter sun shadows and a few more clouds. Each view is one picture, "now"
/// above "proposed", in late morning and late afternoon. Then the volume, sky and light are put back.
/// Temp/MapLook/request.txt while the Editor is open, or the menu; writes Temp/MapLook.
/// </summary>
[InitializeOnLoad]
public static class MapLookPreview
{
    private const string Out = "Temp/MapLook";
    private const string Request = Out + "/request.txt";
    private static readonly (string name, float time)[] Times = { ("morning", 0.35f), ("afternoon", 0.8f) };
    private static double nextPoll;

    static MapLookPreview() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(Request);
        Preview();
    }

    [MenuItem("Tools/Chick/Map/Preview Warmer Look")]
    private static void Preview()
    {
        Directory.CreateDirectory(Out);
        var report = new StringBuilder();
        var sky = new GameViewCapture.SkyState();
        GameObject volumeObject = null;
        VolumeProfile profile = null;
        Light sun = null;
        float shadowStrength = 1f;
        try
        {
            if (GameObject.Find("Farm Reference Map") == null) { report.AppendLine("The open scene has no 'Farm Reference Map'."); return; }
            Terrain main = Terrain.activeTerrains.Where(t => !t.name.StartsWith("Backdrop Hills"))
                .OrderByDescending(t => t.terrainData.size.x * t.terrainData.size.z).First();
            sky.Apply(report);
            var controller = Object.FindFirstObjectByType<TimeOfDayVisualController>();
            sun = RenderSettings.sun != null ? RenderSettings.sun : Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            if (sun != null) shadowStrength = sun.shadowStrength;
            report.AppendLine($"sun {(sun != null ? sun.name : "-")}, shadow strength {shadowStrength}");

            var views = Views(main);
            var before = new List<Texture2D>();
            var after = new List<Texture2D>();
            foreach (var (_, time) in Times)
            {
                controller?.Evaluate(time);
                foreach (var v in views) before.Add(GameViewCapture.Shot(v.from, v.at));
            }

            profile = Proposed(out float cloudCoverage);
            volumeObject = new GameObject("Look Preview Volume") { hideFlags = HideFlags.HideAndDontSave };
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.sharedProfile = profile;
            if (sun != null) sun.shadowStrength = 0.68f;
            Material skyMaterial = RenderSettings.skybox;
            float coverage = skyMaterial != null && skyMaterial.HasProperty("_CloudCoverage") ? skyMaterial.GetFloat("_CloudCoverage") : -1f;
            if (coverage >= 0f) skyMaterial.SetFloat("_CloudCoverage", cloudCoverage);
            report.AppendLine($"proposed: shadow strength 0.68, cloud coverage {coverage:0.00} -> {cloudCoverage:0.00}");
            foreach (var (_, time) in Times)
            {
                controller?.Evaluate(time);
                foreach (var v in views) after.Add(GameViewCapture.Shot(v.from, v.at));
            }
            if (coverage >= 0f) skyMaterial.SetFloat("_CloudCoverage", coverage);

            int k = 0;
            foreach (var (timeName, _) in Times)
                foreach (var v in views)
                {
                    Texture2D pair = GameViewCapture.Stack(before[k], after[k]);
                    File.WriteAllBytes(Path.Combine(Out, $"{timeName}_{v.name}.png"), pair.EncodeToPNG());
                    Object.DestroyImmediate(pair);
                    k++;
                }
            foreach (Texture2D t in before.Concat(after)) Object.DestroyImmediate(t);
        }
        catch (System.Exception e) { report.AppendLine("FAILED: " + e); }
        finally
        {
            if (volumeObject != null) Object.DestroyImmediate(volumeObject);
            if (profile != null) Object.DestroyImmediate(profile);
            if (sun != null) sun.shadowStrength = shadowStrength;
            sky.Restore();
            File.WriteAllText(Path.Combine(Out, "report.txt"), report.ToString());
            Debug.Log("Look preview: " + report);
        }
    }

    // On top of the scene's profile (bloom .25, white balance 5, contrast 6, saturation 8, vignette .2).
    private static VolumeProfile Proposed(out float cloudCoverage)
    {
        cloudCoverage = 0.82f;
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        p.hideFlags = HideFlags.HideAndDontSave;
        var bloom = p.Add<Bloom>(true);
        bloom.threshold.Override(0.9f);
        bloom.intensity.Override(0.4f);
        bloom.scatter.Override(0.65f);
        bloom.tint.Override(new Color(1f, 0.96f, 0.9f));
        var white = p.Add<WhiteBalance>(true);
        white.temperature.Override(9f);
        white.tint.Override(1.5f);
        var colour = p.Add<ColorAdjustments>(true);
        colour.postExposure.Override(0.05f);
        colour.contrast.Override(6f);
        colour.saturation.Override(10f);
        var smh = p.Add<ShadowsMidtonesHighlights>(true);
        smh.shadows.Override(new Vector4(0.95f, 0.98f, 1.06f, 0.03f));   // cool, lifted shade
        smh.midtones.Override(new Vector4(1f, 1f, 1f, 0f));
        smh.highlights.Override(new Vector4(1.04f, 1.01f, 0.96f, 0.01f)); // warm light
        var vignette = p.Add<Vignette>(true);
        vignette.intensity.Override(0.2f);
        vignette.smoothness.Override(0.4f);
        return p;
    }

    private static List<(string name, Vector3 from, Vector3 at)> Views(Terrain main)
    {
        Vector3 o = main.transform.position;
        Vector3 Ground(float x, float z, float up) => new Vector3(x, main.SampleHeight(new Vector3(x, 0f, z)) + o.y + up, z);
        Vector3 barn = GameObject.Find("SM_Bld_Barn_01")?.transform.position ?? o;
        var list = new List<(string, Vector3, Vector3)>();
        // Like the game camera behind the chick: a couple of metres up, in the yard looking at the barn.
        Vector3 a = Ground(barn.x + 6f, barn.z - 22f, 2.2f);
        list.Add(("barn_yard", a, barn + new Vector3(0f, 3f, 0f)));
        a = new Vector3(barn.x - 10f, barn.y + 22f, barn.z - 45f);
        list.Add(("over_farm", a, a + new Vector3(10f, -14f, 90f)));
        Transform pond = GameObject.Find("Farm Pond")?.transform;
        if (pond != null)
        {
            Vector3 c = pond.GetComponentsInChildren<Renderer>().Aggregate(new Bounds(pond.position, Vector3.zero), (b, r) => { b.Encapsulate(r.bounds); return b; }).center;
            a = Ground(c.x - 14f, c.z - 14f, 2.5f);
            list.Add(("pond", a, Ground(c.x, c.z, 0.5f)));
        }
        a = Ground(barn.x - 8f, barn.z + 14f, 2f);
        list.Add(("wood", a, a + new Vector3(4f, 3f, 40f)));
        return list;
    }
}
