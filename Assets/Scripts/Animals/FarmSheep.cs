using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A sheep (or lamb) inside a <see cref="SheepPen"/>: idles, grazes and ambles to new spots, lambs keep
/// near their mother, and a running chick that comes close makes it bolt away. A startled sheep
/// spooks its neighbours a moment later, so the flock scatters together.
/// Drives the Animator states Idle / Walk / Graze / Run and scales their playback to the ground speed.
/// </summary>
[DisallowMultipleComponent]
public sealed class FarmSheep : MonoBehaviour
{
    [SerializeField] private SheepPen pen;
    [Tooltip("Lambs stay close to this sheep.")]
    [SerializeField] private FarmSheep mother;
    [SerializeField] private Animator animator;

    [Header("Movement")]
    [SerializeField, Min(.1f)] private float walkSpeed = .55f;
    [SerializeField, Min(.1f)] private float runSpeed = 2.3f;
    [SerializeField, Min(10f)] private float turnSpeed = 200f;
    [Tooltip("Ground speed the Walk and Run clips were authored for, so feet do not slide.")]
    [SerializeField, Min(.05f)] private float walkClipSpeed = .65f;
    [SerializeField, Min(.05f)] private float runClipSpeed = 1.5f;
    [SerializeField, Min(.1f)] private float bodyRadius = .5f;

    [Header("Startle")]
    [Tooltip("A running chick closer than this (m) sends the sheep running.")]
    [SerializeField, Min(.5f)] private float startleRadius = 4.5f;
    [SerializeField, Min(1f)] private float calmDistance = 6.5f;
    [Tooltip("Sheep within this distance of a startled one get spooked too.")]
    [SerializeField, Min(0f)] private float panicSpread = 3.2f;

    private enum Mode { Idle, Graze, Walk, Flee }

    private static readonly List<FarmSheep> Flock = new List<FarmSheep>();
    private ChickPlayerController player;
    private Mode mode;
    private float modeTimer;
    private Vector3 target;
    private Vector3 velocity;
    private Vector3 threat;
    private float pendingStartle = -1f;
    private Vector3 pendingThreat;
    private string animState;

    public bool IsFleeing => mode == Mode.Flee;
    public bool IsLamb => mother != null;

    public void Configure(SheepPen home, FarmSheep followMother, Animator rig, float walk, float run, float walkClip,
        float runClip, float body)
    {
        pen = home;
        mother = followMother;
        animator = rig;
        walkSpeed = walk;
        runSpeed = run;
        walkClipSpeed = walkClip;
        runClipSpeed = runClip;
        bodyRadius = body;
    }

    private void OnEnable()
    {
        Flock.Add(this);
        if (animator == null) animator = GetComponentInChildren<Animator>();
        player = FindFirstObjectByType<ChickPlayerController>(FindObjectsInactive.Include);
        // Desynchronise idles so the flock never breathes in unison.
        animState = null;
        SetMode(Random.value < .5f ? Mode.Graze : Mode.Idle, Random.Range(.5f, 4f));
        if (animator != null) animator.Play(mode == Mode.Graze ? "Graze" : "Idle", 0, Random.value);
    }

    private void OnDisable() => Flock.Remove(this);

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || pen == null) return;

        if (player != null && player.isActiveAndEnabled && player.IsRunning)
        {
            Vector3 toPlayer = player.transform.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < startleRadius * startleRadius) Startle(player.transform.position, true);
        }
        if (pendingStartle >= 0f && (pendingStartle -= dt) <= 0f)
        {
            pendingStartle = -1f;
            Startle(pendingThreat, false);
        }

        modeTimer -= dt;
        Vector3 desired = Vector3.zero;
        switch (mode)
        {
            case Mode.Idle:
            case Mode.Graze:
                if (modeTimer <= 0f) ChooseNext();
                break;
            case Mode.Walk:
                desired = Seek(target, IsLamb && FarFromMother() ? walkSpeed * 1.5f : walkSpeed);
                if (Flat(target - transform.position).magnitude < .35f || modeTimer <= 0f)
                    SetMode(Random.value < .55f ? Mode.Graze : Mode.Idle, Random.Range(2.5f, 7f));
                break;
            case Mode.Flee:
                Vector3 away = Flat(transform.position - threat);
                float distance = away.magnitude;
                if (distance > 1e-3f) target = transform.position + away / distance * 3f;
                desired = Seek(pen.Constrain(target, bodyRadius), runSpeed);
                // Hemmed in against the wall: slide along it instead of pushing into it.
                desired += pen.Avoidance(transform.position, bodyRadius, .8f) * runSpeed;
                if (modeTimer <= 0f && distance > calmDistance * .6f) SetMode(Mode.Idle, Random.Range(1.2f, 2.5f));
                else if (modeTimer <= -3f) SetMode(Mode.Idle, Random.Range(1.2f, 2.5f));
                break;
        }

        if (mode != Mode.Flee) desired += pen.Avoidance(transform.position, bodyRadius, .6f) * walkSpeed;
        desired += Separation() * (mode == Mode.Flee ? runSpeed : walkSpeed);
        float accel = mode == Mode.Flee ? 7f : 2.2f;
        velocity = Vector3.MoveTowards(velocity, Vector3.ClampMagnitude(desired, mode == Mode.Flee ? runSpeed : walkSpeed * 1.5f),
            accel * dt);
        if (mode == Mode.Idle || mode == Mode.Graze) velocity = Vector3.MoveTowards(velocity, Vector3.zero, 4f * dt);

        Vector3 next = pen.Constrain(transform.position + velocity * dt, bodyRadius);
        next.y = pen.GroundHeight(next);
        transform.position = next;
        float speed = Flat(velocity).magnitude;
        if (speed > .05f)
        {
            Quaternion facing = Quaternion.LookRotation(Flat(velocity), Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, turnSpeed * dt);
        }
        Animate(speed);
    }

    private void Startle(Vector3 from, bool spread)
    {
        threat = from;
        bool wasCalm = mode != Mode.Flee;
        SetMode(Mode.Flee, Random.Range(1.4f, 2.2f));
        if (!wasCalm || !spread) return;
        foreach (var other in Flock)
        {
            if (other == this || other.pen != pen || other.IsFleeing || other.pendingStartle >= 0f) continue;
            if (Flat(other.transform.position - transform.position).magnitude > panicSpread) continue;
            other.pendingThreat = from;
            other.pendingStartle = Random.Range(.12f, .4f);
        }
    }

    private void ChooseNext()
    {
        if (IsLamb && FarFromMother())
        {
            target = pen.RandomPoint(bodyRadius, mother.transform.position, 1.2f);
            SetMode(Mode.Walk, 8f);
            return;
        }
        float roll = Random.value;
        if (roll < .4f)
        {
            target = IsLamb ? pen.RandomPoint(bodyRadius, mother.transform.position, 1.6f)
                : pen.RandomPoint(bodyRadius, transform.position, 4f);
            SetMode(Mode.Walk, 10f);
        }
        else if (roll < .8f) SetMode(Mode.Graze, Random.Range(4f, 9f));
        else SetMode(Mode.Idle, Random.Range(2f, 4.5f));
    }

    private bool FarFromMother() =>
        mother != null && Flat(mother.transform.position - transform.position).magnitude > 2.6f;

    private void SetMode(Mode next, float duration)
    {
        mode = next;
        modeTimer = duration;
    }

    private Vector3 Seek(Vector3 point, float speed)
    {
        Vector3 to = Flat(point - transform.position);
        float distance = to.magnitude;
        if (distance < 1e-3f) return Vector3.zero;
        // Ease in on arrival instead of overshooting.
        return to / distance * speed * Mathf.Clamp01(distance / .8f + .25f);
    }

    private Vector3 Separation()
    {
        Vector3 push = Vector3.zero;
        foreach (var other in Flock)
        {
            if (other == this || other.pen != pen) continue;
            Vector3 away = Flat(transform.position - other.transform.position);
            float reach = bodyRadius + other.bodyRadius + .25f;
            float distance = away.magnitude;
            if (distance >= reach || distance < 1e-4f) continue;
            push += away / distance * (1f - distance / reach);
        }
        if (player != null)
        {
            Vector3 away = Flat(transform.position - player.transform.position);
            float reach = bodyRadius + .45f;
            if (away.magnitude < reach && away.sqrMagnitude > 1e-6f) push += away.normalized * (1f - away.magnitude / reach);
        }
        return push;
    }

    private void Animate(float speed)
    {
        if (animator == null) return;
        string state;
        float playback = 1f;
        if (mode == Mode.Flee && speed > .3f)
        {
            state = "Run";
            playback = Mathf.Clamp(speed / runClipSpeed, .7f, 1.8f);
        }
        else if (speed > .08f)
        {
            state = "Walk";
            playback = Mathf.Clamp(speed / walkClipSpeed, .5f, 2f);
        }
        else state = mode == Mode.Graze ? "Graze" : "Idle";
        if (state != animState)
        {
            animState = state;
            animator.CrossFadeInFixedTime(state, state == "Run" ? .15f : .35f, 0);
        }
        animator.speed = playback;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
