using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The time of day in the air (Map/Atmosphere): fireflies coming out at dusk over the pond, the waterfall's pool, the
/// edge of the wood behind the barn and the orchard;
/// dust drifting in the barn all day (as a child of the barn, so it moves with it). Each is a light particle system
/// (a few dozen particles at most) whose emission TimeOfDayParticles turns up and down with the game clock. "apply"
/// (again: rebuilt), "remove", "look" (pictures at dawn, dusk and in the barn). Temp/MapAtmosphere/request.txt while
/// the Editor is open, or the menu; writes Temp/MapAtmosphere.
/// </summary>
[InitializeOnLoad]
public static class MapAtmosphere
{
    private const string GroupName = "Atmosphere";
    private const string DustName = "Barn Dust";
    private const string Folder = "Assets/Art/Atmosphere";
    private const string Out = "Temp/MapAtmosphere";
    private const string Request = Out + "/request.txt";
    private static double nextPoll;

    static MapAtmosphere() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.Refresh();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string what = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        Run(what);
    }

    [MenuItem("Tools/Chick/Map/Atmosphere/Add Fireflies And Barn Dust")]
    private static void ApplyMenu() => Run("apply");

    [MenuItem("Tools/Chick/Map/Atmosphere/Remove")]
    private static void RemoveMenu() => Run("remove");

    private static void Run(string what)
    {
        Directory.CreateDirectory(Out);
        var report = new StringBuilder();
        try
        {
            GameObject map = GameObject.Find("Map");
            if (GameObject.Find("Farm Reference Map") == null || map == null) { report.AppendLine("The open scene has no 'Farm Reference Map' / 'Map'."); return; }
            if (what == "look") { Look(report); return; }
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Atmosphere");
            int undo = Undo.GetCurrentGroup();
            Transform old = map.transform.Find(GroupName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            Transform barn = Barn();
            Transform oldDust = barn != null ? barn.Find(DustName) : null;
            if (oldDust != null) Undo.DestroyObjectImmediate(oldDust.gameObject);
            AssetDatabase.DeleteAsset(Folder + "/M_MorningMist.mat");
            if (what == "apply") Build(map.transform, barn, report);
            else report.AppendLine("atmosphere removed");
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(map.scene);
            AssetDatabase.SaveAssets();
            if (what == "apply") Look(report);
            report.AppendLine("save the scene to keep it");
        }
        catch (System.Exception e) { report.AppendLine("FAILED: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(Out, "report.txt"), report.ToString());
            Debug.Log("Atmosphere: " + report);
        }
    }

    private static Transform Barn() => GameObject.Find("SM_Bld_Barn_01")?.transform;

    private static Terrain MainTerrain() => Terrain.activeTerrains.OrderByDescending(t => t.terrainData.size.x * t.terrainData.size.z).First();

    private static float Ground(Vector3 p)
    {
        Terrain t = MainTerrain();
        return t.SampleHeight(p) + t.transform.position.y;
    }

    private static Bounds? RendererBounds(string path)
    {
        GameObject go = GameObject.Find(path);
        if (go == null) return null;
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return null;
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // ---------------------------------------------------------------- build

    private static void Build(Transform map, Transform barn, StringBuilder report)
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art", "Atmosphere");
        Material firefly = MaterialAt("M_Firefly", "Farm/Glow", m => { m.SetColor("_Color", new Color(4f, 4.6f, 1.4f)); m.SetFloat("_Core", .3f); });
        Material dust = MaterialAt("M_BarnDust", "Farm/Glow", m => { m.SetColor("_Color", new Color(1.1f, .95f, .7f)); m.SetFloat("_Core", .55f); });

        var root = new GameObject(GroupName).transform;
        root.SetParent(map, false);
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Atmosphere");

        // Fireflies: by the pond, the waterfall's pool, the edge of the wood behind the barn, the orchard.
        var zones = new List<(string name, Bounds area)>();
        Bounds? pond = RendererBounds("Farm Pond/Water Surface");
        if (pond.HasValue) zones.Add(("Pond", Grow(pond.Value, 4f)));
        Bounds? pool = RendererBounds("Map/Waterfall/Pool/Water Surface");
        if (pool.HasValue) zones.Add(("Waterfall Pool", Grow(pool.Value, 3f)));
        if (barn != null) zones.Add(("Wood Edge", new Bounds(barn.position + new Vector3(0f, 0f, 24f), new Vector3(34f, 1f, 9f))));
        Bounds? orchard = RendererBounds("Orchard");
        if (orchard.HasValue) zones.Add(("Orchard", Grow(orchard.Value, 3f)));
        var fireflies = new GameObject("Fireflies").transform;
        fireflies.SetParent(root, false);
        var flySystems = new List<ParticleSystem>();
        foreach (var (name, area) in zones)
        {
            Vector3 c = area.center;
            c.y = Ground(c) + 1.1f;
            float size = area.size.x * area.size.z;
            int max = Mathf.Clamp(Mathf.RoundToInt(size / 9f), 12, 36);
            flySystems.Add(Fireflies(fireflies, name, c, new Vector3(area.size.x, 1.3f, area.size.z), max, firefly));
            report.AppendLine($"fireflies at {name}: {area.size.x:0} x {area.size.z:0} m, up to {max}");
        }
        // Out from a little after five, all out by a quarter past six, until the day ends at seven.
        fireflies.gameObject.AddComponent<TimeOfDayParticles>().Configure(flySystems.ToArray(),
            new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.84f, 0f), new Keyframe(.94f, 1f), new Keyframe(1f, 1f)));

        // Dust in the barn's hall, all day.
        if (barn != null)
        {
            var go = new GameObject(DustName);
            Undo.RegisterCreatedObjectUndo(go, "Atmosphere");
            go.transform.SetParent(barn, false);
            go.transform.localPosition = new Vector3(0f, 1.8f, 0f);
            go.transform.localRotation = Quaternion.identity;
            Dust(go, new Vector3(10.5f, 2.6f, 6f), dust);
            report.AppendLine("dust in the barn");
        }
    }

    private static Bounds Grow(Bounds b, float by) { b.Expand(new Vector3(by * 2f, 0f, by * 2f)); return b; }

    private static Material MaterialAt(string name, string shader, System.Action<Material> setup)
    {
        string path = $"{Folder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find(shader);
        setup(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static ParticleSystem NewSystem(Transform parent, string name, Material material, out ParticleSystemRenderer renderer)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        var main = ps.main;
        main.loop = true; main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        return ps;
    }

    private static ParticleSystem Fireflies(Transform parent, string name, Vector3 centre, Vector3 size, int max, Material material)
    {
        var ps = NewSystem(parent, name, material, out var renderer);
        ps.transform.position = centre;
        var main = ps.main;
        main.maxParticles = max;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.05f, .25f);
        main.startSize = new ParticleSystem.MinMaxCurve(.2f, .3f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, .7f), new Color(.85f, 1f, .55f));
        var emission = ps.emission; emission.rateOverTime = max / 7f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = size;
        var noise = ps.noise; noise.enabled = true; noise.separateAxes = true; noise.strengthX = .45f; noise.strengthY = .08f; noise.strengthZ = .45f;
        noise.frequency = .35f; noise.scrollSpeed = .2f; noise.damping = true;
        // Blinking: on and off a few times through each life.
        var colour = ps.colorOverLifetime; colour.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .12f), new GradientAlphaKey(.1f, .3f), new GradientAlphaKey(.9f, .45f),
                          new GradientAlphaKey(.05f, .62f), new GradientAlphaKey(1f, .8f), new GradientAlphaKey(0f, 1f) });
        colour.color = g;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        return ps;
    }

    private static void Dust(GameObject go, Vector3 size, Material material)
    {
        var ps = go.AddComponent<ParticleSystem>();
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var main = ps.main;
        main.loop = true; main.playOnAwake = true; main.prewarm = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.maxParticles = 70;
        main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 14f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.01f, .05f);
        main.startSize = new ParticleSystem.MinMaxCurve(.03f, .055f);
        main.startColor = new Color(1f, 1f, 1f, .6f);
        var emission = ps.emission; emission.rateOverTime = 6f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = size;
        var noise = ps.noise; noise.enabled = true; noise.separateAxes = true; noise.strengthX = .05f; noise.strengthY = .015f; noise.strengthZ = .05f;
        noise.frequency = .25f; noise.scrollSpeed = .05f; noise.damping = true;
        var colour = ps.colorOverLifetime; colour.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .2f), new GradientAlphaKey(1f, .8f), new GradientAlphaKey(0f, 1f) });
        colour.color = g;
    }

    // ---------------------------------------------------------------- pictures

    private static void Look(StringBuilder report)
    {
        Transform atmosphere = GameObject.Find("Map")?.transform.Find(GroupName);
        Transform barn = Barn();
        var controller = Object.FindFirstObjectByType<TimeOfDayVisualController>();
        var systems = Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)
            .Where(p => (atmosphere != null && p.transform.IsChildOf(atmosphere)) || p.name == DustName).ToList();
        foreach (ParticleSystem p in systems)
        {
            p.Simulate(12f, true, true);
            var parts = new ParticleSystem.Particle[p.main.maxParticles];
            int n = p.GetParticles(parts);
            Vector3 lo = n > 0 ? parts[0].position : Vector3.zero, hi = lo;
            for (int i = 0; i < n; i++) { lo = Vector3.Min(lo, parts[i].position); hi = Vector3.Max(hi, parts[i].position); }
            report.AppendLine($"  {p.name}: {n} particles, {lo} .. {hi} ({p.main.simulationSpace}), object at {p.transform.position}, lossy scale {p.transform.lossyScale}");
        }
        var sky = new GameViewCapture.SkyState();
        try
        {
            sky.Apply(report);
            Bounds? pond = RendererBounds("Farm Pond/Water Surface");
            if (pond.HasValue)
            {
                Vector3 c = pond.Value.center;
                Vector3 from = c + new Vector3(-9f, 0f, -9f); from.y = Ground(from) + 1.8f;
                controller?.Evaluate(.02f);
                Save(from, c + Vector3.up * .5f, "pond_morning");
                controller?.Evaluate(.97f);
                Save(from, c + Vector3.up * .8f, "pond_dusk");
            }
            if (barn != null)
            {
                Vector3 from = barn.position + new Vector3(-4f, 0f, -16f); from.y = Ground(from) + 2.2f;
                controller?.Evaluate(.97f);
                Save(from, barn.position + new Vector3(0f, 2f, 22f), "wood_dusk");
                controller?.Evaluate(.35f);
                Save(barn.TransformPoint(new Vector3(4.6f, 1.5f, -2.3f)), barn.TransformPoint(new Vector3(-3f, 1.4f, 1.2f)), "barn_dust");
            }
            Bounds? pool = RendererBounds("Map/Waterfall/Pool/Water Surface");
            if (pool.HasValue)
            {
                Vector3 c = pool.Value.center;
                Vector3 from = c + new Vector3(-10f, 0f, -4f); from.y = Ground(from) + 1.5f;
                controller?.Evaluate(.97f);
                Save(from, c + Vector3.up * 1.5f, "waterfall_dusk");
            }
        }
        finally
        {
            sky.Restore();
            foreach (ParticleSystem p in systems) p.Clear(true);
        }
        report.AppendLine($"pictures in {Out} ({systems.Count} particle systems shown)");
    }

    private static void Save(Vector3 from, Vector3 at, string name)
    {
        Texture2D tex = GameViewCapture.Shot(from, at);
        File.WriteAllBytes(Path.Combine(Out, name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
