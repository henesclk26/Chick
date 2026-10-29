using UnityEngine;

/// <summary>
/// Now and then a gust of wind: calm for <see cref="calmSeconds"/>, then a gust of <see cref="gustSeconds"/> that
/// swells in, blows a while and dies away, each from about the prevailing quarter (<see cref="prevailing"/>, give or
/// take <see cref="directionSpread"/> degrees). While it blows, thin wisps of wind stream past around the camera
/// (a particle system built here, trails only), the trees on Chick/TreeSway lean and toss with it (global
/// _ChickWindGust: xy direction, z strength 0 to 1), and the falling leaves (FallingLeaves) are swept along. Other code
/// reads <see cref="Direction"/> and <see cref="Strength"/>. Presentation only.
/// </summary>
[DefaultExecutionOrder(-50)]
public sealed class WindGusts : MonoBehaviour
{
    [SerializeField] private Vector2 prevailing = new Vector2(.82f, .57f);
    [SerializeField, Range(0f, 90f)] private float directionSpread = 25f;
    [SerializeField, Tooltip("Seconds of calm between gusts (random in this range).")]
    private Vector2 calmSeconds = new Vector2(25f, 55f);
    [SerializeField, Tooltip("Seconds a gust lasts (random in this range), swell and fade included.")]
    private Vector2 gustSeconds = new Vector2(6f, 10f);
    [SerializeField, Min(.1f)] private float swell = 2f, fade = 3f;
    [SerializeField, Tooltip("Trails only, unlit, transparent white (M_WindStreak).")]
    private Material streakMaterial;
    [SerializeField, Min(0f), Tooltip("Wisps per second at the height of a gust.")]
    private float wispsPerSecond = 30f;
    [SerializeField, Min(0f)] private float wispSpeed = 8f;

    public static Vector3 Direction { get; private set; } = new Vector3(.82f, 0f, .57f).normalized;
    /// <summary>0 when calm, up to 1 at the height of a gust.</summary>
    public static float Strength { get; private set; }

    private static readonly int GustId = Shader.PropertyToID("_ChickWindGust");
    private ParticleSystem wisps;
    private float clock, length;
    private bool blowing;

    private void OnEnable()
    {
        clock = 0f;
        blowing = false;
        length = Random.Range(calmSeconds.x*.4f, calmSeconds.y*.6f);   // the first one comes a little sooner
        if (wisps == null) wisps = BuildWisps();
    }

    private void OnDisable()
    {
        Strength = 0f;
        Shader.SetGlobalVector(GustId, Vector4.zero);
    }

    private void Update()
    {
        clock += Time.deltaTime;
        if (clock >= length)
        {
            clock = 0f;
            blowing = !blowing;
            length = blowing ? Random.Range(gustSeconds.x, gustSeconds.y) : Random.Range(calmSeconds.x, calmSeconds.y);
            if (blowing)
            {
                float a = Mathf.Atan2(prevailing.y, prevailing.x) + Random.Range(-directionSpread, directionSpread)*Mathf.Deg2Rad;
                Direction = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }
        }

        // swell in, blow with a little unrest, die away
        float s = 0f;
        if (blowing)
        {
            s = Mathf.SmoothStep(0f, 1f, clock/swell)*Mathf.SmoothStep(0f, 1f, (length - clock)/fade);
            s *= .85f + .15f*Mathf.Sin(Time.time*1.7f)*Mathf.Sin(Time.time*.63f + 1f);
        }
        Strength = s;
        Shader.SetGlobalVector(GustId, new Vector4(Direction.x, Direction.z, s, 0f));
        UpdateWisps(s);
    }

    private void UpdateWisps(float s)
    {
        var cam = Camera.main;
        if (wisps == null || cam == null) return;
        // a box of air in front of the camera, from a few metres out (a wisp right at the lens looks like a smear)
        var ahead = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        wisps.transform.position = cam.transform.position + ahead*10f - Direction*3f + Vector3.up*.8f;
        wisps.transform.rotation = Quaternion.LookRotation(ahead);
        var emission = wisps.emission;
        emission.rateOverTime = wispsPerSecond*s;
        var velocity = wisps.velocityOverLifetime;
        velocity.x = Direction.x*wispSpeed*Mathf.Max(s, .4f);
        velocity.z = Direction.z*wispSpeed*Mathf.Max(s, .4f);
    }

    private ParticleSystem BuildWisps()
    {
        var go = new GameObject("Wind Wisps");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        main.startSpeed = 0f;
        main.startSize = .045f;   // the trail's width, in metres
        main.maxParticles = 120;
        main.playOnAwake = true;
        main.loop = true;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(24f, 3f, 12f);

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = 0f; velocity.y = 0f; velocity.z = 0f;

        // the wisps curl and loop a little as they go
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 1.1f;
        noise.frequency = .35f;
        noise.scrollSpeed = .4f;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        var fadeInOut = new Gradient();
        fadeInOut.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .3f), new GradientAlphaKey(1f, .65f), new GradientAlphaKey(0f, 1f) });
        colour.color = fadeInOut;

        // only the trail shows: a thin streak, thickest in the middle, faint at both ends
        var trails = ps.trails;
        trails.enabled = true;
        trails.ratio = 1f;
        trails.lifetime = .45f;
        trails.minVertexDistance = .12f;
        trails.dieWithParticles = true;
        trails.inheritParticleColor = true;
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.5f, 1f), new Keyframe(1f, 0f)));
        var along = new Gradient();
        along.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.38f, .5f), new GradientAlphaKey(0f, 1f) });
        trails.colorOverTrail = along;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.None;
        renderer.trailMaterial = streakMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        ps.Play();
        return ps;
    }
}
