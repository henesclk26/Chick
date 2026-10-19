using UnityEngine;

/// <summary>
/// Small touches that make the ground feel like ground, read from the terrain paint under the bird:
/// on earth and paths a puff of dust and a few kicked-up stone chips at every step (more when running), on sand
/// a paler dust, on the pond's damp bank dark mud drops, on grass a few green leaf bits; a burst when landing from
/// a jump or glide, and a small spray where the beak strikes the ground. Pollen and seed fluff drift in the air
/// round the bird. Presentation only: nothing here touches movement or eating.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(120)]
[RequireComponent(typeof(ChickPlayerController), typeof(CharacterController))]
public sealed class GroundEffects : MonoBehaviour
{
    [Tooltip("Soft round puff (URP Particles Unlit, alpha blended).")]
    [SerializeField] private Material puffMaterial;
    [Tooltip("Small irregular chip for stones, leaf bits and mud drops.")]
    [SerializeField] private Material chipMaterial;
    [Tooltip("Tiny soft dot for drifting pollen and seed fluff.")]
    [SerializeField] private Material pollenMaterial;
    [SerializeField, Min(.03f), Tooltip("Metres walked between footfalls at chick size (scaled with the body).")]
    private float stepDistance = .13f;
    [SerializeField, Range(0f, 30f), Tooltip("Pollen and seed fluff born per second round the bird.")]
    private float pollenRate = 7f;

    private enum Surface { Grass, Earth, Sand, Mud }

    private const float ChickHeight = .22f;
    private static readonly Color EarthDust = new Color(.70f, .56f, .40f, .5f);
    private static readonly Color SandDust = new Color(.87f, .79f, .63f, .45f);
    private static readonly Color GrassDust = new Color(.75f, .74f, .55f, .18f);
    private static readonly Color[] Stones = { new Color(.58f, .47f, .35f), new Color(.5f, .48f, .45f), new Color(.66f, .58f, .46f) };
    private static readonly Color[] Leaves = { new Color(.42f, .66f, .24f), new Color(.58f, .78f, .32f), new Color(.66f, .72f, .36f) };
    private static readonly Color[] Muds = { new Color(.25f, .18f, .12f), new Color(.33f, .24f, .16f) };
    private static readonly Color[] SandGrains = { new Color(.80f, .71f, .54f), new Color(.72f, .64f, .5f) };

    private ChickPlayerController player;
    private CharacterController body;
    private ChickEatingController eater;
    private ChickWaterWading wading;
    private ParticleSystem dust, chips, pollen;
    private Vector3 lastStep;
    private bool wasGrounded = true;
    private float airTime;

    private float Scale => body.height / ChickHeight;

    private void Awake()
    {
        player = GetComponent<ChickPlayerController>();
        body = GetComponent<CharacterController>();
        eater = GetComponent<ChickEatingController>();
        wading = GetComponent<ChickWaterWading>();
        dust = Build("Ground Dust", puffMaterial, 160, 0f, true);
        chips = Build("Ground Chips", chipMaterial, 160, 1.4f, false);
        pollen = BuildPollen();
        lastStep = transform.position;
    }

    private void OnEnable()
    {
        if (eater != null) eater.Pecked += OnPeck;
    }

    private void OnDisable()
    {
        if (eater != null) eater.Pecked -= OnPeck;
    }

    private void OnDestroy()
    {
        foreach (var ps in new[] { dust, chips, pollen })
            if (ps != null) Destroy(ps.gameObject);
    }

    private void Update()
    {
        bool grounded = body.isGrounded;
        Vector3 position = transform.position;
        float scale = Scale;
        if (!grounded) airTime += Time.deltaTime;
        else
        {
            if (!wasGrounded && airTime > .18f) Landing(position, scale, Mathf.Clamp01(airTime / .8f));
            airTime = 0f;
            Vector3 walked = position - lastStep; walked.y = 0f;
            float stride = stepDistance * scale * (player.IsRunning ? 1.25f : 1f);
            if (walked.sqrMagnitude > stride * stride)
            {
                Step(position, scale, player.IsRunning);
                lastStep = position;
            }
        }
        if (!grounded) lastStep = position;
        wasGrounded = grounded;

        // the pollen cloud follows the bird (its particles stay in the world)
        pollen.transform.position = position + Vector3.up * (.9f * Mathf.Sqrt(scale));
        var emission = pollen.emission;
        emission.rateOverTime = pollenRate;
    }

    private void Step(Vector3 at, float scale, bool running)
    {
        if (InWater) return;
        var surface = SurfaceAt(at);
        int boost = running ? 2 : 1;
        switch (surface)
        {
            case Surface.Earth:
                Puffs(at, scale, EarthDust, boost, .35f);
                if (Random.value < (running ? .8f : .4f)) Chips(at, scale, Stones, boost, .45f);
                break;
            case Surface.Sand:
                Puffs(at, scale, SandDust, boost + 1, .3f);
                if (Random.value < .35f) Chips(at, scale, SandGrains, 1, .35f);
                break;
            case Surface.Mud:
                Chips(at, scale, Muds, boost + 1, .5f, .8f);
                break;
            default:
                if (Random.value < (running ? .9f : .5f)) Chips(at, scale, Leaves, boost, .4f, 1.25f);
                if (running && Random.value < .3f) Puffs(at, scale, GrassDust, 1, .25f);
                break;
        }
    }

    private void Landing(Vector3 at, float scale, float strength)
    {
        if (InWater) return;
        var surface = SurfaceAt(at);
        int ring = Mathf.RoundToInt(Mathf.Lerp(5, 11, strength));
        switch (surface)
        {
            case Surface.Earth: Ring(at, scale, EarthDust, ring); Chips(at, scale, Stones, ring / 2 + 1, .6f); break;
            case Surface.Sand: Ring(at, scale, SandDust, ring + 2); Chips(at, scale, SandGrains, ring / 3 + 1, .5f); break;
            case Surface.Mud: Chips(at, scale, Muds, ring, .7f, .9f); break;
            default: Chips(at, scale, Leaves, ring / 2 + 2, .55f, 1.25f); Ring(at, scale, GrassDust, ring / 2); break;
        }
    }

    private void OnPeck(Vector3 point)
    {
        if (InWater) return;
        float scale = Scale;
        var surface = SurfaceAt(point);
        switch (surface)
        {
            case Surface.Earth: Puffs(point, scale * .8f, EarthDust, 2, .25f); Chips(point, scale, Stones, 3, .5f); break;
            case Surface.Sand: Puffs(point, scale * .8f, SandDust, 3, .25f); Chips(point, scale, SandGrains, 2, .4f); break;
            case Surface.Mud: Chips(point, scale, Muds, 4, .45f, .8f); break;
            default: Chips(point, scale, Leaves, 3, .4f, 1.25f); break;
        }
    }

    private bool InWater => wading != null && wading.InWater;

    // ---- emitting ------------------------------------------------------------------------------------------------

    private void Puffs(Vector3 at, float scale, Color color, int count, float speed)
    {
        var p = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            Vector2 d = Random.insideUnitCircle;
            p.position = at + new Vector3(d.x, .1f, d.y) * .05f * scale;
            p.velocity = new Vector3(d.x * speed, Random.Range(.05f, .25f), d.y * speed) * scale;
            p.startSize = Random.Range(.06f, .12f) * scale;
            p.startLifetime = Random.Range(.55f, .9f);
            p.startColor = color;
            p.rotation = Random.Range(0f, 360f);
            dust.Emit(p, 1);
        }
    }

    private void Ring(Vector3 at, float scale, Color color, int count)
    {
        var p = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            float a = (i + Random.value * .5f) / count * Mathf.PI * 2f;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            p.position = at + d * .04f * scale + Vector3.up * .02f * scale;
            p.velocity = (d * Random.Range(.45f, .75f) + Vector3.up * Random.Range(.05f, .15f)) * scale;
            p.startSize = Random.Range(.07f, .13f) * scale;
            p.startLifetime = Random.Range(.6f, .95f);
            p.startColor = color;
            p.rotation = Random.Range(0f, 360f);
            dust.Emit(p, 1);
        }
    }

    private void Chips(Vector3 at, float scale, Color[] palette, int count, float speed, float sizeScale = 1f)
    {
        var p = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            Vector2 d = Random.insideUnitCircle.normalized * Random.Range(.4f, 1f);
            p.position = at + new Vector3(d.x, .4f, d.y) * .03f * scale;
            p.velocity = new Vector3(d.x * speed, Random.Range(.6f, 1.1f) * speed * 2.2f, d.y * speed) * scale;
            p.startSize = Random.Range(.012f, .024f) * scale * sizeScale;
            p.startLifetime = Random.Range(.5f, .8f);
            p.startColor = palette[Random.Range(0, palette.Length)];
            p.rotation = Random.Range(0f, 360f);
            p.angularVelocity = Random.Range(-540f, 540f);
            chips.Emit(p, 1);
        }
    }

    // ---- surface ----------------------------------------------------------------------------------------------

    // What the bird stands on, from the farm terrain's paint: TroddenEarth 4, OldEarthAccent 5, Pebbles 6,
    // TilledSoil 8, RichGreen_Earth 13 and StonyPath 19 are earth; RichGreen_Sand 14 is sand; RichGreen_DampEarth 15
    // is mud; everything else is grass. Terrains with fewer layers count only the layers they have.
    private static Surface SurfaceAt(Vector3 position)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) return Surface.Grass;
        var data = terrain.terrainData;
        Vector3 local = position - terrain.transform.position;
        int res = data.alphamapResolution;
        int x = Mathf.Clamp(Mathf.FloorToInt(local.x / data.size.x * res), 0, res - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt(local.z / data.size.z * res), 0, res - 1);
        var w = data.GetAlphamaps(x, z, 1, 1);
        int layers = w.GetLength(2);
        float Weight(int l) => l < layers ? w[0, 0, l] : 0f;
        float earth = Weight(4) + Weight(5) + Weight(6) + Weight(8) + Weight(13) + Weight(19);
        float sand = Weight(14), mud = Weight(15);
        float grass = 1f - earth - sand - mud;
        if (mud >= earth && mud >= sand && mud >= grass) return Surface.Mud;
        if (earth >= sand && earth >= grass) return Surface.Earth;
        return sand >= grass ? Surface.Sand : Surface.Grass;
    }

    // ---- building -------------------------------------------------------------------------------------------------

    private ParticleSystem Build(string name, Material material, int max, float gravity, bool soft)
    {
        var go = new GameObject(name);
        go.transform.SetParent(null, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;
        main.gravityModifier = gravity;
        main.startRotation3D = false;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = false;

        var color = ps.colorOverLifetime; color.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  soft ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .15f), new GradientAlphaKey(.6f, .5f), new GradientAlphaKey(0f, 1f) }
                       : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, .75f), new GradientAlphaKey(0f, 1f) });
        color.color = g;
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = soft ? new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .55f, 1f, 1.6f))
                         : new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, .7f));
        var limit = ps.limitVelocityOverLifetime; limit.enabled = true;
        limit.limit = soft ? .05f : 4f; limit.dampen = soft ? .25f : .02f;
        if (!soft)
        {
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Low;
            collision.bounce = .25f; collision.dampen = .55f; collision.lifetimeLoss = .15f;
            collision.radiusScale = .5f;
            collision.collidesWith = ~LayerMask.GetMask("CameraBlocker", "Edible");
        }
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.minParticleSize = 0f;
        return ps;
    }

    private ParticleSystem BuildPollen()
    {
        var go = new GameObject("Drifting Pollen");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 90;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, .06f);
        main.startSize = new ParticleSystem.MinMaxCurve(.012f, .032f);
        var startColor = new Gradient();
        startColor.SetKeys(new[] { new GradientColorKey(new Color(1f, .95f, .72f), 0f), new GradientColorKey(new Color(1f, 1f, .97f), 1f) },
                           new[] { new GradientAlphaKey(.85f, 0f), new GradientAlphaKey(.85f, 1f) });
        main.startColor = new ParticleSystem.MinMaxGradient(startColor) { mode = ParticleSystemGradientMode.RandomColor };
        main.gravityModifier = -.004f;
        var emission = ps.emission; emission.rateOverTime = pollenRate;
        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(9f, 1.6f, 9f);
        var noise = ps.noise; noise.enabled = true; noise.strength = .18f; noise.frequency = .35f; noise.scrollSpeed = .15f;
        var velocity = ps.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(.03f, .09f); velocity.y = new ParticleSystem.MinMaxCurve(-.01f, .02f); velocity.z = new ParticleSystem.MinMaxCurve(-.02f, .04f);
        var color = ps.colorOverLifetime; color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .2f), new GradientAlphaKey(1f, .8f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = pollenMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.minParticleSize = 0f;
        ps.Play();
        return ps;
    }
}
