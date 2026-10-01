using UnityEngine;

/// <summary>
/// "Çift Zıplama" cue: a soft ring of cream air puffs bursts out under the feet as the wings push off,
/// then drifts, swells and fades. Presentation only; the player controller decides when to play it.
/// </summary>
[DisallowMultipleComponent]
public sealed class AirJumpPuff : MonoBehaviour
{
    [Tooltip("Soft round particle material, e.g. URP Particles Unlit.")]
    [SerializeField] private Material puffMaterial;
    [SerializeField] private Color puffColor = new Color(1f, .97f, .9f, 1f);
    [SerializeField, Range(6, 32)] private int puffCount = 20;

    private ParticleSystem system;

    public bool IsPlaying => system != null && system.IsAlive(true);

    private void Awake() => Build();

    private void Build()
    {
        if (system != null) return;
        system = gameObject.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = system.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = .5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.32f, .5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.55f, .95f);
        main.startSize = new ParticleSystem.MinMaxCurve(.09f, .15f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = puffColor;
        main.gravityModifier = .06f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy; // transform scale sizes the whole burst
        main.maxParticles = 64;
        main.stopAction = ParticleSystemStopAction.Disable;

        var emission = system.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)puffCount) });

        // A flat circle under the feet: particles fly outward along the ground plane.
        var shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = .05f;
        shape.radiusThickness = 1f;
        shape.rotation = new Vector3(90f, 0f, 0f);

        // Pushed slightly down, as if the wings shoved the air away.
        var velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        // All three axes must share one curve mode.
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(-.35f, -.15f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var limit = system.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.drag = 3.5f;
        limit.multiplyDragByParticleSize = false;

        var sizeOverLife = system.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, .45f), new Keyframe(.25f, 1f), new Keyframe(1f, 1.35f)));

        var colorOverLife = system.colorOverLifetime;
        colorOverLife.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .06f), new GradientAlphaKey(.9f, .5f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = gradient;

        var rotation = system.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);

        var render = GetComponent<ParticleSystemRenderer>();
        render.renderMode = ParticleSystemRenderMode.Billboard;
        render.sharedMaterial = puffMaterial;
        render.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        render.receiveShadows = false;

        gameObject.SetActive(false);
    }

    /// <summary>Starts the burst at the feet; scale 1 fits the chick, larger values the chicken.</summary>
    public void Play(Vector3 feet, float scale)
    {
        Build();
        transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        transform.localScale = Vector3.one * Mathf.Max(.1f, scale);
        gameObject.SetActive(true);
        system.Clear(true);
        system.Play(true);
    }
}
