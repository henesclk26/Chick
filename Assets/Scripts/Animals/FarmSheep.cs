using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A sheep (or lamb) inside a <see cref="SheepPen"/>: idles, grazes and ambles to new spots, drifts back
/// towards the flock when it strays, and lambs keep near their mother. The chick does not scare them;
/// they only step aside when it walks into them.
/// Drives the Animator states Idle / Walk / Graze and scales the walk's playback to the ground speed.
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
    [SerializeField, Min(10f)] private float turnSpeed = 200f;
    [Tooltip("Ground speed the Walk clip was authored for, so feet do not slide.")]
    [SerializeField, Min(.05f)] private float walkClipSpeed = .65f;
    [SerializeField, Min(.1f)] private float bodyRadius = .5f;
    [Tooltip("A sheep further than this (m) from the rest of the flock wanders back towards it.")]
    [SerializeField, Min(2f)] private float strayDistance = 7f;

    private enum Mode { Idle, Graze, Walk }

    private static readonly List<FarmSheep> Flock = new List<FarmSheep>();
    private ChickPlayerController player;
    private Mode mode;
    private float modeTimer;
    private Vector3 target;
    private Vector3 velocity;
    private string animState;

    public bool IsLamb => mother != null;

    public void Configure(SheepPen home, FarmSheep followMother, Animator rig, float walk, float walkClip, float body)
    {
        pen = home;
        mother = followMother;
        animator = rig;
        walkSpeed = walk;
        walkClipSpeed = walkClip;
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
        }

        desired += pen.Avoidance(transform.position, bodyRadius, .6f) * walkSpeed;
        desired += Separation() * walkSpeed;
        velocity = Vector3.MoveTowards(velocity, Vector3.ClampMagnitude(desired, walkSpeed * 1.5f), 2.2f * dt);
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

    private void ChooseNext()
    {
        if (IsLamb && FarFromMother())
        {
            target = pen.RandomPoint(bodyRadius, mother.transform.position, 1.2f);
            SetMode(Mode.Walk, 8f);
            return;
        }
        float roll = Random.value;
        if (!IsLamb && roll < .6f && StrayedFromFlock(out Vector3 flockCentre))
        {
            target = pen.RandomPoint(bodyRadius, Vector3.Lerp(transform.position, flockCentre, .7f), 2f);
            SetMode(Mode.Walk, 16f);
        }
        else if (roll < .4f)
        {
            target = IsLamb ? pen.RandomPoint(bodyRadius, mother.transform.position, 1.6f)
                : pen.RandomPoint(bodyRadius, transform.position, 4f);
            SetMode(Mode.Walk, 10f);
        }
        else if (roll < .8f) SetMode(Mode.Graze, Random.Range(4f, 9f));
        else SetMode(Mode.Idle, Random.Range(2f, 4.5f));
    }

    // Grazing sheep keep loosely together: a sheep far from the others' centre heads back.
    private bool StrayedFromFlock(out Vector3 centre)
    {
        centre = Vector3.zero;
        int count = 0;
        foreach (var other in Flock)
        {
            if (other == this || other.pen != pen || other.IsLamb) continue;
            centre += other.transform.position;
            count++;
        }
        if (count == 0) return false;
        centre /= count;
        return Flat(centre - transform.position).magnitude > strayDistance;
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
        if (speed > .08f)
        {
            state = "Walk";
            playback = Mathf.Clamp(speed / walkClipSpeed, .5f, 2f);
        }
        else state = mode == Mode.Graze ? "Graze" : "Idle";
        if (state != animState)
        {
            animState = state;
            animator.CrossFadeInFixedTime(state, .35f, 0);
        }
        animator.speed = playback;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
