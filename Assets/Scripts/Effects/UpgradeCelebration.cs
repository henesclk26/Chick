using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The bird's little celebration when it gets stronger: a passive growth stage reached (the "STAMİNA ARTTI!" banner)
/// or an upgrade bought in the journal. It flaps its wings (ChickPlayerController.TryCelebrateFlap), a burst of its own
/// down flies off it and flutters back to the ground (yellow for the chick, white for the chicken), and a ring of dust
/// puffs out under its feet. A purchase made with the journal open waits until the journal is closed and play goes
/// on, so it is seen. Presentation only; the particles are built here and reused.
/// </summary>
[DisallowMultipleComponent]
public sealed class UpgradeCelebration : MonoBehaviour
{
    [Tooltip("Opaque particle material for the down, e.g. URP Particles Unlit; the colour comes from the particles.")]
    [SerializeField] private Material downMaterial;
    [Tooltip("Soft round puff for the dust ring (the ground dust's).")]
    [SerializeField] private Material puffMaterial;
    [SerializeField] private Color chickDown = new Color(1f, .9f, .22f);
    [SerializeField] private Color chickenDown = new Color(1f, .98f, .93f);
    [SerializeField] private Color dustColor = new Color(.95f, .9f, .8f, 1f);
    [SerializeField, Range(6, 60)] private int downCount = 30;
    [SerializeField, Range(6, 40)] private int puffCount = 26;
    [SerializeField, Min(0f)] private float flapSeconds = .8f;
    [Tooltip("Optional happy cheep played with it.")]
    [SerializeField] private AudioClip sound;
    [SerializeField, Range(0f, 1f)] private float soundVolume = .7f;

    private ChickPlayerController player;
    private PlayerGrowthController growth;
    private GrowthProgressController progress;
    private PlayerUpgrades upgrades;
    private MainMenuGameplayGate gate;
    private GameTimeManager time;
    private ParticleSystem down, dust;
    private AudioSource audioSource;
    private bool pending;
    private float waitedSince;
    private const float MaxWait = 2f;
    private static Mesh feather;

    public bool IsPlaying => down != null && down.IsAlive(true);

    private void Awake()
    {
        player = GetComponent<ChickPlayerController>();
        growth = GetComponent<PlayerGrowthController>();
        Build();
    }

    private void OnEnable()
    {
        if (progress == null) progress = FindFirstObjectByType<GrowthProgressController>(FindObjectsInactive.Include);
        if (upgrades == null) upgrades = FindFirstObjectByType<PlayerUpgrades>(FindObjectsInactive.Include);
        if (gate == null) gate = FindFirstObjectByType<MainMenuGameplayGate>(FindObjectsInactive.Include);
        if (time == null) time = FindFirstObjectByType<GameTimeManager>(FindObjectsInactive.Include);
        if (progress != null) progress.OnStageReached += OnStageReached;
        if (upgrades != null) upgrades.Purchased += OnPurchased;
        pending = false;
    }

    private void OnDisable()
    {
        if (progress != null) progress.OnStageReached -= OnStageReached;
        if (upgrades != null) upgrades.Purchased -= OnPurchased;
    }

    private void OnStageReached(int stage) => Queue();
    private void OnPurchased(string id) => Queue();

    private void Queue()
    {
        pending = true;
        waitedSince = Time.time;
    }

    // A stage is reached by eating, so it waits for the bite to end (the flap needs the bird free); it does not wait
    // longer than a moment, playing without the flap if the bird keeps busy.
    private void Update()
    {
        if (!pending) return;
        bool playing = (gate == null || gate.IsReleased) && !GameplayJournalController.IsAnyOpen &&
            (time == null || (!time.IsPaused && !time.IsTransitioning));
        if (!playing) { waitedSince = Time.time; return; }
        if (player != null && !player.CanCelebrate && Time.time - waitedSince < MaxWait) return;
        pending = false;
        Play();
    }

    /// <summary>Plays the celebration at the bird's feet now.</summary>
    public void Play()
    {
        Build();
        bool chicken = growth != null && growth.CurrentForm == PlayerGrowthController.Form.Chicken;
        float scale = chicken ? 2.2f : 1f;
        if (player != null) player.TryCelebrateFlap(flapSeconds);
        Vector3 feet = transform.position;
        Burst(down, feet + Vector3.up * .1f * scale, scale, chicken ? chickenDown : chickDown);
        Burst(dust, feet + Vector3.up * .02f, scale, dustColor);
        if (sound != null && audioSource != null) audioSource.PlayOneShot(sound, soundVolume);
    }

    private static void Burst(ParticleSystem ps, Vector3 at, float scale, Color colour)
    {
        if (ps == null) return;
        ps.gameObject.SetActive(true);
        ps.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        ps.transform.localScale = Vector3.one * scale;
        var main = ps.main;
        main.startColor = new ParticleSystem.MinMaxGradient(colour * new Color(.92f, .9f, .85f, 1f), colour);
        ps.Clear(true);
        ps.Play(true);
    }

    private void Build()
    {
        if (down != null) return;
        var root = new GameObject("Upgrade Celebration").transform;
        root.SetParent(transform, false);

        // The down: little curled feathers bursting up and out, slowed by the air, then fluttering slowly down.
        down = NewSystem(root, "Down");
        var main = down.main;
        main.duration = .3f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.6f, 1.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(.022f, .034f);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = .06f;
        down.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)downCount) });
        var shape = down.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = .06f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var limit = down.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.drag = 4f;
        limit.multiplyDragByParticleSize = false;
        var noise = down.noise;
        noise.enabled = true;
        noise.strength = .15f;
        noise.frequency = 1.2f;
        var spin = down.rotationOverLifetime;
        spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-5f, 5f);
        spin.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
        spin.z = new ParticleSystem.MinMaxCurve(-5f, 5f);
        var size = down.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, .6f), new Keyframe(.1f, 1f), new Keyframe(.8f, 1f), new Keyframe(1f, 0f)));
        var r = down.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = Feather();
        r.alignment = ParticleSystemRenderSpace.Local;
        r.enableGPUInstancing = false;
        r.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
        r.sharedMaterial = downMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // A ring of dust puffing out along the ground from under the feet.
        dust = NewSystem(root, "Dust Ring");
        var dm = dust.main;
        dm.duration = .3f;
        dm.startDelay = .08f;
        dm.startLifetime = new ParticleSystem.MinMaxCurve(.5f, .8f);
        dm.startSpeed = new ParticleSystem.MinMaxCurve(.45f, .75f);
        dm.startSize = new ParticleSystem.MinMaxCurve(.04f, .07f);
        dm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        dust.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)puffCount) });
        var ds = dust.shape;
        ds.shapeType = ParticleSystemShapeType.Circle;
        ds.radius = .06f;
        ds.radiusThickness = 0f;
        ds.rotation = new Vector3(90f, 0f, 0f);
        var dl = dust.limitVelocityOverLifetime;
        dl.enabled = true;
        dl.drag = 3f;
        dl.multiplyDragByParticleSize = false;
        var dsize = dust.sizeOverLifetime;
        dsize.enabled = true;
        dsize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, .5f), new Keyframe(.3f, 1f), new Keyframe(1f, 1.4f)));
        var dc = dust.colorOverLifetime;
        dc.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.9f, .08f), new GradientAlphaKey(.7f, .5f), new GradientAlphaKey(0f, 1f) });
        dc.color = fade;
        var dr = dust.GetComponent<ParticleSystemRenderer>();
        dr.renderMode = ParticleSystemRenderMode.Billboard;
        dr.sharedMaterial = puffMaterial;
        dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dr.receiveShadows = false;

        if (sound != null)
        {
            audioSource = root.gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
        }
    }

    private static ParticleSystem NewSystem(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;   // the transform's scale sizes the whole burst
        main.maxParticles = 64;
        main.stopAction = ParticleSystemStopAction.Disable;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        go.SetActive(false);
        return ps;
    }

    // A small curled feather, 1 unit long along +Z: a soft rounded vane either side of a thin quill, its tip curling up.
    private static Mesh Feather()
    {
        if (feather != null) return feather;
        var v = new List<Vector3>();
        var t = new List<int>();
        const int n = 8;
        for (int i = 0; i <= n; i++)
        {
            float u = i / (float)n;
            float w = Mathf.Sin(Mathf.PI * Mathf.Pow(u, .75f)) * .26f;
            float y = .12f * u * u;
            v.Add(new Vector3(-w, y + w * .15f, u - .5f));
            v.Add(new Vector3(0f, y, u - .5f));
            v.Add(new Vector3(w, y + w * .15f, u - .5f));
        }
        for (int i = 0; i < n; i++)
        {
            int a = i * 3, b = a + 3;
            t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1, a + 1, b + 1, a + 2, a + 2, b + 1, b + 2 });
        }
        feather = new Mesh { name = "Down Feather" };
        feather.SetVertices(v);
        feather.SetTriangles(t, 0);
        feather.RecalculateNormals();
        return feather;
    }

#if UNITY_EDITOR
    public void Configure(Material downMat, Material puffMat)
    {
        downMaterial = downMat;
        puffMaterial = puffMat;
    }
#endif
}
