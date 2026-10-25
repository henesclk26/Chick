using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Pictures of the map as the game shows it, for the Editor's map tools: the game's sky and light switched on in the
/// Editor and back off (SkyState), a few standard views over and around the farm, a camera rendering with
/// post-processing like the game camera (Shot), and two pictures stacked into one (Stack).
/// </summary>
internal static class GameViewCapture
{
    private const float DayTime = 0.35f;

    internal static List<(string name, Vector3 from, Vector3 at)> Views(Terrain main)
    {
        Vector3 o = main.transform.position, s = main.terrainData.size;
        Vector3 Ground(float x, float z, float up) => new Vector3(x, main.SampleHeight(new Vector3(x, 0f, z)) + o.y + up, z);
        Vector3 barn = GameObject.Find("SM_Bld_Barn_01")?.transform.position ?? o + s / 2f;
        float cx = o.x + s.x / 2f, cz = o.z + s.z / 2f;
        var list = new List<(string, Vector3, Vector3)>();
        // Chick's-eye: a few metres up, looking out over the edges.
        Vector3 a = Ground(barn.x, barn.z + 8f, 3.5f);
        list.Add(("north_from_barn", a, a + new Vector3(0f, 2f, 60f)));
        a = Ground(cx, o.z + 45f, 3.5f);
        list.Add(("south", a, a + new Vector3(0f, 1.5f, -60f)));
        a = Ground(o.x + 60f, cz, 3.5f);
        list.Add(("west", a, a + new Vector3(-60f, 1.5f, 0f)));
        a = Ground(o.x + s.x - 60f, cz, 3.5f);
        list.Add(("east", a, a + new Vector3(60f, 1.5f, 0f)));
        // From a raised camera over the farm, like the game's camera pulled up.
        a = new Vector3(barn.x - 10f, barn.y + 22f, barn.z - 45f);
        list.Add(("over_farm_north", a, a + new Vector3(10f, -14f, 90f)));
        // Over the whole map from the south.
        a = new Vector3(cx, o.y + 85f, o.z - 60f);
        list.Add(("whole_map", a, new Vector3(cx, o.y, cz + 20f)));
        return list;
    }

    internal static Texture2D Shot(Vector3 from, Vector3 at)
    {
        Camera camera = MapSurvey.TempCamera();
        try
        {
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.volumeLayerMask = ~0;
            data.renderShadows = true;
            camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
            return MapSurvey.Render(camera, 1200, 675);
        }
        finally { Object.DestroyImmediate(camera.gameObject); }
    }

    internal static Texture2D Stack(Texture2D top, Texture2D bottom)
    {
        int w = top.width, gap = 6;
        var tex = new Texture2D(w, top.height + bottom.height + gap, TextureFormat.RGB24, false);
        tex.SetPixels(0, 0, w, bottom.height, bottom.GetPixels());
        tex.SetPixels(0, bottom.height, w, gap, Enumerable.Repeat(Color.white, w * gap).ToArray());
        tex.SetPixels(0, bottom.height + gap, w, top.height, top.GetPixels());
        tex.Apply();
        return tex;
    }

    // ---------------------------------------------------------------- the game's sky in the Editor, and back

    internal sealed class SkyState
    {
        private TimeOfDayVisualController controller;
        private Light sun;
        private Material skybox;
        private UnityEngine.Rendering.AmbientMode ambientMode;
        private Color ambientSky, ambientEquator, ambientGround, fogColor, sunColor;
        private bool fog;
        private FogMode fogMode;
        private float fogStart, fogEnd, sunIntensity;
        private Quaternion sunRotation;
        private static readonly BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public bool Apply(StringBuilder report)
        {
            skybox = RenderSettings.skybox; ambientMode = RenderSettings.ambientMode;
            ambientSky = RenderSettings.ambientSkyColor; ambientEquator = RenderSettings.ambientEquatorColor; ambientGround = RenderSettings.ambientGroundColor;
            fog = RenderSettings.fog; fogMode = RenderSettings.fogMode; fogColor = RenderSettings.fogColor;
            fogStart = RenderSettings.fogStartDistance; fogEnd = RenderSettings.fogEndDistance;
            controller = Object.FindFirstObjectByType<TimeOfDayVisualController>();
            if (controller == null) { report.AppendLine("no TimeOfDayVisualController: default sky"); return true; }
            sun = (Light)typeof(TimeOfDayVisualController).GetField("sun", Any).GetValue(controller);
            if (sun != null) { sunColor = sun.color; sunIntensity = sun.intensity; sunRotation = sun.transform.rotation; }
            typeof(TimeOfDayVisualController).GetMethod("Awake", Any).Invoke(controller, null);
            controller.Evaluate(DayTime);
            report.AppendLine($"game sky on (time {DayTime})");
            return true;
        }

        public void Restore()
        {
            if (controller != null)
            {
                FieldInfo field = typeof(TimeOfDayVisualController).GetField("runtimeSky", Any);
                if (field.GetValue(controller) is Material m) Object.DestroyImmediate(m);
                field.SetValue(controller, null);
                Transform probe = controller.transform.Find("Sky Reflection Probe");
                if (probe != null) Object.DestroyImmediate(probe.gameObject);
                if (sun != null) { sun.color = sunColor; sun.intensity = sunIntensity; sun.transform.rotation = sunRotation; }
            }
            RenderSettings.skybox = skybox; RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientSkyColor = ambientSky; RenderSettings.ambientEquatorColor = ambientEquator; RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.fog = fog; RenderSettings.fogMode = fogMode; RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart; RenderSettings.fogEndDistance = fogEnd;
        }
    }
}
