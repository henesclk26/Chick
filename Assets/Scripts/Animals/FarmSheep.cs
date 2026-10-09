using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A sheep (or lamb) in the fenced pasture (<see cref="SheepPen"/>), following the game clock: mornings and
/// afternoons it grazes, now and then drinking at the trough or pulling hay from the rack; in the midday heat the
/// flock lies down in the trees' shade; from late afternoon it walks into the shelter and lies down for the night.
/// Lambs keep near their mother and now and then trot after the chick to play. The chick never scares them;
/// they only step aside when it walks into them.
/// Drives the Animator states Idle / Walk / Graze / Run / Lie and scales walking playback to the ground speed.
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
    [Tooltip("This sheep's sleeping place in the shelter (an index into the pen's rest spots).")]
    [SerializeField] private int restSlot;

    [Header("Day rhythm (game hours)")]
    [SerializeField] private float shadeFrom = 12f;
    [SerializeField] private float shadeUntil = 14f;
    [SerializeField] private float shelterFrom = 17.5f;

    [Header("Lamb play")]
    [Tooltip("A lamb may start playing when the chick comes this close (m).")]
    [SerializeField, Min(.5f)] private float playRadius = 3f;
    [SerializeField, Min(.5f)] private float playSpeed = 1.5f;
    [Tooltip("Ground speed the Run clip was authored for.")]
    [SerializeField, Min(.1f)] private float runClipSpeed = 1.1f;
    [SerializeField] private Vector2 playCooldown = new Vector2(20f, 45f);

    private enum Mode { Idle, Graze, Walk, Lie, Play }
    private enum Routine { Pasture, Shade, Shelter }

    private static readonly List<FarmSheep> Flock = new List<FarmSheep>();
    private static GameTimeManager clock;
    private ChickPlayerController player;
    private Mode mode;
    private float modeTimer;
    private Vector3 target;
    private Vector3 velocity;
    private string animState;
    // What to do on reaching a walk target, and what to face while doing it.
    private Mode arrival = Mode.Graze;
    private float arrivalFor;
    private Vector3 facePoint;
    private bool faceOnArrival;
    private float nextPlay;

    public bool IsLamb => mother != null;
    public bool IsLying => mode == Mode.Lie;

    public void Configure(SheepPen home, FarmSheep followMother, Animator rig, float walk, float walkClip, float body,
        int sleepingSlot)
    {
        pen = home;
        mother = followMother;
        animator = rig;
        walkSpeed = walk;
        walkClipSpeed = walkClip;
        bodyRadius = body;
        restSlot = sleepingSlot;
    }

    private void OnEnable()
    {
        Flock.Add(this);
        if (animator == null) animator = GetComponentInChildren<Animator>();
        player = FindFirstObjectByType<ChickPlayerController>(FindObjectsInactive.Include);
        if (clock == null) clock = FindFirstObjectByType<GameTimeManager>(FindObjectsInactive.Include);
        nextPlay = Time.time + Random.Range(playCooldown.x, playCooldown.y) * .5f;
        // Desynchronise idles so the flock never breathes in unison.
        animState = null;
        SetMode(Random.value < .5f ? Mode.Graze : Mode.Idle, Random.Range(.5f, 4f));
        if (animator != null) animator.Play(mode == Mode.Graze ? "Graze" : "Idle", 0, Random.value);
    }

    private void OnDisable() => Flock.Remove(this);

    private Routine Now
    {
        get
        {
            if (clock == null) return Routine.Pasture;
            float hour = clock.CurrentMinutes / 60f;
            if (hour >= shelterFrom) return Routine.Shelter;
            return hour >= shadeFrom && hour < shadeUntil ? Routine.Shade : Routine.Pasture;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || pen == null) return;

        modeTimer -= dt;
        if (IsLamb && mode != Mode.Play && mode != Mode.Lie && WantsToPlay(dt))
        {
            SetMode(Mode.Play, Random.Range(4f, 6.5f));
            nextPlay = Time.time + Random.Range(playCooldown.x, playCooldown.y);
        }

        Vector3 desired = Vector3.zero;
        float limit = walkSpeed * 1.5f;
        switch (mode)
        {
            case Mode.Idle:
            case Mode.Graze:
            case Mode.Lie:
                if (modeTimer <= 0f) ChooseNext();
                break;
            case Mode.Walk:
                desired = Seek(target, IsLamb && FarFromMother() ? walkSpeed * 1.5f : walkSpeed);
                if (Flat(target - transform.position).magnitude < .35f || modeTimer <= 0f) SetMode(arrival, arrivalFor);
                break;
            case Mode.Play:
                // Trot up behind the chick and keep a step away from it; give up when called back by distance.
                Vector3 toChick = player != null ? Flat(player.transform.position - transform.position) : Vector3.zero;
                bool lost = player == null || !player.isActiveAndEnabled || toChick.magnitude > playRadius * 2.5f ||
                            Flat(mother.transform.position - transform.position).magnitude > 7f;
                if (modeTimer <= 0f || lost) { SetMode(Mode.Idle, Random.Range(1f, 2f)); break; }
                target = player.transform.position - toChick.normalized * .8f;
                desired = Seek(target, playSpeed);
                limit = playSpeed;
                break;
        }

        if (mode != Mode.Lie)
        {
            desired += pen.Avoidance(transform.position, bodyRadius, .6f) * walkSpeed;
            desired += Separation() * walkSpeed;
        }
        velocity = Vector3.MoveTowards(velocity, Vector3.ClampMagnitude(desired, limit), (mode == Mode.Play ? 5f : 2.2f) * dt);
        if (mode == Mode.Idle || mode == Mode.Graze || mode == Mode.Lie)
            velocity = Vector3.MoveTowards(velocity, Vector3.zero, 4f * dt);

        Vector3 next = pen.Constrain(transform.position + velocity * dt, bodyRadius);
        next.y = pen.GroundHeight(next);
        transform.position = next;
        float speed = Flat(velocity).magnitude;
        Vector3 heading = speed > .05f ? Flat(velocity)
            : faceOnArrival && mode != Mode.Walk ? Flat(facePoint - transform.position) : Vector3.zero;
        if (heading.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(heading, Vector3.up),
                turnSpeed * dt);
        Animate(speed);
    }

    private void ChooseNext()
    {
        faceOnArrival = false;
        Routine routine = Now;
        if (IsLamb) { ChooseLamb(routine); return; }
        float roll = Random.value;
        switch (routine)
        {
            case Routine.Shelter:
                Vector3 bed = pen.RestSpot(restSlot), door = pen.ShelterDoor;
                // From the meadow go round to the open front first; from the doorway or inside, to the bed.
                if (Flat(bed - transform.position).magnitude <= .6f) SetMode(Mode.Lie, 30f);
                else if (Flat(door - transform.position).magnitude > 1f &&
                         Flat(bed - transform.position).magnitude > Flat(bed - door).magnitude) WalkTo(door, Mode.Idle, .1f);
                else WalkTo(bed, Mode.Lie, 30f);
                return;
            case Routine.Shade:
                if (Flat(pen.ShadeSpot - transform.position).magnitude > pen.ShadeRadius)
                    WalkTo(pen.RandomPoint(bodyRadius, pen.ShadeSpot, pen.ShadeRadius * .8f), Mode.Lie, Random.Range(12f, 25f));
                else if (roll < .6f) SetMode(Mode.Lie, Random.Range(12f, 25f));
                else if (roll < .85f) SetMode(Mode.Idle, Random.Range(3f, 6f));
                else SetMode(Mode.Graze, Random.Range(4f, 8f));
                return;
        }
        if (roll < .06f) WalkTo(pen.RandomPoint(bodyRadius, pen.TroughStand, .4f), Mode.Graze, Random.Range(5f, 8f), pen.TroughCenter);
        else if (roll < .11f) WalkTo(pen.RandomPoint(bodyRadius, pen.RackStand, .4f), Mode.Graze, Random.Range(6f, 10f), pen.RackCenter);
        else if (roll < .6f && StrayedFromFlock(out Vector3 flockCentre))
            WalkTo(pen.RandomPoint(bodyRadius, Vector3.Lerp(transform.position, flockCentre, .7f), 2f), NextRest(), Random.Range(2.5f, 7f));
        else if (roll < .42f) WalkTo(pen.RandomPoint(bodyRadius, transform.position, 4f), NextRest(), Random.Range(2.5f, 7f));
        else if (roll < .82f) SetMode(Mode.Graze, Random.Range(4f, 9f));
        else SetMode(Mode.Idle, Random.Range(2f, 4.5f));
    }

    // Lambs stay by their mother whatever the hour, and lie down when she does.
    private void ChooseLamb(Routine routine)
    {
        if (FarFromMother()) WalkTo(pen.RandomPoint(bodyRadius, mother.transform.position, 1f), Mode.Idle, 1f);
        else if (mother.IsLying) SetMode(Mode.Lie, Random.Range(6f, 12f));
        else
        {
            float roll = Random.value;
            if (roll < .35f) WalkTo(pen.RandomPoint(bodyRadius, mother.transform.position, 1.6f),
                routine == Routine.Pasture ? Mode.Graze : Mode.Idle, Random.Range(2f, 5f));
            else if (roll < .75f) SetMode(Mode.Graze, Random.Range(3f, 7f));
            else SetMode(Mode.Idle, Random.Range(2f, 4f));
        }
    }

    private Mode NextRest() => Random.value < .55f ? Mode.Graze : Mode.Idle;

    private void WalkTo(Vector3 point, Mode then, float thenFor, Vector3? face = null)
    {
        target = point;
        arrival = then;
        arrivalFor = thenFor;
        faceOnArrival = face.HasValue;
        if (face.HasValue) facePoint = face.Value;
        // Long trips (to the shelter or the shade) get time to arrive; a blocked sheep settles where it is.
        SetMode(Mode.Walk, Flat(point - transform.position).magnitude / walkSpeed * 2f + 10f);
    }

    private bool WantsToPlay(float dt) =>
        Now == Routine.Pasture && player != null && player.isActiveAndEnabled && Time.time >= nextPlay &&
        Flat(player.transform.position - transform.position).magnitude < playRadius &&
        Flat(mother.transform.position - transform.position).magnitude < 5f && Random.value < dt * .5f;

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
        mother != null && mode != Mode.Play && Flat(mother.transform.position - transform.position).magnitude > 2.6f;

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
        if (mode == Mode.Lie) state = "Lie";
        else if (mode == Mode.Play && speed > .6f)
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
            animator.CrossFadeInFixedTime(state, state == "Lie" ? .8f : .35f, 0);
        }
        animator.speed = playback;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
