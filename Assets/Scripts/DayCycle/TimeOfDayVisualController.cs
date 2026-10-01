using UnityEngine;
using UnityEngine.Rendering;

public sealed class TimeOfDayVisualController : MonoBehaviour
{
    [SerializeField] private Light sun;
    [SerializeField] private Material skyTemplate;
    [SerializeField] private Gradient sunColor = MakeGradient(new Color(1,.79f,.55f), new Color(1,.98f,.9f), new Color(1,.88f,.68f), new Color(1,.62f,.36f), new Color(.8f,.72f,.78f));
    [SerializeField] private Gradient skyColor = MakeGradient(new Color(.24f,.51f,.78f), new Color(.16f,.48f,.83f), new Color(.24f,.5f,.78f), new Color(.34f,.43f,.65f), new Color(.21f,.28f,.45f));
    [SerializeField] private Gradient horizonColor = MakeGradient(new Color(1,.78f,.57f), new Color(.72f,.87f,.98f), new Color(.9f,.85f,.71f), new Color(1,.59f,.37f), new Color(.64f,.48f,.56f));
    [SerializeField] private AnimationCurve sunIntensity = new AnimationCurve(new Keyframe(0,1.05f), new Keyframe(.42f,1.8f),new Keyframe(.75f,1.35f),new Keyframe(.92f,.8f),new Keyframe(1,.45f));
    [SerializeField] private AnimationCurve sunElevation = new AnimationCurve(new Keyframe(0,18),new Keyframe(.42f,65),new Keyframe(.75f,34),new Keyframe(1,7));

    // Stylized sky (Farm/Stylized Sky): sun disk, sun-aware glow and lit cumulus. Untick to use skyTemplate again.
    // Keys: 07:00, 09:30, 12:00, 16:00, 18:00, 19:00.
    [Header("Stylized Sky")]
    [SerializeField] private bool useStylizedSky = true;
    [SerializeField] private Material stylizedSkyTemplate;
    [SerializeField] private Gradient sunGlowColor = MakeGradient(DayKeys, new Color(1,.62f,.40f), new Color(1,.85f,.68f), new Color(1,.95f,.88f), new Color(1,.82f,.58f), new Color(1,.5f,.26f), new Color(.92f,.42f,.36f));
    [SerializeField] private Gradient antiSunColor = MakeGradient(DayKeys, new Color(.82f,.66f,.78f), new Color(.8f,.85f,.95f), new Color(.8f,.88f,.98f), new Color(.85f,.8f,.85f), new Color(.84f,.56f,.7f), new Color(.6f,.46f,.66f));
    [SerializeField] private AnimationCurve twilight = new AnimationCurve(new Keyframe(0,.8f),new Keyframe(.21f,.1f),new Keyframe(.42f,0),new Keyframe(.75f,.2f),new Keyframe(.92f,.85f),new Keyframe(1,1));
    [SerializeField] private Gradient cloudLitColor = MakeGradient(DayKeys, new Color(1,.88f,.78f), new Color(1,.98f,.95f), Color.white, new Color(1,.96f,.88f), new Color(1,.74f,.56f), new Color(.9f,.62f,.64f));
    [SerializeField] private Gradient cloudShadowColor = MakeGradient(DayKeys, new Color(.62f,.6f,.72f), new Color(.68f,.74f,.86f), new Color(.7f,.78f,.9f), new Color(.68f,.7f,.82f), new Color(.62f,.5f,.62f), new Color(.5f,.42f,.56f));
    [SerializeField] private AnimationCurve starIntensity = new AnimationCurve(new Keyframe(0,0),new Keyframe(.9f,0),new Keyframe(1,.55f));
    [SerializeField] private AnimationCurve moonIntensity = new AnimationCurve(new Keyframe(0,0),new Keyframe(.85f,0),new Keyframe(.92f,.25f),new Keyframe(1,.7f));
    private static readonly float[] DayKeys = { 0, .21f, .42f, .75f, .92f, 1 };
    private Material runtimeSky, originalSky;
    private bool stylized;
    private AmbientMode originalAmbientMode;
    private Color originalSkyAmbient, originalEquator, originalGround, originalSunColor;
    private Quaternion originalSunRotation;
    private float originalIntensity;
    private void Awake()
    {
        originalSky = RenderSettings.skybox;
        originalAmbientMode = RenderSettings.ambientMode;
        originalSkyAmbient = RenderSettings.ambientSkyColor;
        originalEquator = RenderSettings.ambientEquatorColor;
        originalGround = RenderSettings.ambientGroundColor;
        originalSunColor = sun.color; originalIntensity = sun.intensity; originalSunRotation = sun.transform.rotation;
        stylized = useStylizedSky && stylizedSkyTemplate != null;
        runtimeSky = new Material(stylized ? stylizedSkyTemplate : skyTemplate);
        RenderSettings.skybox = runtimeSky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        Evaluate(0);
    }
    public void Evaluate(float time)
    {
        if (runtimeSky == null) return;
        time = Mathf.Clamp01(time);
        sun.color = sunColor.Evaluate(time);
        sun.intensity = sunIntensity.Evaluate(time);
        sun.transform.rotation = Quaternion.Euler(sunElevation.Evaluate(time), Mathf.Lerp(-65,65,time),0);
        Color upperSky = skyColor.Evaluate(time);
        // On the 07:00-19:00 day, clear the morning haze from 09:00 to 10:00,
        // keep a blue horizon until 15:00, then rejoin the original sunset by 16:00.
        float clearDay = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f / 12f, 3f / 12f, time)) *
            (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f / 12f, 9f / 12f, time)));
        Color blueHorizon = Color.Lerp(upperSky, Color.white, .30f);
        runtimeSky.SetColor("_SkyColor", upperSky);
        runtimeSky.SetColor("_HorizonColor", Color.Lerp(horizonColor.Evaluate(time), blueHorizon, clearDay));
        runtimeSky.SetFloat("_HorizonBlendHeight", Mathf.Lerp(.65f, .42f, clearDay));
        runtimeSky.SetColor("_CloudColor",Color.Lerp(Color.white,horizonColor.Evaluate(time),.22f));
        if (stylized)
        {
            Vector3 toSun = -sun.transform.forward;
            Vector3 opposite = new Vector3(-toSun.x, 0, -toSun.z).normalized;
            runtimeSky.SetVector("_SunDirection", toSun);
            runtimeSky.SetColor("_SunColor", sun.color);
            runtimeSky.SetColor("_SunGlowColor", sunGlowColor.Evaluate(time));
            runtimeSky.SetColor("_AntiSunColor", antiSunColor.Evaluate(time));
            runtimeSky.SetFloat("_Twilight", twilight.Evaluate(time));
            runtimeSky.SetColor("_CloudLitColor", cloudLitColor.Evaluate(time));
            runtimeSky.SetColor("_CloudShadowColor", cloudShadowColor.Evaluate(time));
            runtimeSky.SetFloat("_StarIntensity", starIntensity.Evaluate(time));
            runtimeSky.SetFloat("_MoonIntensity", moonIntensity.Evaluate(time));
            runtimeSky.SetVector("_MoonDirection", opposite + Vector3.up * .35f);   // rises opposite the setting sun
        }
        RenderSettings.ambientSkyColor = skyColor.Evaluate(time) * .8f;
        RenderSettings.ambientEquatorColor = Color.Lerp(horizonColor.Evaluate(time),Color.white,.35f) * .55f;
        RenderSettings.ambientGroundColor = new Color(.24f,.23f,.26f);
    }
    private void OnDestroy()
    {
        if (runtimeSky == null) return;
        RenderSettings.skybox = originalSky;
        RenderSettings.ambientMode = originalAmbientMode;
        RenderSettings.ambientSkyColor = originalSkyAmbient;
        RenderSettings.ambientEquatorColor = originalEquator;
        RenderSettings.ambientGroundColor = originalGround;
        if (sun != null) { sun.color=originalSunColor; sun.intensity=originalIntensity; sun.transform.rotation=originalSunRotation; }
        Destroy(runtimeSky);
    }
    private static Gradient MakeGradient(float[] times, params Color[] colors)
    {
        var keys = new GradientColorKey[colors.Length];
        for (int i = 0; i < colors.Length; i++) keys[i] = new GradientColorKey(colors[i], times[i]);
        var gradient = new Gradient();
        gradient.SetKeys(keys, new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
        return gradient;
    }
    private static Gradient MakeGradient(Color a,Color b,Color c,Color d,Color e)
    {
        var gradient = new Gradient();
        gradient.SetKeys(new[]{new GradientColorKey(a,0),new GradientColorKey(b,.42f),new GradientColorKey(c,.75f),new GradientColorKey(d,.92f),new GradientColorKey(e,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
        return gradient;
    }
}
