using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The helper chick wanders and pecks nearby, follows when its owner leaves, and hops only
/// when a blocked route has a safe landing. Owner jump commands are never copied.
/// Created and destroyed by <see cref="HelperChickSpawner"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(150)] // After the player controller (100), so it reads this frame's player state.
[RequireComponent(typeof(CharacterController), typeof(Animator))]
public sealed class HelperChickController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(.1f)] private float walkSpeed = .8f;
    [SerializeField, Min(.1f)] private float runSpeed = 1.4f;
    [Tooltip("How hard the helper closes the distance to its navigation target, in 1/s.")]
    [SerializeField, Min(0f)] private float followGain = 4f;
    [Tooltip("Extra speed on top of the chicken's own speed while catching up.")]
    [SerializeField, Min(0f)] private float catchUpBonus = .7f;
    [SerializeField, Min(1f)] private float acceleration = 14f;
    [SerializeField] private float gravity = -18f;
    [SerializeField, Min(0f)] private float jumpHeight = .275f;
    [SerializeField, Min(0f)] private float minJumpAirTime = .18f;
    [SerializeField, Min(1f)] private float maxTurnSpeed = 540f;

    [Header("Independent movement")]
    [Tooltip("Free wandering radius; separate from the purchased food collection range.")]
    [SerializeField, Min(.6f)] private float idleRoamRadius = 1.35f;
    [Tooltip("Start catching up only after leaving this personal space, not on every owner step.")]
    [SerializeField, Min(1f)] private float followStartDistance = 2.2f;
    [SerializeField, Min(.5f)] private float followRestDistance = 1.2f;
    [SerializeField, Min(2f)] private float urgentFollowDistance = 3.6f;

    [Header("Obstacles")]
    [SerializeField] private LayerMask obstacleMask = ~(1 << 9);

    [Header("Eating")]
    [SerializeField] private LayerMask edibleMask = 1 << 9;
    [Tooltip("Peck animation speed of the chicken before upgrades; the helper pecks at a fraction of it.")]
    [SerializeField, Min(.1f)] private float baseEatAnimationSpeed = 1.4f;
    [SerializeField, Min(0f)] private float maxContactAssistDistance = .13f;
    [SerializeField] private Transform beakEatPoint;
    [SerializeField] private Vector3 localImpactPoint = new Vector3(-.007f, .011f, .087f);

    private enum EatPhase { None, Eating, Recovering }

    private const float ArriveDistance = .04f;
    private const float ScanInterval = .2f;
    private const float ContactAssistSeconds = .14f;
    private const float IgnoreSeconds = 8f;
    private const float StepHeight = .4f;
    private const int SlotCount = 12;
    private const float ClimbDelay = .4f;
    private const float UnreachableTeleportDelay = 6f;
    private const float HopClearance = .15f;
    private const float MaxHopSpeed = 3f;

    private static readonly int ToMove = Animator.StringToHash("to_move");
    private static readonly int Speed = Animator.StringToHash("speed");
    private static readonly int ToCrouch = Animator.StringToHash("to_crouch");
    private static readonly int ToLanding = Animator.StringToHash("to_landing");
    private static readonly int Eat = Animator.StringToHash("eat");
    private static readonly int Jump = Animator.StringToHash("jump");
    private static readonly int EatSpeed = Animator.StringToHash("EatPlaybackSpeed");
    private static readonly int RunPlaybackSpeed = Animator.StringToHash("RunPlaybackSpeed");

    private CharacterController body;
    private Animator animator;
    private PlayerUpgrades upgrades;
    private ChickPlayerController player;
    private CharacterController playerBody;
    private ChickEatingController playerEater;
    private IReadOnlyList<HelperChickController> companions;

    private readonly RaycastHit[] castHits = new RaycastHit[8];
    private readonly Collider[] overlaps = new Collider[128];
    private readonly Collider[] blockers = new Collider[8];
    private readonly Dictionary<int, float> ignoredUntil = new Dictionary<int, float>();

    private Vector3 lastPlayerPosition;
    private Vector3 playerVelocity;
    private Vector3 slotWorld;
    private bool settled;
    private bool following;
    private float roamTimer;
    private float roamTravelTime;
    private float wanderSpeed = .6f;
    private Vector3 followOffset;
    private float personalFollowDistance;
    private float personalRestDistance;
    private float reactionDelay;
    private float departureTimer;
    private float destinationTimer;
    private float hopDelay;
    private float stuckTimer;
    private float avoidSide = 1f;
    private bool steerBlocked;
    private float climbWait;
    private float belowTimer;
    private bool hopping;

    private Vector3 velocity;
    private Vector3 jumpVelocity;
    private float verticalVelocity = -1.5f;
    private bool grounded;
    private bool jumping;
    private float airborneTimer;
    private bool runAnimation;
    private float smoothedSpeed;
    private float yawVelocity;

    private EatPhase eatPhase;
    private EdibleObject foodTarget;
    private Vector3 standPoint;
    private float foodTimer;
    private float scanTimer;
    private Transform targetVisual;
    private Vector3 originalVisualLocalPosition;
    private Vector3 originalBitePosition;
    private Vector3 targetRootAtStart;
    private bool assistInvalidated;
    private bool impactHandled;
    private bool enteredEatState;
    private float startDeadline;
    private float eatClipLength = 80f / 60f;
    private float impactNormalizedTime = 21f / 80f;

    private float SlotSideOffset => (playerBody != null ? playerBody.radius : .18f) + body.radius + .1f;

    private void Awake()
    {
        body = GetComponent<CharacterController>();
        // The final centimetres of a food approach are slow; at high frame rates each step falls below the
        // default 1 mm threshold and the controller silently drops it, leaving the helper stalled.
        body.minMoveDistance = 0f;
        animator = GetComponent<Animator>();
        if (beakEatPoint == null)
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
                if (child.name == "BeakEatPoint") { beakEatPoint = child; break; }
        ReadAnimationTiming();
    }

    public void Initialize(PlayerUpgrades playerUpgrades, ChickPlayerController owner,
        IReadOnlyList<HelperChickController> flock = null)
    {
        companions = flock;
        upgrades = playerUpgrades;
        player = owner;
        playerBody = owner.GetComponent<CharacterController>();
        playerEater = owner.GetComponent<ChickEatingController>();
        Physics.IgnoreCollision(body, playerBody);
        if (companions != null)
            foreach (HelperChickController other in companions)
                if (other != null && other != this && other.body != null)
                    Physics.IgnoreCollision(body, other.body);
        lastPlayerPosition = owner.transform.position;
        SnapToSlot();
    }

    private void OnDisable() => CancelFood();

    private bool IsCompanion(HelperChickController other) =>
        other != null && other != this && other.isActiveAndEnabled && other.player == player;

    private bool FoodClaimedByCompanion(EdibleObject edible)
    {
        if (companions == null) return false;
        foreach (HelperChickController other in companions)
            if (IsCompanion(other) && other.foodTarget == edible) return true;
        return false;
    }

    private Vector3 SeparateFromCompanions(Vector3 desired)
    {
        if (jumping) return desired;
        Vector3 separation = Vector3.zero;
        Vector3 fromOwner = Flat(transform.position - player.transform.position);
        float ownerClearance = SlotSideOffset;
        if (fromOwner.magnitude < ownerClearance && Mathf.Abs(transform.position.y - player.transform.position.y) < body.height)
            separation += FlatOrDefault(fromOwner, -transform.forward).normalized *
                (1f - fromOwner.magnitude / ownerClearance) * walkSpeed * 2f;
        if (companions != null) foreach (HelperChickController other in companions)
        {
            if (!IsCompanion(other) || Mathf.Abs(other.transform.position.y - transform.position.y) > body.height) continue;
            Vector3 away = Flat(transform.position - other.transform.position);
            float spacing = body.radius + other.body.radius + .16f;
            float distance = away.magnitude;
            // Queue behind a nearby chick instead of overtaking through it or dodging to the other side.
            if (desired.sqrMagnitude > .01f && distance < spacing + .45f)
            {
                Vector3 forward = desired.normalized;
                float ahead = Vector3.Dot(-away, forward);
                float sideways = Mathf.Abs(Vector3.Cross(-away, forward).y);
                bool sameDirection = Vector3.Dot(other.transform.forward, forward) > .3f;
                if (ahead > 0f && sideways < spacing && (sameDirection || GetInstanceID() > other.GetInstanceID()))
                    desired *= Mathf.InverseLerp(spacing, spacing + .45f, ahead);
            }
            if (distance >= spacing) continue;
            Vector3 direction = distance > .001f ? away / distance :
                (GetInstanceID() < other.GetInstanceID() ? Vector3.left : Vector3.right);
            separation += direction * ((spacing - distance) / spacing) * walkSpeed * 2f;
        }
        return Vector3.ClampMagnitude(desired + separation, Mathf.Max(walkSpeed, desired.magnitude));
    }

    private void ReadAnimationTiming()
    {
        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            foreach (AnimationEvent clipEvent in clip.events)
            {
                if (clipEvent.functionName != nameof(OnEatImpact)) continue;
                eatClipLength = clip.length;
                impactNormalizedTime = clipEvent.time / clip.length;
            }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || player == null || !player.isActiveAndEnabled) return;

        Transform playerTransform = player.transform;
        Vector3 playerPosition = playerTransform.position;
        Vector3 playerStep = Flat(playerPosition - lastPlayerPosition);
        lastPlayerPosition = playerPosition;
        if (playerStep.sqrMagnitude > 4f) { SnapToSlot(); return; }
        playerVelocity = Vector3.Lerp(playerVelocity, playerStep / dt, 1f - Mathf.Exp(-12f * dt));
        float playerSpeed = playerVelocity.magnitude;
        float leash = upgrades != null ? upgrades.HelperRoamRadius : .5f;
        float playerDistance = Flat(transform.position - playerPosition).magnitude;

        UpdateNavigation(playerPosition, playerDistance, leash, dt);
        if (playerDistance > Mathf.Max(8f, leash * 5f)) { SnapToSlot(); return; }

        bool playerJumping = player.IsJumping;

        // The chicken stands on something higher (a melon, a crate) and the helper is not beside it yet.
        bool chickenAbove = !playerJumping && playerPosition.y - transform.position.y > .1f &&
                            Flat(slotWorld - transform.position).magnitude > .15f;
        // Nearby chicks may finish their own peck/approach even while the owner starts walking.
        UpdateFood(playerPosition, leash, playerDistance, !chickenAbove && !following, dt);

        bool busyEating = eatPhase != EatPhase.None;
        Vector3 desired = Vector3.zero;
        Vector3 faceDirection = Vector3.zero;
        if (!busyEating)
            desired = foodTarget != null ? ApproachFood(ref faceDirection) : FollowDesired(playerSpeed, dt);
        busyEating = eatPhase != EatPhase.None;
        if (!busyEating && foodTarget == null) desired = SeparateFromCompanions(desired);
        Vector3 steered = busyEating ? Vector3.zero : Steer(desired);

        // Turn into travel before accelerating, rather than sliding sideways or facing the owner.
        if (!busyEating)
        {
            Vector3 facing = faceDirection.sqrMagnitude > 1e-6f && !steerBlocked ? faceDirection : steered;
            if (jumping) facing = jumpVelocity;
            if (facing.sqrMagnitude > 1e-6f) RotateTowards(facing, dt, maxTurnSpeed);
            else yawVelocity = 0f;
            if (!jumping && steered.sqrMagnitude > 1e-6f)
            {
                float alignment = Vector3.Dot(transform.forward, steered.normalized);
                steered *= Mathf.Clamp01((alignment + .1f) / 1.1f);
            }
        }

        if (jumping)
        {
            // First clear the ledge vertically, then follow the checked landing arc.
            float movingFraction = Mathf.Clamp01((airborneTimer + dt - hopDelay) / dt);
            velocity = jumpVelocity * movingFraction;
            airborneTimer += dt;
        }
        else if (busyEating) velocity = Vector3.zero;
        else
            velocity = Vector3.MoveTowards(velocity, steered, acceleration * dt);

        if (grounded && verticalVelocity < 0f) verticalVelocity = -1.5f;
        float verticalStep = verticalVelocity * dt + .5f * gravity * dt * dt;
        verticalVelocity += gravity * dt;
        body.Move(velocity * dt + Vector3.up * verticalStep);
        grounded = body.isGrounded;
        if (jumping && grounded && airborneTimer >= minJumpAirTime && verticalVelocity <= 0f)
        {
            jumping = hopping = false;
            jumpVelocity = Vector3.zero;
            airborneTimer = 0f;
        }

        float actualSpeed = Flat(body.velocity).magnitude;
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, actualSpeed, 1f - Mathf.Exp(-14f * dt));

        bool stalled = desired.magnitude > .3f && actualSpeed < .1f;
        if (foodTarget == null && !jumping && following && stalled) stuckTimer += dt;
        else stuckTimer = Mathf.Max(0f, stuckTimer - 2f * dt);
        if (stuckTimer > UnreachableTeleportDelay && Flat(slotWorld - transform.position).magnitude > .5f) { SnapToSlot(); return; }

        bool blockedBelow = grounded && !jumping && !busyEating && (steerBlocked || stalled);
        if (UpdateClimb(playerPosition, chickenAbove, blockedBelow, dt)) return;

        UpdateAnimation();
    }

    // ---- Following -------------------------------------------------------------------------------

    private Vector3 FollowDesired(float playerSpeed, float dt)
    {
        Vector3 toSlot = Flat(slotWorld - transform.position);
        float distance = toSlot.magnitude;
        if (settled) { if (distance > .12f) settled = false; }
        else if (distance < .06f) settled = true;
        if (settled) return Vector3.zero;

        float ownerDistance = Flat(player.transform.position - transform.position).magnitude;
        float urgency = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(personalRestDistance, urgentFollowDistance, ownerDistance));
        // Distance controls pace. Never add the owner's velocity or copy its turn into our motion.
        float limit = following ? Mathf.Lerp(walkSpeed, Mathf.Max(runSpeed, playerSpeed + catchUpBonus), urgency) : wanderSpeed;
        float speed = Mathf.Min(limit, Mathf.Min(distance * followGain, distance / Mathf.Max(.001f, dt)));
        return toSlot.normalized * speed;
    }

    private void UpdateNavigation(Vector3 playerPosition, float distance, float leash, float dt)
    {
        float stopDistance = SlotSideOffset;
        float roamRadius = Mathf.Max(idleRoamRadius, leash);
        Vector3 fromHelper = Flat(playerPosition - transform.position);
        bool elevated = !player.IsJumping && playerPosition.y - transform.position.y > .12f;
        if (!following)
        {
            departureTimer = distance > Mathf.Max(personalFollowDistance, leash) ? departureTimer + dt : 0f;
            if (elevated || distance > urgentFollowDistance || departureTimer >= reactionDelay)
            {
                following = true;
                settled = false;
                destinationTimer = 0f;
                // Preserve the occupied side in WORLD space: no rotating slots or random side swaps.
                followOffset = FlatOrDefault(transform.position - playerPosition, -transform.forward).normalized * .65f;
            }
        }
        else if (distance <= (playerVelocity.magnitude < .15f ? Mathf.Max(personalRestDistance, personalFollowDistance - .1f) : personalRestDistance) && !elevated)
        {
            following = false;
            slotWorld = transform.position;
            settled = true;
            departureTimer = 0f;
            roamTimer = Random.Range(.65f, 1.5f);
            roamTravelTime = 0f;
        }
        if (following)
        {
            if (elevated)
            {
                slotWorld = playerPosition - FlatOrDefault(fromHelper, transform.forward).normalized * stopDistance;
                if (TryNearbyStand(playerPosition, out Vector3 raised)) slotWorld = raised;
            }
            else if (!jumping)
            {
                destinationTimer -= dt;
                if (destinationTimer <= 0f)
                {
                    // Commit to a ground destination instead of turning in lockstep with the owner.
                    destinationTimer = Random.Range(.65f, 1.05f);
                    Vector3 candidate = playerPosition + followOffset;
                    if (TryGround(candidate, playerPosition.y, out Vector3 stand) &&
                        Mathf.Abs(stand.y - playerPosition.y) <= .15f && PointFree(stand)) slotWorld = stand;
                    else slotWorld = playerPosition - FlatOrDefault(fromHelper, transform.forward).normalized * stopDistance;
                }
            }
            return;
        }
        if (foodTarget != null || jumping || !grounded || player.IsJumping) return;
        // Finish each excursion before choosing the next one. Each chick pauses on its own schedule.
        if (!settled && Flat(slotWorld - transform.position).magnitude > .1f)
        {
            roamTravelTime += dt;
            if (roamTravelTime < 4.5f) return;
            slotWorld = transform.position;
            settled = true;
        }
        roamTimer -= dt;
        if (roamTimer > 0f) return;
        roamTimer = Random.Range(.55f, 1.6f);
        roamTravelTime = 0f;
        wanderSpeed = Random.Range(walkSpeed * .6f, walkSpeed * .95f);
        for (int i = 0; i < 12; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, Random.Range(-140f, 140f), 0f) * transform.forward;
            Vector3 candidate = i < 8 ? transform.position + direction * Random.Range(.45f, .95f)
                : playerPosition + direction * Random.Range(roamRadius * .5f, roamRadius);
            float ownerGap = Flat(candidate - playerPosition).magnitude;
            if (ownerGap < stopDistance + .08f || ownerGap > roamRadius) continue;
            if (!TryGround(candidate, transform.position.y, out Vector3 ground) ||
                Mathf.Abs(ground.y - transform.position.y) > .12f || !PointFree(ground)) continue;
            Vector3 delta = Flat(ground - transform.position);
            if (delta.magnitude < .25f || !IsFree(delta.normalized, delta.magnitude, out _) || !SafeRoamPath(ground)) continue;
            slotWorld = ground;
            settled = false;
            break;
        }
    }

    private void ResetFollowBehaviour()
    {
        personalFollowDistance = followStartDistance * Random.Range(.92f, 1.1f);
        personalRestDistance = Mathf.Min(personalFollowDistance - .5f, followRestDistance * Random.Range(.9f, 1.1f));
        reactionDelay = Random.Range(.25f, .65f);
        departureTimer = destinationTimer = 0f;
        followOffset = Vector3.zero;
    }

    private bool SafeRoamPath(Vector3 end)
    {
        Vector3 start = transform.position;
        int steps = Mathf.Max(1, Mathf.CeilToInt(Flat(end - start).magnitude / .2f));
        for (int i = 1; i <= steps; i++)
        {
            Vector3 point = Vector3.Lerp(start, end, (float)i / steps);
            if (Flat(point - player.transform.position).magnitude < SlotSideOffset ||
                !TryGround(point, start.y, out Vector3 ground) || Mathf.Abs(ground.y - start.y) > .12f) return false;
        }
        return true;
    }

    private bool TryNearbyStand(Vector3 playerPosition, out Vector3 best)
    {
        best = playerPosition;
        float bestScore = float.PositiveInfinity;
        Vector3 radial = FlatOrDefault(transform.position - playerPosition, -player.transform.forward).normalized;
        for (int i = 0; i < SlotCount; i++)
        {
            Vector3 point = playerPosition + Quaternion.Euler(0f, i * 360f / SlotCount, 0f) * radial * SlotSideOffset;
            if (!TryGround(point, playerPosition.y, out Vector3 ground) ||
                Mathf.Abs(ground.y - playerPosition.y) > .15f || !StableLanding(ground)) continue;
            float score = (ground - transform.position).sqrMagnitude;
            if (score >= bestScore) continue;
            best = ground;
            bestScore = score;
        }
        return !float.IsPositiveInfinity(bestScore);
    }

    private void SnapToSlot()
    {
        Vector3 playerPosition = player.transform.position;
        if (!TryNearbyStand(playerPosition, out Vector3 ground)) return;
        slotWorld = ground;
        Teleport(ground, player.transform.rotation);
    }

    private void Teleport(Vector3 position, Quaternion rotation)
    {
        CancelFood();
        bool wasEnabled = body.enabled;
        body.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        body.enabled = wasEnabled;
        velocity = jumpVelocity = playerVelocity = Vector3.zero;
        verticalVelocity = -1.5f;
        jumping = hopping = false;
        airborneTimer = 0f;
        stuckTimer = climbWait = belowTimer = 0f;
        yawVelocity = 0f;
        settled = false;
        following = false;
        roamTimer = Random.Range(.35f, 1.1f);
        roamTravelTime = 0f;
        ResetFollowBehaviour();
    }

    // ---- Climbing -------------------------------------------------------------------------------

    // Returns true when the helper was teleported this frame.
    private bool UpdateClimb(Vector3 playerPosition, bool chickenAbove, bool blockedBelow, float dt)
    {
        if (hopping) return false;
        if (!following || player.IsJumping || foodTarget != null || !blockedBelow)
        {
            climbWait = 0f;
            belowTimer = Mathf.Max(0f, belowTimer - dt);
            return false;
        }
        climbWait += dt;
        belowTimer += dt;
        if (belowTimer >= UnreachableTeleportDelay && chickenAbove) { SnapToSlot(); return true; }
        if (climbWait >= ClimbDelay)
        {
            climbWait = 0f;
            if (TryLedgeHop(playerPosition)) belowTimer = stuckTimer = 0f;
        }
        return false;
    }

    // A flapping hop onto the surface the chicken stands on, up to the chicken's own jump height.
    private bool TryLedgeHop(Vector3 playerPosition)
    {
        Vector3 feet = transform.position;
        float maxRise = Mathf.Max(jumpHeight, player.CurrentJumpHeight);
        Vector3 towardPlayer = FlatOrDefault(slotWorld - feet, playerPosition - feet).normalized;
        // No obstacle on our route means no jump, even when the owner jumps beside us.
        if (!FindBlock(Vector3.zero, towardPlayer, .4f, true, out RaycastHit obstacle)) return false;
        // Probe the near edge first: this also allows intermediate steps, not only the owner's final surface.
        for (int i = 0; i < 5; i++)
        {
            float reach = obstacle.distance + body.radius * 2f + .06f + i * .12f;
            Vector3 point = feet + towardPlayer * reach;
            if (!RaycastGround(point + Vector3.up * (maxRise + .1f), maxRise + .2f, out RaycastHit top)) continue;
            float rise = top.point.y - feet.y;
            if (rise < .06f || rise > maxRise || !StableLanding(top.point)) continue;
            if (BeginHop(top.point)) return true;
        }
        if (TryHopLanding(slotWorld, playerPosition.y, feet, maxRise, out Vector3 landing))
            return BeginHop(landing);
        return false;
    }

    private bool BeginHop(Vector3 landing)
    {
        Vector3 feet = transform.position;
        float g = -gravity;
        if (g <= 0f) return false;
        float apex = landing.y - feet.y + HopClearance;
        float upSpeed = Mathf.Sqrt(2f * g * apex);
        float flightTime = upSpeed / g + Mathf.Sqrt(2f * HopClearance / g);
        float clearanceHeight = landing.y - feet.y + .025f;
        float delay = (upSpeed - Mathf.Sqrt(Mathf.Max(0f, upSpeed * upSpeed - 2f * g * clearanceHeight))) / g;
        float travelTime = flightTime - delay;
        Vector3 horizontal = Flat(landing - feet);
        float distance = horizontal.magnitude;
        if (travelTime <= .01f || distance / travelTime > MaxHopSpeed) return false;
        Vector3 across = horizontal / travelTime;
        // Check the actual swept arc, including overhead obstacles, before leaving the ground.
        Vector3 previous = Vector3.zero;
        for (int i = 1; i <= 16; i++)
        {
            float t = flightTime * i / 16f;
            Vector3 sample = across * Mathf.Max(0f, t - delay) + Vector3.up * (upSpeed * t - .5f * g * t * t);
            Vector3 segment = sample - previous;
            if (FindBlock(previous, segment.normalized, segment.magnitude, true, out _)) return false;
            previous = sample;
        }

        CancelFood();
        jumping = hopping = true;
        grounded = false;
        airborneTimer = 0f;
        hopDelay = delay;
        jumpVelocity = across;
        velocity = Vector3.zero;
        verticalVelocity = upSpeed;
        animator.ResetTrigger(Eat);
        animator.SetTrigger(Jump);
        animator.SetBool(ToCrouch, false);
        return true;
    }

    private bool TryHopLanding(Vector3 point, float chickenY, Vector3 feet, float maxRise, out Vector3 landing)
    {
        if (!TryGround(point, chickenY, out landing)) return false;
        float rise = landing.y - feet.y;
        return rise > .08f && rise <= maxRise && Mathf.Abs(landing.y - chickenY) < .15f && StableLanding(landing);
    }

    // A centre ray alone can choose the very lip of a box, causing a landing followed by a fall.
    private bool StableLanding(Vector3 point)
    {
        if (!PointFree(point)) return false;
        for (int i = 0; i < 4; i++)
        {
            Vector3 offset = Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward * body.radius * .85f;
            if (!RaycastGround(point + offset + Vector3.up * .12f, .22f, out RaycastHit support) ||
                Mathf.Abs(support.point.y - point.y) > .07f) return false;
        }
        return true;
    }

    // ---- Obstacle steering ----------------------------------------------------------------------

    private Vector3 Steer(Vector3 desired)
    {
        steerBlocked = false;
        float speed = desired.magnitude;
        if (speed < .01f) return Vector3.zero;
        Vector3 direction = desired / speed;
        float reach = Mathf.Max(.16f, speed * .25f);

        if (IsFree(direction, reach, out Vector3 normal)) return desired;
        steerBlocked = true;

        Vector3 slide = Vector3.ProjectOnPlane(direction, normal);
        if (slide.sqrMagnitude > .05f)
        {
            slide.Normalize();
            if (IsFree(slide, reach * .8f, out _))
            {
                float turn = Vector3.SignedAngle(direction, slide, Vector3.up);
                if (Mathf.Abs(turn) > 1f) avoidSide = Mathf.Sign(turn);
                return slide * speed * Mathf.Lerp(.5f, 1f, Vector3.Dot(direction, slide));
            }
        }

        for (int step = 1; step <= 4; step++)
            for (int flip = 0; flip < 2; flip++)
            {
                float sign = flip == 0 ? avoidSide : -avoidSide;
                Vector3 candidate = Quaternion.AngleAxis(step * 40f * sign, Vector3.up) * direction;
                if (!IsFree(candidate, reach * .8f, out _)) continue;
                avoidSide = sign;
                return candidate * speed * .8f;
            }
        return Vector3.zero;
    }

    // Casts the helper's own capsule along the direction; walkable ground never counts as an obstacle.
    private bool IsFree(Vector3 direction, float distance, out Vector3 normal)
    {
        normal = Vector3.zero;
        if (!FindBlock(Vector3.zero, direction, distance, true, out RaycastHit hit)) return true;
        normal = Flat(hit.normal);
        if (normal.sqrMagnitude < 1e-6f) normal = -direction;
        normal.Normalize();
        return false;
    }

    // Nearest hit of the helper's capsule, raised by lift, swept along direction.
    private bool FindBlock(Vector3 lift, Vector3 direction, float distance, bool ignoreFloors, out RaycastHit nearestHit)
    {
        nearestHit = default;
        float radius = body.radius * .95f;
        Vector3 center = transform.TransformPoint(body.center) + lift;
        float half = Mathf.Max(0f, body.height * .5f - radius);
        int count = Physics.CapsuleCastNonAlloc(center + Vector3.down * half, center + Vector3.up * half, radius,
            direction, castHits, distance, obstacleMask, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = castHits[i];
            if (hit.collider == null || IsOwnCollider(hit.collider) || hit.distance <= 0f) continue;
            if ((ignoreFloors && hit.normal.y > .6f) || hit.distance >= nearest) continue;
            nearest = hit.distance;
            nearestHit = hit;
        }
        return nearest < float.PositiveInfinity;
    }

    private bool IsOwnCollider(Collider collider) =>
        collider.transform.IsChildOf(transform) || (player != null && collider.transform.IsChildOf(player.transform)) ||
        IsCompanion(collider.GetComponentInParent<HelperChickController>());

    // Ray queries skip the helper's and the chicken's own colliders, so standing on a spot never invalidates it.
    private bool RaycastGround(Vector3 origin, float length, out RaycastHit best)
    {
        best = default;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, castHits, length, obstacleMask,
            QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = castHits[i];
            if (hit.collider == null || IsOwnCollider(hit.collider) || hit.normal.y < .5f || hit.distance >= nearest) continue;
            nearest = hit.distance;
            best = hit;
        }
        return nearest < float.PositiveInfinity;
    }

    private bool LineBlocked(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length < 1e-4f) return false;
        int count = Physics.RaycastNonAlloc(from, delta / length, castHits, length, obstacleMask,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (castHits[i].collider != null && !IsOwnCollider(castHits[i].collider)) return true;
        return false;
    }

    private bool TryGround(Vector3 point, float referenceY, out Vector3 ground)
    {
        if (RaycastGround(new Vector3(point.x, referenceY + .3f, point.z), 1.2f, out RaycastHit hit))
        {
            ground = hit.point;
            return true;
        }
        ground = point;
        return false;
    }

    private bool PointFree(Vector3 ground)
    {
        if (companions != null)
            foreach (HelperChickController other in companions)
                if (IsCompanion(other) && Mathf.Abs(other.transform.position.y - ground.y) < body.height &&
                    Flat(other.transform.position - ground).magnitude < body.radius + other.body.radius + .05f)
                    return false;
        float radius = body.radius * .9f;
        int count = Physics.OverlapCapsuleNonAlloc(ground + Vector3.up * (radius + .04f),
            ground + Vector3.up * Mathf.Max(radius + .04f, body.height - radius), radius, blockers,
            obstacleMask, QueryTriggerInteraction.Ignore);
        if (count == blockers.Length) return false;
        for (int i = 0; i < count; i++)
            if (!IsOwnCollider(blockers[i])) return false;
        return true;
    }

    // ---- Eating ---------------------------------------------------------------------------------

    private void UpdateFood(Vector3 playerPosition, float leash, float playerDistance, bool playerCalm, float dt)
    {
        if (foodTarget != null)
        {
            bool leftLeash = playerDistance > leash + .6f;
            bool playerMoving = eatPhase == EatPhase.None && !playerCalm;
            bool finished = eatPhase != EatPhase.None && impactHandled;
            bool lost = !foodTarget.CanBeEaten() && !finished;
            if (following || leftLeash || playerMoving || lost || jumping) { CancelFood(); return; }
            if (eatPhase == EatPhase.None)
            {
                foodTimer += dt;
                if (foodTimer > 5f) { Ignore(foodTarget); CancelFood(); }
            }
            return;
        }

        if (eatPhase != EatPhase.None) return; // Consumed objects may be destroyed before recovery ends.
        scanTimer -= dt;
        if (scanTimer > 0f || !playerCalm || jumping || !grounded) return;
        scanTimer = ScanInterval;
        FindFood(playerPosition, leash);
    }

    private void FindFood(Vector3 playerPosition, float leash)
    {
        int count = Physics.OverlapSphereNonAlloc(playerPosition + Vector3.up * .05f, leash + .15f, overlaps,
            edibleMask, QueryTriggerInteraction.Collide);
        EdibleObject best = null;
        float bestScore = float.PositiveInfinity;
        Vector3 helperPosition = transform.position;
        for (int i = 0; i < count; i++)
        {
            EdibleObject edible = overlaps[i].GetComponentInParent<EdibleObject>();
            overlaps[i] = null;
            if (!IsEdible(edible) || IsIgnored(edible) || FoodClaimedByCompanion(edible)) continue;
            if (playerEater != null && playerEater.CurrentTarget == edible) continue;
            Vector3 bite = edible.BitePosition;
            if (Flat(bite - playerPosition).sqrMagnitude > leash * leash) continue;
            Vector3 toFood = Flat(bite - helperPosition);
            // Prefer reachable food ahead over an equally close seed behind the helper.
            float turnCost = (1f - Vector3.Dot(transform.forward, toFood.normalized)) * .08f;
            float score = toFood.sqrMagnitude + Mathf.Abs(bite.y - helperPosition.y) + turnCost;
            if (score < bestScore) { best = edible; bestScore = score; }
        }
        if (best != null) TryClaim(best);
    }

    private bool IsEdible(EdibleObject edible) =>
        edible != null && beakEatPoint != null && edible.CanBeEaten() && edible.ContactVisual != null &&
        edible.ContactVisual != edible.transform && edible.ContactVisual.IsChildOf(edible.transform) &&
        (edibleMask.value & (1 << edible.gameObject.layer)) != 0 &&
        edible.IsSurfaceAccessibleFrom(body.bounds.center);

    private void TryClaim(EdibleObject edible)
    {
        if (FoodClaimedByCompanion(edible)) return;
        Vector3 bite = edible.BitePosition;
        if (!RaycastGround(bite + Vector3.up * .05f, .45f, out RaycastHit floor) ||
            Mathf.Abs(bite.y - (floor.point.y + localImpactPoint.y)) > edible.MaxVerticalInteractionOffset)
        {
            Ignore(edible);
            return;
        }
        Vector3 approach = FlatOrDefault(bite - transform.position, transform.forward).normalized;
        // A seed already in beak range does not require stepping backwards and turning twice.
        Vector3 stand = CanReachFacing(edible) ? transform.position : bite - approach * localImpactPoint.z;
        if (!TryGround(stand, transform.position.y, out Vector3 ground) ||
            Mathf.Abs(ground.y - transform.position.y) > StepHeight || !PointFree(ground))
        {
            Ignore(edible);
            return;
        }
        foodTarget = edible;
        standPoint = ground;
        foodTimer = 0f;
        slotWorld = transform.position;
        settled = true;
        roamTimer = Random.Range(1.5f, 3f);
    }

    private Vector3 ApproachFood(ref Vector3 faceDirection)
    {
        Vector3 toStand = Flat(standPoint - transform.position);
        float distance = toStand.magnitude;
        Vector3 toFood = Flat(foodTarget.BitePosition - transform.position);
        faceDirection = toFood;
        if (CanReachFacing(foodTarget))
        {
            if (velocity.magnitude < .06f && Vector3.Angle(transform.forward, toFood) <= 12f) TryBeginEat();
            return Vector3.zero;
        }
        if (distance > ArriveDistance)
            return toStand / distance * Mathf.Min(distance * 3f, walkSpeed, distance / Mathf.Max(.001f, Time.deltaTime));
        if (toFood.sqrMagnitude > 1e-6f && Vector3.Angle(transform.forward, toFood) <= 12f) TryBeginEat();
        return Vector3.zero;
    }

    private bool CanReachFacing(EdibleObject edible)
    {
        Vector3 toFood = Flat(edible.BitePosition - transform.position);
        if (toFood.sqrMagnitude < .0001f) return false;
        Quaternion facing = Quaternion.LookRotation(toFood.normalized, Vector3.up);
        Vector3 predicted = transform.position + facing * Vector3.Scale(localImpactPoint, transform.lossyScale);
        float assist = maxContactAssistDistance * edible.InteractionAssistMultiplier * .85f;
        return Mathf.Abs(edible.BitePosition.y - predicted.y) <= edible.MaxVerticalInteractionOffset &&
            (edible.BitePosition - predicted).sqrMagnitude <= assist * assist;
    }

    private void TryBeginEat()
    {
        if (eatPhase != EatPhase.None || jumping || !grounded || foodTarget == null || beakEatPoint == null) return;
        if (!IsInReach(foodTarget)) { Ignore(foodTarget); CancelFood(); return; }

        targetVisual = foodTarget.ContactVisual;
        originalVisualLocalPosition = targetVisual.localPosition;
        originalBitePosition = foodTarget.BitePosition;
        targetRootAtStart = foodTarget.transform.position;
        assistInvalidated = false;
        impactHandled = false;
        enteredEatState = false;
        startDeadline = Time.time + .25f;

        float fraction = upgrades != null ? upgrades.HelperEatSpeedFraction : .5f;
        animator.SetFloat(EatSpeed, baseEatAnimationSpeed * fraction);
        animator.ResetTrigger(Jump);
        animator.SetTrigger(Eat);
        animator.SetBool(ToCrouch, false);
        eatPhase = EatPhase.Eating;
    }

    private bool IsInReach(EdibleObject edible)
    {
        if (!IsEdible(edible)) return false;
        Vector3 predicted = transform.TransformPoint(localImpactPoint);
        Vector3 planar = Flat(edible.BitePosition - transform.position);
        if (planar.sqrMagnitude < 1e-6f || Vector3.Dot(planar.normalized, transform.forward) < Mathf.Cos(80f * Mathf.Deg2Rad))
            return false;
        float assist = maxContactAssistDistance * edible.InteractionAssistMultiplier;
        return Mathf.Abs(edible.BitePosition.y - predicted.y) <= edible.MaxVerticalInteractionOffset &&
               (edible.BitePosition - predicted).sqrMagnitude <= assist * assist;
    }

    // Animation Event on the eat clip: the first beak minimum.
    public void OnEatImpact()
    {
        if (eatPhase != EatPhase.Eating || impactHandled) return;
        bool atContact = ApplyContactAssist(1f);
        impactHandled = true;
        bool eaten = atContact && foodTarget != null && IsInReach(foodTarget) && foodTarget.Consume();
        if (!eaten && foodTarget != null) Ignore(foodTarget);
        RestoreContactVisual();
    }

    // Animation Event on the eat clip: the first recovery crest.
    public void OnEatRecovery()
    {
        if (eatPhase == EatPhase.Eating) eatPhase = EatPhase.Recovering;
    }

    private void LateUpdate()
    {
        if (eatPhase == EatPhase.None || animator == null) return;
        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
        bool transitioning = animator.IsInTransition(0);
        bool currentEat = current.IsName("eat");
        bool nextEat = transitioning && animator.GetNextAnimatorStateInfo(0).IsName("eat");
        if (currentEat || nextEat) enteredEatState = true;

        if (eatPhase == EatPhase.Eating && !impactHandled && currentEat)
        {
            float playback = Mathf.Max(.01f, current.speed * current.speedMultiplier * animator.speed);
            float remaining = (impactNormalizedTime - current.normalizedTime) * eatClipLength / playback;
            if (remaining <= ContactAssistSeconds)
                ApplyContactAssist(Mathf.Clamp01(1f - remaining / ContactAssistSeconds));
        }

        if (eatPhase == EatPhase.Eating && currentEat && current.normalizedTime >= 1f) eatPhase = EatPhase.Recovering;
        if (!currentEat && !nextEat && enteredEatState && !transitioning) FinishEat();
        else if (!enteredEatState && Time.time > startDeadline) CancelFood();
    }

    private bool ApplyContactAssist(float progress)
    {
        if (assistInvalidated || foodTarget == null || targetVisual == null || beakEatPoint == null ||
            !IsInReach(foodTarget) ||
            (foodTarget.transform.position - targetRootAtStart).sqrMagnitude > 1e-6f)
        {
            assistInvalidated = true;
            RestoreContactVisual();
            return false;
        }

        Vector3 destination = beakEatPoint.position;
        float verticalBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.75f, 1f, progress));
        destination.y = Mathf.Lerp(originalBitePosition.y, destination.y, verticalBlend);
        Vector3 correction = Vector3.ClampMagnitude(destination - originalBitePosition,
            maxContactAssistDistance * foodTarget.InteractionAssistMultiplier);
        Vector3 visualStart = targetVisual.parent.TransformPoint(originalVisualLocalPosition);
        targetVisual.position = visualStart + correction * Mathf.SmoothStep(0f, 1f, progress);
        return progress >= 1f &&
               Vector3.Distance(originalBitePosition + correction, beakEatPoint.position) <= .012f;
    }

    private void RestoreContactVisual()
    {
        if (targetVisual != null) targetVisual.localPosition = originalVisualLocalPosition;
    }

    private void FinishEat()
    {
        RestoreContactVisual();
        if (foodTarget != null && foodTarget.CanBeEaten()) Ignore(foodTarget);
        eatPhase = EatPhase.None;
        foodTarget = null;
        targetVisual = null;
        impactHandled = false;
        enteredEatState = false;
    }

    private void CancelFood()
    {
        RestoreContactVisual();
        if (animator != null) animator.ResetTrigger(Eat);
        eatPhase = EatPhase.None;
        foodTarget = null;
        targetVisual = null;
        impactHandled = false;
        enteredEatState = false;
    }

    private void Ignore(EdibleObject edible)
    {
        if (edible == null) return;
        if (ignoredUntil.Count > 64)
        {
            var expired = new List<int>();
            foreach (KeyValuePair<int, float> pair in ignoredUntil)
                if (Time.time >= pair.Value) expired.Add(pair.Key);
            foreach (int id in expired) ignoredUntil.Remove(id);
        }
        ignoredUntil[edible.GetInstanceID()] = Time.time + IgnoreSeconds;
    }

    private bool IsIgnored(EdibleObject edible) =>
        ignoredUntil.TryGetValue(edible.GetInstanceID(), out float until) && Time.time < until;

    // ---- Animation ------------------------------------------------------------------------------

    private void UpdateAnimation()
    {
        bool eating = eatPhase == EatPhase.Eating;
        bool busy = eatPhase != EatPhase.None;
        float speed = smoothedSpeed;
        bool moving = !busy && speed > .08f;

        if (runAnimation) runAnimation = speed > 1.1f;
        else runAnimation = speed > 1.35f;
        bool running = moving && runAnimation;

        bool sitting = player.IsSitting && !moving && !busy && grounded && !jumping && foodTarget == null;

        animator.SetBool(ToMove, moving);
        animator.SetBool(ToCrouch, sitting);
        animator.SetBool(ToLanding, grounded);
        animator.SetFloat(Speed, moving ? (running ? 2f : 1f) : 0f);
        animator.SetFloat(RunPlaybackSpeed, Mathf.Clamp(speed / runSpeed, .8f, 4f));

        // Stepping off a ledge (e.g. following the chicken down from a melon) reads as a short drop.
        bool falling = !grounded && !jumping && verticalVelocity < -2.5f;
        string baseState;
        if (jumping) baseState = verticalVelocity > 0f ? "jump" : "jump_descent";
        else if (falling) baseState = "jump_descent";
        else if (eating) baseState = "eat";
        else if (sitting) baseState = "crouch";
        else if (moving) baseState = running ? "run" : "move";
        else baseState = "idle";
        EnsureState(baseState, 0);

        // The walk clip is authored for the chick's own pace; stretch it when keeping up with the chicken.
        animator.speed = baseState == "move" ? Mathf.Clamp(speed / walkSpeed, .75f, 2f) : 1f;

        // A ledge hop is higher than the chick's own jump, so it flaps from take-off.
        string wingState = hopping || falling ? "wing_flapping"
            : jumping ? (airborneTimer < .2f ? "wing_down" : "wing_flapping") : "wing_idle";
        EnsureState(wingState, 1);
    }

    private void EnsureState(string stateName, int layerIndex)
    {
        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(layerIndex);
        if (current.IsName(stateName)) return;
        if (animator.IsInTransition(layerIndex) && animator.GetNextAnimatorStateInfo(layerIndex).IsName(stateName)) return;
        float duration = stateName == "jump" || stateName == "jump_descent" ? .18f : stateName == "eat" ? .05f : .16f;
        animator.CrossFadeInFixedTime(stateName, duration, layerIndex, 0f);
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private void RotateTowards(Vector3 direction, float dt, float maxSpeed)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 1e-4f) return;
        float yaw = transform.eulerAngles.y;
        float target = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        float next = Mathf.SmoothDampAngle(yaw, target, ref yawVelocity, .1f, maxSpeed, dt);
        transform.rotation = Quaternion.Euler(0f, next, 0f);
    }

    private static Vector3 Flat(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    private static Vector3 FlatOrDefault(Vector3 value, Vector3 fallback)
    {
        value.y = 0f;
        if (value.sqrMagnitude > 1e-6f) return value;
        fallback.y = 0f;
        return fallback.sqrMagnitude > 1e-6f ? fallback.normalized : Vector3.forward;
    }
}
