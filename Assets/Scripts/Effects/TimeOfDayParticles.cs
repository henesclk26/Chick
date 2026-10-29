using UnityEngine;

/// <summary>
/// Lets particle systems come and go with the time of day: their emission follows <see cref="amount"/> over the day
/// (0 at 07:00, 1 at 19:00, <see cref="GameTimeManager.NormalizedDayTime"/>), so the fireflies come out at dusk and the
/// mist lifts off the pond in the morning. Particles already out finish their lives, so it fades rather than snaps.
/// Without a game clock (the main menu) the amount at <see cref="withoutClock"/> is used.
/// </summary>
public sealed class TimeOfDayParticles : MonoBehaviour
{
    [SerializeField] private ParticleSystem[] systems = new ParticleSystem[0];
    [SerializeField] private AnimationCurve amount = AnimationCurve.Constant(0f, 1f, 1f);
    [SerializeField, Range(0f, 1f)] private float withoutClock = .35f;

    private float[] baseRates;
    private GameTimeManager clock;
    private float nextCheck;

    public void Configure(ParticleSystem[] particleSystems, AnimationCurve overTheDay)
    {
        systems = particleSystems;
        amount = overTheDay;
    }

    private void Awake()
    {
        baseRates = new float[systems.Length];
        for (int i = 0; i < systems.Length; i++)
            if (systems[i] != null) baseRates[i] = systems[i].emission.rateOverTimeMultiplier;
    }

    private void OnEnable()
    {
        nextCheck = 0f;
        Apply();
    }

    private void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + .5f;
        Apply();
    }

    private void Apply()
    {
        if (clock == null) clock = FindFirstObjectByType<GameTimeManager>();
        float t = clock != null ? clock.NormalizedDayTime : withoutClock;
        float a = Mathf.Max(0f, amount.Evaluate(t));
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i] == null) continue;
            var emission = systems[i].emission;
            emission.rateOverTimeMultiplier = baseRates[i] * a;
            if (a > 0f && !systems[i].isPlaying) systems[i].Play();
        }
    }
}
