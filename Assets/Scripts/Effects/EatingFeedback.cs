using System.Collections;
using UnityEngine;

/// <summary>
/// What a bite feels like: crumbs in the food's colour fly from the beak at every bite; when the bite finishes the food,
/// the food shrinks away at the beak with a little twist instead of vanishing on the spot and the bird gives a happy
/// squash and stretch. Optional sounds for a bite and for a finished food (left empty, nothing plays).
/// Presentation only: listens to <see cref="ChickEatingController.Bit"/> and touches neither eating nor movement.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(130)]
[RequireComponent(typeof(ChickEatingController), typeof(CharacterController))]
public sealed class EatingFeedback : MonoBehaviour
{
    [Tooltip("Small round crumb (Farm/Mist).")]
    [SerializeField] private Material crumbMaterial;
    [SerializeField, Range(0, 16)] private int crumbsPerBite = 6;
    [SerializeField, Min(.05f)] private float shrinkSeconds = .16f;
    [Tooltip("How far the bird stretches up in its happy squash (share of its height).")]
    [SerializeField, Range(0f, .3f)] private float squash = .13f;
    [SerializeField, Min(.05f)] private float squashSeconds = .3f;
    [SerializeField] private AudioClip biteSound;
    [SerializeField] private AudioClip finishSound;
    [SerializeField, Range(0f, 1f)] private float volume = .7f;

    private const float ChickHeight = .22f;
    private static readonly Color DefaultCrumb = new Color(.86f, .74f, .5f);
    private ChickEatingController eater;
    private ChickPlayerController player;
    private CharacterController body;
    private ParticleSystem crumbs;
    private AudioSource voice;
    private Transform squashBone;
    private Vector3 squashBase;
    private int squashAxis = 1;   // the bone's local axis that points up
    private float squashStart = -1f;

    private float Scale => Mathf.Sqrt(body.height / ChickHeight);

    public void Configure(Material crumb) { crumbMaterial = crumb; }

    private void Awake()
    {
        eater = GetComponent<ChickEatingController>();
        player = GetComponent<ChickPlayerController>();
        body = GetComponent<CharacterController>();
        crumbs = Build("Eat Crumbs", crumbMaterial, 96, 2.2f);
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false; voice.spatialBlend = 0f; voice.volume = volume;
        gameObject.AddComponent<MenuAudioChannel>().SetBus(MenuAudioChannel.Bus.Sfx);
    }

    private void OnEnable() { if (eater != null) eater.Bit += OnBite; }

    private void OnDisable()
    {
        if (eater != null) eater.Bit -= OnBite;
        EndSquash();
    }

    private void OnDestroy()
    {
        if (crumbs != null) Destroy(crumbs.gameObject);
    }

    private ParticleSystem Build(string name, Material material, int max, float gravity)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false; main.playOnAwake = false; main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.7f, .9f), new Keyframe(1f, 0f)));
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        ps.Play();
        return ps;
    }

    private void OnBite(EdibleObject food, bool finished)
    {
        if (food == null) return;
        float scale = Scale;
        Vector3 at = food.BitePosition;
        Color colour = CrumbColour(food);
        // Crumbs: thrown up and out from the bite, falling back to the ground.
        var emit = new ParticleSystem.EmitParams();
        int count = finished ? crumbsPerBite + 2 : crumbsPerBite;
        for (int i = 0; i < count; i++)
        {
            Vector2 side = Random.insideUnitCircle.normalized * Random.Range(.25f, 1f);
            emit.position = at + Vector3.up * .01f * scale;
            emit.velocity = new Vector3(side.x * .45f, Random.Range(.55f, 1.05f), side.y * .45f) * scale;
            emit.startSize = Random.Range(.011f, .02f) * scale;
            emit.startLifetime = Random.Range(.35f, .6f);
            emit.startColor = Color.Lerp(colour, colour * .75f, Random.value) * new Color(1f, 1f, 1f, 1f);
            crumbs.Emit(emit, 1);
        }
        if (!finished)
        {
            Play(biteSound, 1f);
            return;
        }
        // Finished: the food shrinking away, the bird's happy squash.
        StartCoroutine(ShrinkAway(food));
        BeginSquash();
        Play(finishSound != null ? finishSound : biteSound, finishSound != null ? 1f : 1.25f);
    }

    private static Color CrumbColour(EdibleObject food)
    {
        if (FoodStatisticIdentity.ResolveBadge(food.StatisticKey) != "YE") return FoodStatisticIdentity.ResolveColor(food.StatisticKey);
        var renderer = food.GetComponentInChildren<Renderer>(true);
        if (renderer != null && renderer.sharedMaterial != null)
        {
            Material m = renderer.sharedMaterial;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.color;
        }
        return DefaultCrumb;
    }

    private void Play(AudioClip clip, float pitch)
    {
        if (clip == null) return;
        voice.pitch = pitch * Random.Range(.92f, 1.08f);
        voice.PlayOneShot(clip, volume);
    }

    // A copy of the food's meshes where they are at the bite (a seed has been drawn up to the beak), shrinking to
    // nothing with a little turn, then gone: the food is swallowed rather than switched off.
    private IEnumerator ShrinkAway(EdibleObject food)
    {
        var meshes = food.GetComponentsInChildren<MeshRenderer>(true);
        if (meshes.Length == 0) yield break;
        Vector3 pivot = Vector3.zero;
        foreach (MeshRenderer r in meshes) pivot += r.transform.position;
        pivot /= meshes.Length;
        var ghost = new GameObject("Eaten " + food.name);
        ghost.transform.position = pivot;
        int parts = 0;
        foreach (MeshRenderer r in meshes)
        {
            if (!r.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
            var part = new GameObject(r.name);
            part.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
            part.transform.localScale = r.transform.lossyScale;
            part.transform.SetParent(ghost.transform, true);
            part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var copy = part.AddComponent<MeshRenderer>();
            copy.sharedMaterials = r.sharedMaterials;
            copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (++parts >= 6) break;
        }
        if (parts == 0) { Destroy(ghost); yield break; }
        float spin = Random.value < .5f ? -1f : 1f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / shrinkSeconds)
        {
            float s = 1f - t * t;
            float pop = 1f + Mathf.Sin(t * Mathf.PI) * .05f;
            ghost.transform.localScale = Vector3.one * s * pop;
            ghost.transform.rotation = Quaternion.Euler(0f, spin * 90f * t, 0f);
            yield return null;
        }
        Destroy(ghost);
    }

    // The happy squash: the skeleton's root bone stretched up and squashed down once, round the feet, after the
    // animation has posed it each frame (so the animation itself is untouched).
    private void BeginSquash()
    {
        EndSquash();
        Animator animator = player != null ? player.ActiveAnimator : null;
        var skin = animator != null ? animator.GetComponentInChildren<SkinnedMeshRenderer>() : GetComponentInChildren<SkinnedMeshRenderer>();
        if (skin == null || skin.rootBone == null) return;
        squashBone = skin.rootBone;
        squashBase = squashBone.localScale;
        Vector3 up = squashBone.InverseTransformDirection(Vector3.up);
        squashAxis = Mathf.Abs(up.x) > Mathf.Abs(up.y) ? (Mathf.Abs(up.x) > Mathf.Abs(up.z) ? 0 : 2) : (Mathf.Abs(up.y) > Mathf.Abs(up.z) ? 1 : 2);
        squashStart = Time.time;
    }

    private void EndSquash()
    {
        if (squashBone != null) squashBone.localScale = squashBase;
        squashBone = null;
        squashStart = -1f;
    }

    private void LateUpdate()
    {
        if (squashBone == null) return;
        float t = (Time.time - squashStart) / squashSeconds;
        if (t >= 1f) { EndSquash(); return; }
        // up (stretch), down (squash), settle
        float up = t < .3f ? Mathf.Sin(t / .3f * Mathf.PI * .5f) : t < .65f ? Mathf.Cos((t - .3f) / .35f * Mathf.PI) : -Mathf.Sin((1f - t) / .35f * Mathf.PI) * .4f;
        float y = 1f + up * squash, xz = 1f / Mathf.Sqrt(Mathf.Max(.5f, y));
        var factor = new Vector3(xz, xz, xz);
        factor[squashAxis] = y;
        squashBone.localScale = Vector3.Scale(squashBase, factor);
    }
}
