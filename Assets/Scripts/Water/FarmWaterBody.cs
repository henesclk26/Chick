using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Water the bird can wade in and drink from (the farm pond, the sheep trough). Answers how deep the water is
/// at a point, keeps the ring ripples every water surface shares (read by Chick/PondWater), and throws droplets.
/// </summary>
public abstract class FarmWaterBody : MonoBehaviour
{
    [SerializeField] protected Material dropletMaterial;

    private const int MaxRipples = 12;
    private static readonly List<FarmWaterBody> Active = new List<FarmWaterBody>();
    private static readonly int RipplesId = Shader.PropertyToID("_PondRipples");
    private static readonly int PondTimeId = Shader.PropertyToID("_PondTime");
    private static readonly Vector4[] Ripples = new Vector4[MaxRipples];
    private static int nextRipple;
    private static int uploadedFrame = -1;
    private ParticleSystem droplets;

    public virtual float SurfaceHeight => transform.position.y;

    /// <summary>Finds the water covering <paramref name="position"/>, with the water depth there.</summary>
    public static FarmWaterBody Find(Vector3 position, out float depth)
    {
        for (int i = 0; i < Active.Count; i++)
            if (Active[i].TryGetDepth(position, out depth)) return Active[i];
        depth = 0f;
        return null;
    }

    /// <summary>Depth of the water column at the point's XZ, if this water covers it.</summary>
    public abstract bool TryGetDepth(Vector3 position, out float depth);

    /// <summary>Whether a bird standing with its feet at <paramref name="feetHeight"/> gets its beak over the edge.</summary>
    public virtual bool CanReachFrom(float feetHeight, float bodyScale) => true;

    /// <summary>A ring that spreads across the surface; strength 1 is a beak dip, .3 a footstep.</summary>
    public void Ripple(Vector3 position, float strength)
    {
        Ripples[nextRipple] = new Vector4(position.x, position.z, Time.timeSinceLevelLoad, strength);
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

    /// <summary>A drop falling straight down, as from a lifted beak or a dripping spout.</summary>
    public void Drip(Vector3 position, float size = 1f, float lifetime = .35f)
    {
        if (droplets == null) return;
        var emit = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = new Vector3(Random.Range(-.03f, .03f), -.05f, Random.Range(-.03f, .03f)),
            startSize = Random.Range(.008f, .013f) * size,
            startLifetime = lifetime
        };
        droplets.Emit(emit, 1);
    }

    protected virtual void OnEnable()
    {
        Active.Add(this);
        RequestCameraTextures();
        if (droplets == null) droplets = CreateDroplets();
    }

    protected virtual void OnDisable()
    {
        Active.Remove(this);
        if (Active.Count > 0) return;
        System.Array.Clear(Ripples, 0, Ripples.Length);
        Shader.SetGlobalVectorArray(RipplesId, Ripples);
    }

    protected virtual void LateUpdate()
    {
        // One upload per frame serves every water surface.
        if (uploadedFrame == Time.frameCount) return;
        uploadedFrame = Time.frameCount;
        Shader.SetGlobalVectorArray(RipplesId, Ripples);
        Shader.SetGlobalFloat(PondTimeId, Time.timeSinceLevelLoad);
    }

    // The water shader reads the scene depth and colour behind the surface; make sure every
    // pipeline asset (the mobile one turns them off) provides them while water exists.
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
        var holder = new GameObject("Water Droplets");
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
