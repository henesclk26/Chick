using System;
using UnityEngine;

// A caterpillar that comes out of an apple left lying on the ground (FruitTree): it pushes out of the apple head
// first, turns round and nibbles at it, the apple shrinking bite by bite, until it is gone; then it crawls off and
// disappears. If the chick eats the apple first, it crawls off at once. It crawls like an inchworm: its body
// stretches and bunches up as it goes. The model faces +Z with its origin under its belly.
[DisallowMultipleComponent]
public sealed class AppleCaterpillar : MonoBehaviour
{
    [SerializeField, Tooltip("The model; stretches, bunches and nods. Defaults to the first child.")]
    private Transform visual;
    [SerializeField] private float emergeSeconds = 2.4f;
    [SerializeField] private float biteEvery = 1.2f;
    [SerializeField, Range(.5f, .99f), Tooltip("Apple size kept after each bite.")]
    private float keptPerBite = .9f;
    [SerializeField, Range(.05f, .6f), Tooltip("The apple is gone once it is this small.")]
    private float goneBelow = .3f;
    [SerializeField, Tooltip("Crawling speed (m/s).")]
    private float crawlSpeed = .03f;
    [SerializeField] private float leaveSeconds = 5f;

    private enum Phase { Emerge, Turn, Eat, Leave, Vanish }

    private Phase phase;
    private EdibleObject apple;
    private Transform appleBody;                  // the apple's pickup, centred on it: shrinks round its middle
    private Vector3 appleScale, appleRest, restScale, outPoint, inPoint;
    private float appleRadius, t, nextBite, gait;
    private Action onEaten;
    private Quaternion turnFrom, turnTo;

    public void Begin(EdibleObject food, float radius, Action eaten)
    {
        apple = food;
        appleBody = food.transform;
        appleScale = appleBody.localScale;
        appleRest = appleBody.position;
        appleRadius = radius;
        onEaten = eaten;
        if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
        restScale = visual != null ? visual.localScale : Vector3.one;

        // Out of the apple's middle, head first, towards a random side, onto the ground beside it.
        Vector3 centre = food.transform.position;
        Vector3 dir = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
        float ground = centre.y - radius * .9f;
        inPoint = new Vector3(centre.x, ground, centre.z);
        outPoint = inPoint + dir * (radius + .03f);
        transform.SetPositionAndRotation(inPoint, Quaternion.LookRotation(dir));
        transform.localScale = Vector3.one * .4f;
        phase = Phase.Emerge;
        t = 0f;
    }

    /// <summary>The apple is gone (eaten by the chick): crawl off now.</summary>
    public void Leave()
    {
        if (phase == Phase.Leave || phase == Phase.Vanish) return;
        StartLeaving();
    }

    private void StartLeaving()
    {
        phase = Phase.Leave;
        t = 0f;
        transform.localScale = Vector3.one;
        turnFrom = transform.rotation;
        turnTo = Quaternion.Euler(0f, transform.eulerAngles.y + 180f + UnityEngine.Random.Range(-40f, 40f), 0f);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        bool appleGone = apple == null || apple.IsConsumed || !apple.gameObject.activeInHierarchy;
        if (appleGone && phase < Phase.Leave) StartLeaving();
        t += dt;

        switch (phase)
        {
            case Phase.Emerge:
            {
                float f = Mathf.Clamp01(t / emergeSeconds);
                transform.position = Vector3.Lerp(inPoint, outPoint, Mathf.SmoothStep(0f, 1f, f));
                transform.localScale = Vector3.one * Mathf.Lerp(.4f, 1f, f);
                Crawl(dt, 1f);
                if (f >= 1f)
                {
                    phase = Phase.Turn;
                    t = 0f;
                    turnFrom = transform.rotation;
                    Vector3 toApple = apple.transform.position - transform.position;
                    toApple.y = 0f;
                    turnTo = Quaternion.LookRotation(toApple.sqrMagnitude > 1e-6f ? toApple : -transform.forward);
                }
                break;
            }
            case Phase.Turn:
            {
                float f = Mathf.Clamp01(t / .9f);
                transform.rotation = Quaternion.Slerp(turnFrom, turnTo, Mathf.SmoothStep(0f, 1f, f));
                Crawl(dt, .5f);
                if (f >= 1f) { phase = Phase.Eat; t = 0f; nextBite = biteEvery * .5f; }
                break;
            }
            case Phase.Eat:
            {
                // Nodding into the apple; each bite takes a little off it.
                if (visual != null)
                {
                    visual.localScale = restScale;
                    visual.localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 2f / biteEvery)) * 18f, 0f, 0f);
                }
                if (t >= nextBite)
                {
                    nextBite += biteEvery;
                    // Smaller round its middle, still resting on the ground.
                    appleBody.localScale *= keptPerBite;
                    float kept = appleBody.localScale.x / appleScale.x;
                    appleBody.position = appleRest - Vector3.up * appleRadius * .9f * (1f - kept);
                    if (kept < goneBelow)
                    {
                        onEaten?.Invoke();
                        if (visual != null) visual.localRotation = Quaternion.identity;
                        StartLeaving();
                    }
                }
                break;
            }
            case Phase.Leave:
            {
                transform.rotation = Quaternion.Slerp(turnFrom, turnTo, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / .9f)));
                if (t > .9f) transform.position += transform.forward * Crawl(dt, 1f) * dt;
                if (t >= leaveSeconds) { phase = Phase.Vanish; t = 0f; }
                break;
            }
            case Phase.Vanish:
            {
                // Into the grass.
                transform.localScale = Vector3.one * Mathf.Max(0f, 1f - t / .8f);
                if (t >= .8f) Destroy(gameObject);
                break;
            }
        }
    }

    // Inchworm gait: the body stretches (moving forward) and bunches up (resting). Returns the speed this moment.
    private float Crawl(float dt, float pace)
    {
        gait += dt * pace * 5f;
        float s = Mathf.Sin(gait);
        if (visual != null)
        {
            visual.localScale = new Vector3(restScale.x * (1f - s * .05f), restScale.y * (1f - s * .08f), restScale.z * (1f + s * .14f));
            visual.localRotation = Quaternion.identity;
        }
        return crawlSpeed * Mathf.Max(0f, s) * 2f;
    }
}
