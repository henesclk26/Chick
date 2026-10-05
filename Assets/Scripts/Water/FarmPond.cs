using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Shallow farm pond: a level water surface (this transform's height) over a carved terrain basin.
/// Answers how deep the water is at a point for wading and drinking, and drives the shared
/// surface ripples (read by Chick/PondWater) and droplet splashes.
/// </summary>
[DisallowMultipleComponent]
public sealed class FarmPond : MonoBehaviour
{
    [SerializeField] private Terrain terrain;
    [Tooltip("Shoreline in world XZ as a closed loop. Written by Tools/Chick/Pond.")]
    [SerializeField] private Vector2[] shoreline = new Vector2[0];
    [Tooltip("Water may reach this far past the shoreline where the bank dips below the surface.")]
    [SerializeField, Min(0f)] private float shoreMargin = .3f;
    [SerializeField] private Material dropletMaterial;
    [Tooltip("Scene objects the pond covers; the authoring tool re-enables them when the pond is removed.")]
    [SerializeField] private GameObject[] hiddenForPond = new GameObject[0];

    private const int MaxRipples = 12;
    private static readonly List<FarmPond> Active = new List<FarmPond>();
    private static readonly int RipplesId = Shader.PropertyToID("_PondRipples");
    private static readonly int PondTimeId = Shader.PropertyToID("_PondTime");
    private readonly Vector4[] ripples = new Vector4[MaxRipples];
    private int nextRipple;
    private Rect area;
    private ParticleSystem droplets;

    public float SurfaceHeight => transform.position.y;
    public GameObject[] HiddenForPond => hiddenForPond;

    /// <summary>Finds the pond whose water covers <paramref name="position"/>, with the water depth there.</summary>
    public static FarmPond Find(Vector3 position, out float depth)
    {
        for (int i = 0; i < Active.Count; i++)
            if (Active[i].TryGetDepth(position, out depth)) return Active[i];
        depth = 0f;
        return null;
    }

    public bool TryGetDepth(Vector3 position, out float depth)
    {
        depth = 0f;
        if (terrain == null || shoreline.Length < 3 || !area.Contains(new Vector2(position.x, position.z))) return false;
        if (!IsWithinShore(position.x, position.z)) return false;
        depth = SurfaceHeight - (terrain.SampleHeight(position) + terrain.transform.position.y);
        return depth > 0f;
    }

    public void Configure(Terrain basinTerrain, Vector2[] shore, float margin, Material droplet, GameObject[] hidden)
    {
        terrain = basinTerrain;
        shoreline = shore;
        shoreMargin = margin;
        dropletMaterial = droplet;
        hiddenForPond = hidden;
        RebuildArea();
    }

    /// <summary>A ring that spreads across the surface; strength 1 is a beak dip, .3 a footstep.</summary>
    public void Ripple(Vector3 position, float strength)
    {
        ripples[nextRipple] = new Vector4(position.x, position.z, Time.timeSinceLevelLoad, strength);
        nextRipple = (nextRipple + 1) % MaxRipples;
    }

    /// <summary>Throws <paramref name="count"/> droplets up and out from <paramref name="position"/>.</summary>
    public void Splash(Vector3 position, int count, float speed, float size = 1f)
    {
        if (droplets == null || count <= 0) return;
        var emit = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            Vector2 side = Random.insideUnitCircle;
            emit.position = position + new Vector3(side.x, 0f, side.y) * .01f;
            emit.velocity = new Vector3(side.x * .7f, Random.Range(.7f, 1.2f), side.y * .7f) * speed;
            emit.startSize = Random.Range(.012f, .022f) * size;
            emit.startLifetime = Random.Range(.28f, .5f);
            droplets.Emit(emit, 1);
        }
    }

    /// <summary>Drops falling straight down, as from a lifted beak.</summary>
    public void Drip(Vector3 position, float size = 1f)
    {
        if (droplets == null) return;
        var emit = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = new Vector3(Random.Range(-.03f, .03f), -.05f, Random.Range(-.03f, .03f)),
            startSize = Random.Range(.008f, .013f) * size,
            startLifetime = .35f
        };
        droplets.Emit(emit, 1);
    }

    private void OnEnable()
    {
        RebuildArea();
        Active.Add(this);
        RequestCameraTextures();
        if (droplets == null) droplets = CreateDroplets();
    }

    private void OnDisable()
    {
        Active.Remove(this);
        System.Array.Clear(ripples, 0, ripples.Length);
        Shader.SetGlobalVectorArray(RipplesId, ripples);
    }

    private void LateUpdate()
    {
        Shader.SetGlobalVectorArray(RipplesId, ripples);
        Shader.SetGlobalFloat(PondTimeId, Time.timeSinceLevelLoad);
    }

    private void RebuildArea()
    {
        if (shoreline == null || shoreline.Length == 0) { area = Rect.zero; return; }
        Vector2 min = shoreline[0], max = shoreline[0];
        foreach (var p in shoreline) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        area = Rect.MinMaxRect(min.x - shoreMargin, min.y - shoreMargin, max.x + shoreMargin, max.y + shoreMargin);
    }

    private bool IsWithinShore(float x, float z)
    {
        bool inside = false;
        float nearest = float.PositiveInfinity;
        var p = new Vector2(x, z);
        for (int i = 0, j = shoreline.Length - 1; i < shoreline.Length; j = i++)
        {
            Vector2 a = shoreline[i], b = shoreline[j];
            if ((a.y > z) != (b.y > z) && x < (b.x - a.x) * (z - a.y) / (b.y - a.y) + a.x) inside = !inside;
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            nearest = Mathf.Min(nearest, (a + ab * t - p).sqrMagnitude);
        }
        return inside || nearest <= shoreMargin * shoreMargin;
    }

    // The water shader reads the scene depth and colour behind the surface; make sure every
    // pipeline asset (the mobile one turns them off) provides them while a pond exists.
    private static void RequestCameraTextures()
    {
        var camera = Camera.main;
        if (camera == null || !camera.TryGetComponent(out UniversalAdditionalCameraData data)) return;
        data.requiresDepthOption = CameraOverrideOption.On;
        data.requiresColorOption = CameraOverrideOption.On;
    }

    private ParticleSystem CreateDroplets()
    {
        if (dropletMaterial == null) return null;
        var holder = new GameObject("Pond Droplets");
        holder.transform.SetParent(transform, false);
        var system = holder.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1f;
        main.maxParticles = 256;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 1f;
        main.startSpeed = 0f;
        main.startColor = new Color(.86f, .95f, 1f, .9f);
        var emission = system.emission;
        emission.enabled = false;
        var shape = system.shape;
        shape.enabled = false;
        var fade = system.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, .6f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var render = holder.GetComponent<ParticleSystemRenderer>();
        render.sharedMaterial = dropletMaterial;
        render.renderMode = ParticleSystemRenderMode.Billboard;
        render.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        render.receiveShadows = false;
        system.Play();
        return system;
    }
}
