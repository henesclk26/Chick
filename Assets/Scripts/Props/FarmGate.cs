using System;
using UnityEngine;

// A farm gate that swings open by itself when a walker (the chick, a helper chick: anything moved by a
// CharacterController) comes near, and swings shut a little after everyone has gone. The leaves always open away from
// whoever arrived first, so they never sweep into them, and stay on that side until the gate has closed again.
// Each leaf turns about its own hinge (its transform's pivot; closed at its rotation in the scene). A locked gate stays
// shut, like the farm's road gate, which keeps the chick inside the farm.
[DisallowMultipleComponent]
public sealed class FarmGate : MonoBehaviour
{
    [Serializable]
    public struct Leaf
    {
        [Tooltip("The leaf, pivoting on its hinge about its local Y axis.")]
        public Transform hinge;
        [Tooltip("+1 if turning the hinge by a positive angle swings the leaf towards the gate's +Z side, else -1.")]
        public float towardsPositiveZ;
    }

    [SerializeField] private Leaf[] leaves = Array.Empty<Leaf>();
    [SerializeField, Tooltip("Keeps the gate shut.")]
    private bool locked;
    [SerializeField, Tooltip("Stays shut while the player is a chick and opens once it has grown into a chicken.")]
    private bool chickenOnly;
    [SerializeField, Range(20f, 120f), Tooltip("How far the leaves open, in degrees.")]
    private float openAngle = 100f;
    [SerializeField, Min(.5f), Tooltip("A walker this close to the gate's centre (m, along the ground) opens it. Larger than " +
                                        "a leaf, so a leaf never closes onto someone.")]
    private float reach = 2.5f;
    [SerializeField, Min(10f), Tooltip("Swing speed, degrees per second.")]
    private float swingSpeed = 120f;
    [SerializeField, Min(0f), Tooltip("Seconds the gate stays open after the last walker has left.")]
    private float closeDelay = 1.5f;

    private const float ScanInterval = .1f, WalkerRefreshInterval = 1f;
    // Every walker in the scene, shared by all gates and looked up again now and then (helpers come and go).
    private static CharacterController[] walkers = Array.Empty<CharacterController>();
    private static float walkersFoundAt = float.NegativeInfinity;
    private Quaternion[] closed;
    private float angle;          // 0 closed .. openAngle open
    private float side;           // +1 opening towards +Z, -1 towards -Z
    private float scanTimer;
    private float emptyTimer;
    private bool someoneNear;
    private bool shut;
    private static PlayerGrowthController playerGrowth;

    // With domain reload off (Enter Play Mode Options) these would carry the last session's walkers and lookup time
    // into the next, and the gates would not see anyone until the clock caught up.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetWalkers()
    {
        walkers = Array.Empty<CharacterController>();
        walkersFoundAt = float.NegativeInfinity;
        playerGrowth = null;
    }

    public bool Locked
    {
        get => locked;
        set => locked = value;
    }

    public bool IsOpen => angle > 1f;

    private void Awake()
    {
        closed = new Quaternion[leaves.Length];
        for (int i = 0; i < leaves.Length; i++)
            if (leaves[i].hinge != null) closed[i] = leaves[i].hinge.localRotation;
    }

    private void Update()
    {
        scanTimer -= Time.deltaTime;
        if (scanTimer <= 0f)
        {
            scanTimer = ScanInterval;
            Scan();
        }
        emptyTimer = someoneNear ? 0f : emptyTimer + Time.deltaTime;
        bool open = !shut && (someoneNear || emptyTimer < closeDelay) && side != 0f;
        float target = open ? openAngle : 0f;
        if (Mathf.Approximately(angle, target)) return;
        angle = Mathf.MoveTowards(angle, target, swingSpeed * Time.deltaTime);
        if (angle <= 0f && !someoneNear) side = 0f;
        Pose();
    }

    private void Scan()
    {
        someoneNear = false;
        if (chickenOnly && playerGrowth == null) playerGrowth = FindFirstObjectByType<PlayerGrowthController>();
        shut = locked || (chickenOnly && playerGrowth != null && playerGrowth.CurrentForm == PlayerGrowthController.Form.Chick);
        if (shut) return;
        if (Time.unscaledTime - walkersFoundAt > WalkerRefreshInterval)
        {
            walkers = FindObjectsByType<CharacterController>(FindObjectsSortMode.None);
            walkersFoundAt = Time.unscaledTime;
        }
        float nearest = float.MaxValue;
        Vector3 arrival = Vector3.zero;
        foreach (var walker in walkers)
        {
            if (walker == null || !walker.enabled || !walker.gameObject.activeInHierarchy) continue;
            Vector3 offset = walker.transform.position - transform.position;
            if (Mathf.Abs(offset.y) > 2f) continue;
            offset.y = 0f;
            if (offset.sqrMagnitude > reach * reach) continue;
            someoneNear = true;
            if (offset.sqrMagnitude < nearest)
            {
                nearest = offset.sqrMagnitude;
                arrival = offset;
            }
        }
        // Pick the side only while shut: away from the walker, so the leaves swing ahead of them.
        if (someoneNear && angle <= 0f)
            side = Vector3.Dot(arrival, transform.forward) > 0f ? -1f : 1f;
    }

    private void Pose()
    {
        float eased = Mathf.SmoothStep(0f, 1f, angle / openAngle) * openAngle;
        for (int i = 0; i < leaves.Length; i++)
        {
            if (leaves[i].hinge == null) continue;
            leaves[i].hinge.localRotation = closed[i] * Quaternion.Euler(0f, eased * side * leaves[i].towardsPositiveZ, 0f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = locked ? Color.red : chickenOnly ? Color.yellow : Color.green;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * .5f, reach);
    }
}
