using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(100)] // Turn after the orbit camera has updated its yaw.
public sealed class ChickPlayerController : MonoBehaviour
{
    [Header("Movement")]
        [SerializeField] private float moveSpeed = 1.2f;
        [SerializeField] private float runSpeed = 2f;
        [SerializeField] private float chickMoveSpeed = 0.8f;
        [SerializeField] private float chickRunSpeed = 1.4f;
        [SerializeField] private float gravity = -18f;
    [SerializeField] private float jumpHeight = 0.55f;
    [SerializeField] private float chickJumpHeight = 0.275f;
    [SerializeField] private float minJumpAirTime = 0.18f;
    [Tooltip("\"Çift Zıplama\": the mid-air jump reaches this fraction of the ground jump's height.")]
    [SerializeField, Range(.1f, 1f)] private float secondJumpHeightMultiplier = .8f;
    [Tooltip("How long the mid-air jump shows the full-body wing push before the normal jump pose.")]
    [SerializeField, Min(0f)] private float airJumpFlapSeconds = .32f;
    [Tooltip("\"Süzülme\": holding Space while falling from a jump caps the fall at this speed (m/s).")]
    [SerializeField, Min(.05f)] private float glideFallSpeed = .9f;
    [Tooltip("While gliding, steering input moves this many times faster than on foot, carrying the glide farther.")]
    [SerializeField, Min(1f)] private float glideSpeedMultiplier = 1.2f;
    [Tooltip("How quickly a fast fall is braked down to the glide speed (m/s²), on top of gravity.")]
    [SerializeField, Min(0f)] private float glideBrake = 30f;
    [SerializeField, Min(0f)] private float airSteeringAcceleration = 8f;
    [Tooltip("\"Sprint Hızı\" levels speed up running (Shift) and its run animation together.")]
    [SerializeField] private PlayerUpgrades upgrades;

    [Header("Sprint Stamina")]
    [Tooltip("Seconds of continuous sprint before \"Sprint Süresi\" upgrades.")]
    [SerializeField, Min(.5f)] private float baseSprintSeconds = 4f;
    [Tooltip("Seconds to refill from empty to full once sprinting stops.")]
    [SerializeField, Min(.1f)] private float sprintRefillSeconds = 4f;
    [SerializeField, Min(0f)] private float sprintRefillDelay = .75f;
    [Tooltip("After running out, sprint unlocks again at this fraction, so tapping Shift cannot chain sprints.")]
    [SerializeField, Range(0f, 1f)] private float sprintRecoverFraction = .25f;

    [Header("Character Turning")]
    [SerializeField, Min(0.01f)] private float movementSteeringSmoothTime = 0.03f;
    [SerializeField, Min(0.01f)] private float visualRotationSmoothTime = 0.14f;
    [SerializeField, Min(1f)] private float maxVisualTurnSpeed = 540f;
    [SerializeField, Range(0f, 2f)] private float turnDeadZone = 0.5f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private float sitAfterIdleSeconds = 10f;
    [SerializeField] private float peckLockSeconds = 0.85f;
    [SerializeField] private float peepLockSeconds = 0.9f;
    [SerializeField] private float damageLockSeconds = 1.0f;
    [SerializeField] private float downLockSeconds = 1.0f;
    [SerializeField] private float landingLockSeconds = 0.35f;

    private CharacterController controller;
    private ChickEatingController eatingController;
    // Optional pond behaviours; without them the bird moves exactly as before.
    private ChickDrinkingController drinkingController;
    private ChickWaterWading waterWading;
    private Transform movementCamera;
    private FreeOrbitThirdPersonCamera orbitCamera;
    private float verticalVelocity;
    private Vector3 jumpHorizontalVelocity;
    private float lastHeldCameraYaw;
    private bool rightMouseTurning;
    private float idleTimer;

    private bool jumping;
    private bool airJumpUsed;
    private float airJumpFlapTimer;
    private bool gliding;
    private bool eating;
    private float peckTimer;
    private float peepTimer;
    private float damageTimer;
    private float downTimer;
    private float airborneTimer;
    private float landingLockTimer;
    private float flapTimer;
    private float lastTurnSide = 1f;
    private float visualYawVelocity;
    private float steeringYaw;
    private float steeringYawVelocity;
    private float steeringSide = 1f;
    private bool steeringActive;
    private bool chickenForm;

    private float sprintStamina = -1f; // seconds left; filled on the first update
    private float sprintRefillWait;
    private bool sprintExhausted;

    // Session-only developer overrides; saved upgrades and movement settings stay intact.
    private bool debugInfiniteSprint;
    public bool DebugInfiniteSprint
    {
        get => debugInfiniteSprint;
        set
        {
            debugInfiniteSprint = value;
            if (!value) return;
            sprintStamina = SprintSeconds;
            sprintExhausted = false;
            sprintRefillWait = 0f;
        }
    }
    public bool DebugSprintSpeed5x { get; set; }
    public bool DebugJumpHeight2x { get; set; }

    public float SprintSeconds => baseSprintSeconds + (upgrades != null ? upgrades.SprintDurationBonusSeconds : 0f);
    public float SprintStamina01 => sprintStamina < 0f ? 1f : Mathf.Clamp01(sprintStamina / SprintSeconds);
    public bool SprintExhausted => sprintExhausted;
    public Vector3 FacingDirection => transform.forward;
    public Animator ActiveAnimator => animator;
    public bool GrowthControlsLocked { get; set; }
    public bool EggLayingPose { get; private set; }
    public bool IsJumping => jumping;
    public bool IsGliding => gliding;
    /// <summary>Counts take-offs from the ground only; ledge glides and mid-air jumps leave it unchanged.</summary>
    public int GroundJumpCount { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsSitting { get; private set; }
    public bool IsDrinking => drinkingController != null && drinkingController.IsBusy;
    public float CurrentJumpHeight => (chickenForm ? jumpHeight : chickJumpHeight) * (DebugJumpHeight2x ? 2f : 1f);
    public bool CanChangeForm => !GrowthControlsLocked && !EggLayingPose && controller != null && controller.isGrounded && !jumping &&
        (eatingController == null || !eatingController.IsBusy) && !IsDrinking && peckTimer <= 0f &&
        peepTimer <= 0f && damageTimer <= 0f && downTimer <= 0f && flapTimer <= 0f;

    public bool TryBeginEggLaying()
    {
        if (!chickenForm || !isActiveAndEnabled || !CanChangeForm) return false;
        EggLayingPose = true;
        idleTimer = 0f;
        rightMouseTurning = steeringActive = false;
        visualYawVelocity = steeringYawVelocity = 0f;
        UpdateAnimationStates(false, false, true, true, false, false, false, false, false, false);
        return true;
    }

    public void EndEggLaying()
    {
        if (!EggLayingPose) return;
        EggLayingPose = false;
        idleTimer = 0f;
        if (animator != null)
        {
            animator.SetBool(ToCrouch, false);
            EnsureState("idle", 0);
        }
    }

    public void SetFormAnimator(Animator next)
    {
        EndEggLaying();
        animator = next;
        chickenForm = next != GetComponent<Animator>();
        idleTimer = peckTimer = peepTimer = damageTimer = downTimer = flapTimer = 0f;
        landingLockTimer = 0f;
        eating = false;
        if (drinkingController != null) drinkingController.Cancel();
        visualYawVelocity = steeringYawVelocity = 0f;
        steeringActive = rightMouseTurning = false;
        animator.Play("idle", 0, 0f);
        animator.Play("wing_idle", 1, 0f);
        animator.Update(0f);
    }

public void TeleportTo(Vector3 position, Quaternion rotation)
    {
        EndEggLaying();
        if (controller == null) controller = GetComponent<CharacterController>();
        if (eatingController != null) eatingController.CancelEat();
        if (drinkingController != null) drinkingController.Cancel();
        bool capsuleWasEnabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        if (controller != null) controller.enabled = capsuleWasEnabled;

        verticalVelocity = -1.5f;
        jumpHorizontalVelocity = Vector3.zero;
        jumping = false;
        airJumpUsed = false;
        airJumpFlapTimer = 0f;
        gliding = false;
        airborneTimer = 0f;
        landingLockTimer = 0f;
        idleTimer = 0f;
        steeringActive = false;
        rightMouseTurning = false;
        steeringYawVelocity = 0f;
        visualYawVelocity = 0f;
        eating = false;
        if (orbitCamera != null) orbitCamera.SnapToTarget();
    }


    private static readonly int ToMove = Animator.StringToHash("to_move");
    private static readonly int Speed = Animator.StringToHash("speed");
    private static readonly int ToCrouch = Animator.StringToHash("to_crouch");
    private static readonly int Eat = Animator.StringToHash("eat");
    private static readonly int Jump = Animator.StringToHash("jump");
    private static readonly int ToLanding = Animator.StringToHash("to_landing");
    private static readonly int Peep = Animator.StringToHash("peep");
    private static readonly int Peck = Animator.StringToHash("peck");
    private static readonly int Damage = Animator.StringToHash("damage");
    private static readonly int DuringDamage = Animator.StringToHash("during_damage");
    private static readonly int RunPlaybackSpeed = Animator.StringToHash("RunPlaybackSpeed");

    private float SprintMultiplier => upgrades != null ? Mathf.Max(1f, upgrades.SprintSpeedMultiplier) : 1f;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        BirdGroundTraversal.Configure(controller);
        eatingController = GetComponent<ChickEatingController>();
        drinkingController = GetComponent<ChickDrinkingController>();
        waterWading = GetComponent<ChickWaterWading>();
        if (upgrades == null) upgrades = FindFirstObjectByType<PlayerUpgrades>(FindObjectsInactive.Include);

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        movementCamera = Camera.main != null ? Camera.main.transform : null;
        orbitCamera = Camera.main != null ? Camera.main.GetComponent<FreeOrbitThirdPersonCamera>() : null;

        if (animator == null)
        {
            Debug.LogError("ChickPlayerController requires an Animator on the chick or one of its children.", this);
        }
    }

    private void Update()
    {
        if (animator == null)
        {
            return;
        }

        bool inputLocked = GrowthControlsLocked || EggLayingPose;
        Keyboard keyboard = inputLocked ? null : Keyboard.current;
        Vector2 input = ReadMovement(keyboard);
        bool hasMovementInput = input.sqrMagnitude > 0.01f;
        bool rightMouseHeld = !inputLocked && Mouse.current != null && Mouse.current.rightButton.isPressed;
        if (rightMouseHeld && !rightMouseTurning && orbitCamera != null)
        {
            lastHeldCameraYaw = orbitCamera.OrbitYaw;
        }
        rightMouseTurning = rightMouseHeld;
        // Out of stamina: Shift walks until the recover fraction refills.
        bool runHeld = keyboard != null && keyboard.leftShiftKey.isPressed && !sprintExhausted;
        bool jumpPressed = keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
        bool jumpHeld = keyboard != null && keyboard.spaceKey.isPressed;
        bool eatPressed = !inputLocked && eatingController != null && eatingController.ReadEatRequest();
        bool sitHeld = keyboard != null && keyboard.cKey.isPressed;
        bool peckPressed = keyboard != null && keyboard.eKey.wasPressedThisFrame;
        bool peepPressed = keyboard != null && keyboard.pKey.wasPressedThisFrame;
        bool damagePressed = keyboard != null && keyboard.qKey.wasPressedThisFrame;
        bool flapPressed = keyboard != null && keyboard.fKey.wasPressedThisFrame;
        bool downPressed = keyboard != null && keyboard.xKey.wasPressedThisFrame;
        bool grounded = controller.isGrounded;

        peckTimer = Mathf.Max(0f, peckTimer - Time.deltaTime);
        peepTimer = Mathf.Max(0f, peepTimer - Time.deltaTime);
        damageTimer = Mathf.Max(0f, damageTimer - Time.deltaTime);
        downTimer = Mathf.Max(0f, downTimer - Time.deltaTime);
        landingLockTimer = Mathf.Max(0f, landingLockTimer - Time.deltaTime);
        flapTimer = Mathf.Max(0f, flapTimer - Time.deltaTime);
        airJumpFlapTimer = Mathf.Max(0f, airJumpFlapTimer - Time.deltaTime);
        eating = eatingController != null && eatingController.IsEating;
        bool eatBusy = eatingController != null && eatingController.IsBusy;
        bool drinkBusy = IsDrinking;
        bool pecking = peckTimer > 0f;
        bool peeping = peepTimer > 0f;
        bool damaged = damageTimer > 0f;
        bool downNow = downTimer > 0f;
        bool actionLocked = GrowthControlsLocked || eatBusy || drinkBusy || pecking || peeping || damaged || downNow;

        if (jumping)
        {
            airborneTimer += Time.deltaTime;
        }

        if (hasMovementInput || jumping || eatBusy || drinkBusy)
        {
            idleTimer = 0f;
        }
        else
        {
            idleTimer += Time.deltaTime;
        }

        bool canJump = jumpPressed && grounded && !actionLocked && !jumping;
        if (canJump)
        {
            jumping = true;
            airJumpUsed = false;
            GroundJumpCount++;
            airborneTimer = 0f;
            jumpHorizontalVelocity = BuildWorldMovement(input, runHeld);
            verticalVelocity = Mathf.Sqrt(CurrentJumpHeight * -2f * gravity);
            animator.ResetTrigger(Eat);
            animator.SetTrigger(Jump);
            animator.SetBool(ToCrouch, false);
            idleTimer = 0f;
        }
        else if (jumpPressed && jumping && !grounded && !actionLocked && !airJumpUsed &&
                 upgrades != null && upgrades.DoubleJumpUnlocked)
        {
            // "Çift Zıplama": one extra push in the air. Replacing the current (possibly falling)
            // velocity gives a predictable rise; the landing resets the allowance.
            airJumpUsed = true;
            airborneTimer = 0f;
            verticalVelocity = Mathf.Sqrt(CurrentJumpHeight * secondJumpHeightMultiplier * -2f * gravity);
            animator.ResetTrigger(Eat);
            animator.SetTrigger(Jump);
            idleTimer = 0f;
            // A full-body wing push, so the second jump reads as its own move.
            airJumpFlapTimer = airJumpFlapSeconds;
        }

        // "Süzülme": holding Space on the way down of a jump. A fresh press above goes to the
        // double jump first, so both upgrades combine: jump, jump again, then keep holding to glide.
        bool glideUnlocked = upgrades != null && upgrades.GlideUnlocked;
        // Walking off a ledge is a fall without a jump: holding Space on the way down starts a glide too.
        // It becomes an air state like a jump, keeping the current drift; it grants no mid-air jump.
        if (!jumping && !grounded && jumpHeld && !actionLocked && glideUnlocked && verticalVelocity < -2f)
        {
            jumping = true;
            airJumpUsed = true;
            airborneTimer = minJumpAirTime;
            Vector3 drift = controller.velocity;
            drift.y = 0f;
            jumpHorizontalVelocity = drift;
            animator.ResetTrigger(Eat);
        }
        gliding = jumping && !grounded && jumpHeld && !actionLocked && verticalVelocity <= 0f && glideUnlocked;

        // Eating outranks locomotion: accept held-WASD clicks, then apply the short action brake.
        bool canEat = eatPressed && grounded && !actionLocked && !jumping;
        if (canEat && eatingController.TryBeginEat())
        {
            eating = true;
            eatBusy = true;
            actionLocked = true;
            animator.ResetTrigger(Jump);
            animator.SetTrigger(Eat);
            animator.SetBool(ToCrouch, false);
            idleTimer = 0f;
        }
        // No food in reach: a peck at a berry plant (strawberry, blackberry) shakes a berry down onto the ground instead.
        else if (canEat && BerryPlant.TryKnockNear(transform.position, transform.forward, chickenForm ? 1.7f : 1f))
        {
            peckTimer = peckLockSeconds;
            actionLocked = true;
            animator.SetBool(Peck, true);
            animator.SetBool(ToCrouch, false);
            idleTimer = 0f;
        }
        // Nothing to eat but pond water in reach: drink. Holding the button also starts it once the
        // bird stands still at the water, and keeps it sipping.
        else if (drinkingController != null && grounded && !actionLocked && !jumping &&
                 (canEat || (!inputLocked && !hasMovementInput && eatingController.EatHeld)) &&
                 drinkingController.TryBegin())
        {
            drinkBusy = true;
            actionLocked = true;
            animator.ResetTrigger(Jump);
            animator.SetBool(ToCrouch, false);
            idleTimer = 0f;
        }

        bool canStartExtraAction = grounded && !actionLocked && !jumping && !hasMovementInput;
        if (canStartExtraAction && peckPressed)
        {
            peckTimer = peckLockSeconds;
            animator.SetBool(Peck, true);
            idleTimer = 0f;
        }

        if (canStartExtraAction && peepPressed)
        {
            peepTimer = peepLockSeconds;
            animator.SetBool(Peep, true);
            idleTimer = 0f;
        }

        if (canStartExtraAction && damagePressed)
        {
            damageTimer = damageLockSeconds;
            animator.SetTrigger(Damage);
            idleTimer = 0f;
        }

        if (canStartExtraAction && flapPressed)
        {
            flapTimer = 0.8f;
            idleTimer = 0f;
        }

        if (canStartExtraAction && downPressed)
        {
            downTimer = downLockSeconds;
            idleTimer = 0f;
        }

        pecking = peckTimer > 0f;
        peeping = peepTimer > 0f;
        damaged = damageTimer > 0f;
        downNow = downTimer > 0f;
        actionLocked = GrowthControlsLocked || eatBusy || drinkBusy || pecking || peeping || damaged || downNow;

        animator.SetBool(Peck, pecking);
        animator.SetBool(Peep, peeping);
        animator.SetBool(DuringDamage, damaged);

        bool flapping = flapTimer > 0f;
        if (actionLocked || flapping)
        {
            input = Vector2.zero;
            hasMovementInput = false;
        }

        bool moving = hasMovementInput && !actionLocked && !jumping;
        bool running = moving && runHeld;
        UpdateSprintStamina(running, Time.deltaTime);
        running &= !sprintExhausted;

        Vector3 move;
        if (jumping)
        {
            // Keep take-off momentum when the key is released; held or newly
            // pressed WASD can still guide the jump toward a reachable ledge.
            if (hasMovementInput)
            {
                Vector3 requested = BuildWorldMovement(input, runHeld);
                // Gliding carries the flight much farther forward than a plain jump.
                if (gliding) requested *= glideSpeedMultiplier;
                jumpHorizontalVelocity = Vector3.MoveTowards(jumpHorizontalVelocity,
                    requested, airSteeringAcceleration * Time.deltaTime);
            }
            move = jumpHorizontalVelocity;
        }
        else
        {
            move = SteerMovement(BuildWorldMovement(input, running), Time.deltaTime);
        }
        if (move.sqrMagnitude > 0.001f && !rightMouseHeld)
        {
            RotateTowards(move, Time.deltaTime);
        }

        bool groundedAfterMove = ApplyGravityAndMove(move);
        if (jumping && groundedAfterMove && airborneTimer >= minJumpAirTime && verticalVelocity <= 0f)
        {
            jumping = false;
            airJumpUsed = false;
            gliding = false;
            jumpHorizontalVelocity = Vector3.zero;
            airborneTimer = 0f;
            landingLockTimer = landingLockSeconds;
            idleTimer = 0f;
        }

        bool sitting = EggLayingPose || !moving && !actionLocked && !jumping && !flapping &&
                       groundedAfterMove && landingLockTimer <= 0f &&
                       (sitHeld || idleTimer >= sitAfterIdleSeconds);
        IsRunning = running;
        IsSitting = sitting;

        UpdateAnimationStates(
            moving,
            running,
            sitting,
            groundedAfterMove,
            eating,
            pecking,
            peeping,
            damaged,
            flapping,
            downNow);
    }

    private void LateUpdate()
    {
        if (!rightMouseTurning || orbitCamera == null)
        {
            return;
        }

        float cameraYaw = orbitCamera.OrbitYaw;
        if ((eatingController != null && eatingController.IsBusy) || IsDrinking ||
            peckTimer > 0f || peepTimer > 0f || damageTimer > 0f || downTimer > 0f)
        {
            // Keep the last camera angle in sync so an action ending while RMB
            // is held cannot apply the entire held turn in one frame.
            lastHeldCameraYaw = cameraYaw;
            return;
        }
        float turn = Mathf.DeltaAngle(lastHeldCameraYaw, cameraYaw);
        lastHeldCameraYaw = cameraYaw;
        if (Mathf.Abs(turn) > 0.0001f)
        {
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y + turn, 0f);
        }
    }

    private Vector2 ReadMovement(Keyboard keyboard)
    {
        if (keyboard == null)
        {
            return Vector2.zero;
        }

        float horizontal = 0f;
        float vertical = 0f;

        if (keyboard.aKey.isPressed)
        {
            horizontal -= 1f;
        }

        if (keyboard.dKey.isPressed)
        {
            horizontal += 1f;
        }

        if (keyboard.sKey.isPressed)
        {
            vertical -= 1f;
        }

        if (keyboard.wKey.isPressed)
        {
            vertical += 1f;
        }

        return Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f);
    }

    private void UpdateSprintStamina(bool sprinting, float deltaTime)
    {
        float max = SprintSeconds;
        if (DebugInfiniteSprint)
        {
            sprintStamina = max;
            sprintExhausted = false;
            sprintRefillWait = 0f;
            return;
        }
        if (sprintStamina < 0f) sprintStamina = max;
        if (sprinting)
        {
            sprintStamina = Mathf.Max(0f, sprintStamina - deltaTime);
            sprintRefillWait = sprintRefillDelay;
            if (sprintStamina <= 0f) sprintExhausted = true;
        }
        else if (sprintRefillWait > 0f) sprintRefillWait -= deltaTime;
        else sprintStamina = Mathf.Min(max, sprintStamina + max / sprintRefillSeconds * deltaTime);
        sprintStamina = Mathf.Min(sprintStamina, max);
        if (sprintExhausted && sprintStamina >= max * sprintRecoverFraction) sprintExhausted = false;
    }

    private Vector3 BuildWorldMovement(Vector2 input, bool running)
    {
        Vector3 forward = movementCamera != null ? movementCamera.forward : Vector3.forward;
        Vector3 right = movementCamera != null ? movementCamera.right : Vector3.right;

        forward.y = 0f;
        right.y = 0f;

        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.forward;
        }
        else
        {
            forward.Normalize();
        }

        if (right.sqrMagnitude < 0.001f)
        {
            right = Vector3.right;
        }
        else
        {
            right.Normalize();
        }

        float speed = chickenForm
            ? (running ? runSpeed : moveSpeed)
            : (running ? chickRunSpeed : chickMoveSpeed);
        if (running) speed *= SprintMultiplier * (DebugSprintSpeed5x ? 5f : 1f);
        // Pond water slows the stride as it deepens.
        if (waterWading != null) speed *= waterWading.SpeedMultiplier;
        return (right * input.x + forward * input.y) * speed;
    }

    private void RotateTowards(Vector3 movement, float deltaTime)
    {
        Vector3 direction = new Vector3(movement.x, 0f, movement.z);
        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        float currentYaw = transform.eulerAngles.y;
        float targetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        float difference = Mathf.DeltaAngle(currentYaw, targetYaw);
        if (Mathf.Abs(difference) <= turnDeadZone)
        {
            visualYawVelocity = 0f;
            return;
        }

        // Opposite vectors have two equally short arcs. Keep the previous side
        // within a tiny tolerance so floating-point noise cannot flip it.
        if (Mathf.Abs(difference) >= 179.75f)
        {
            difference = Mathf.Abs(difference) * lastTurnSide;
        }
        else
        {
            lastTurnSide = Mathf.Sign(difference);
        }

        float nextYaw = Mathf.SmoothDamp(currentYaw, currentYaw + difference,
            ref visualYawVelocity, visualRotationSmoothTime,
            maxVisualTurnSpeed, Mathf.Max(0f, deltaTime));
        transform.rotation = Quaternion.Euler(0f, nextYaw, 0f);
    }

    private Vector3 SteerMovement(Vector3 desiredMovement, float deltaTime)
    {
        float speed = desiredMovement.magnitude;
        if (speed < 0.001f)
        {
            // Stop immediately and discard angular momentum across idle/jump/action locks.
            steeringActive = false;
            steeringYawVelocity = 0f;
            visualYawVelocity = 0f;
            return Vector3.zero;
        }

        float desiredYaw = Mathf.Atan2(desiredMovement.x, desiredMovement.z) * Mathf.Rad2Deg;
        if (!steeringActive)
        {
            steeringYaw = desiredYaw;
            steeringActive = true;
        }

        float difference = Mathf.DeltaAngle(steeringYaw, desiredYaw);
        if (Mathf.Abs(difference) >= 179.75f)
            difference = Mathf.Abs(difference) * steeringSide;
        else if (Mathf.Abs(difference) > 0.01f)
            steeringSide = Mathf.Sign(difference);

        // Smooth angles rather than vectors: opposite inputs must not cancel speed.
        steeringYaw = Mathf.SmoothDamp(steeringYaw, steeringYaw + difference,
            ref steeringYawVelocity, movementSteeringSmoothTime, 2160f,
            Mathf.Max(0f, deltaTime));
        steeringYaw = Mathf.Repeat(steeringYaw, 360f);
        return Quaternion.Euler(0f, steeringYaw, 0f) * Vector3.forward * speed;
    }

    private void UpdateAnimationStates(
        bool moving,
        bool running,
        bool sitting,
        bool grounded,
        bool eatingNow,
        bool pecking,
        bool peeping,
        bool damaged,
        bool flapping,
        bool downNow)
    {
        animator.SetBool(ToMove, moving);
        animator.SetBool(ToCrouch, sitting);
        animator.SetBool(ToLanding, grounded);
        animator.SetFloat(Speed, moving ? (running ? 2f : 1f) : 0f);
        // Run clip plays faster in step with the upgraded sprint so the feet do not slide.
        animator.SetFloat(RunPlaybackSpeed, SprintMultiplier);
        animator.SetBool(Peck, pecking);
        animator.SetBool(Peep, peeping);
        animator.SetBool(DuringDamage, damaged);

        string baseState;
        if (jumping)
        {
            // The mid-air jump's wing push and gliding use the full-body flap, so both read
            // differently from a plain jump.
            baseState = airJumpFlapTimer > 0f || gliding ? "flapping" : verticalVelocity > 0f ? "jump" : "jump_descent";
        }
        else if (damaged)
        {
            baseState = "damage";
        }
        else if (downNow)
        {
            baseState = "down";
        }
        else if (IsDrinking)
        {
            baseState = drinkingController.BaseState;
        }
        else if (eatingNow)
        {
            baseState = "eat";
        }
        else if (pecking)
        {
            baseState = "peck";
        }
        else if (peeping)
        {
            baseState = "peep";
        }
        else if (flapping)
        {
            baseState = "flapping";
        }
        else if (sitting)
        {
            baseState = "crouch";
        }
        else if (moving)
        {
            baseState = running ? "run" : "move";
        }
        else
        {
            baseState = "idle";
        }

        EnsureState(baseState, 0);

        string wingState;
        if (jumping)
        {
            wingState = airJumpFlapTimer > 0f ? "wing_flapping" : airborneTimer < 0.2f ? "wing_down" : "wing_flapping";
        }
        else if (damaged)
        {
            wingState = "wing_damage";
        }
        else if (flapping)
        {
            wingState = "wing_flapping";
        }
        else
        {
            wingState = "wing_idle";
        }

        EnsureState(wingState, 1);
    }

    private bool ApplyGravityAndMove(Vector3 horizontalMovement)
    {
        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -1.5f;
        }

        verticalVelocity += gravity * Time.deltaTime;
        // Gliding brakes a fast fall smoothly down to the glide speed, then holds it there.
        if (gliding && verticalVelocity < -glideFallSpeed)
            verticalVelocity = Mathf.MoveTowards(verticalVelocity, -glideFallSpeed, glideBrake * Time.deltaTime);
        Vector3 movement = horizontalMovement;
        movement.y = verticalVelocity;

        controller.Move(movement * Time.deltaTime);
        bool grounded = controller.isGrounded;
        animator.SetBool(ToLanding, grounded);
        return grounded;
    }

    private void EnsureState(string stateName, int layerIndex)
    {
        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(layerIndex);
        if (currentState.IsName(stateName))
        {
            return;
        }

        if (animator.IsInTransition(layerIndex) &&
            animator.GetNextAnimatorStateInfo(layerIndex).IsName(stateName))
        {
            return;
        }

        float transitionDuration = stateName == "jump" || stateName == "jump_descent"
            ? 0.18f
            : stateName == "eat" ? 0.05f : 0.16f;

        animator.CrossFadeInFixedTime(stateName, transitionDuration, layerIndex, 0f);
    }
}
