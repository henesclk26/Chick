using System;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class FreeOrbitThirdPersonCamera : MonoBehaviour
{
    public enum OrbitMode
    {
        HoldRightMouseToTurnChick,
        AlwaysOrbit
    }

    public enum CameraProfile
    {
        Chick,
        Chicken,
        Large
    }

    [Serializable]
    private struct CameraProfileSettings
    {
        public float distance;
        public float targetHeight;
        public float fieldOfView;
    }

    [Header("FOLLOW")]
    [SerializeField] private Transform target;
    [SerializeField] private Transform playerRoot;
    [SerializeField] private float targetHeight = 0f;
    [SerializeField] private float targetForwardOffset = 0f;
    [SerializeField] private float followDamping = 0.12f;
    [SerializeField] private float verticalFollowDamping = 0.18f;
    [SerializeField] private float maximumVerticalLag = 0.12f;
    [SerializeField] private float framingTilt = 2f;

    [Header("ORBIT")]
    [SerializeField] private OrbitMode orbitMode = OrbitMode.HoldRightMouseToTurnChick;
    [SerializeField] private float horizontalSensitivity = 0.12f;
    [SerializeField] private float verticalSensitivity = 0.10f;
    [SerializeField] private bool invertY = false;
    [SerializeField] private float minimumPitch = -18f;
    [SerializeField] private float maximumPitch = 52f;
    [SerializeField] private float orbitInputSmoothTime = 0.035f;
    [SerializeField] private float initialYaw = 0f;
    [SerializeField] private float initialPitch = 8f;

    [Header("SMART RECENTER")]
    [SerializeField] private bool enableSmartRecenter = false;
    [SerializeField, Min(0f)] private float manualInputDelay = 1.5f;
    [SerializeField, Range(10f, 179f)] private float recenterActivationAngle = 60f;
    [SerializeField, Range(1f, 10f)] private float recenterStopAngle = 6f;
    [SerializeField, Min(0.05f)] private float stableDirectionTime = 0.40f;
    [SerializeField, Min(0.01f)] private float movementDirectionFilterTime = 0.30f;
    [SerializeField, Min(0.1f)] private float recenterSmoothTime = 1f;
    [SerializeField, Min(0.01f)] private float recenterMovementThreshold = 0.15f;

    [Header("CAMERA ZOOM")]
    [SerializeField] private float defaultDistance = 0.83f;
    [SerializeField] private float minimumDistance = 0.45f;
    [SerializeField] private float maximumDistance = 0.83f;
    [SerializeField] private float zoomSensitivity = 0.04f;
    [SerializeField] private float zoomSmoothTime = 0.10f;

    [Header("LENS")]
    [SerializeField] private float fieldOfView = 58f;
    [SerializeField] private float nearClipPlane = 0.02f;
    [SerializeField] private float farClipPlane = 1000f;

    [Header("COLLISION")]
    [SerializeField] private LayerMask collisionMask;
    [SerializeField] private float collisionRadius = 0.055f;
    [SerializeField] private float collisionPadding = 0.025f;
    [SerializeField] private float minimumCollisionDistance = 0.18f;
    [SerializeField] private float collisionDamping = 0.045f;
    [SerializeField] private float collisionRecoveryDamping = 0.14f;

    [Header("CHARACTER CAMERA PROFILES")]
    [SerializeField] private CameraProfile activeProfile = CameraProfile.Chick;
    [SerializeField] private CameraProfileSettings chickProfile = new CameraProfileSettings
    {
        distance = 0.78f,
        targetHeight = 0f,
        fieldOfView = 58f
    };
    [SerializeField] private CameraProfileSettings chickenProfile = new CameraProfileSettings
    {
        distance = 0.95f,
        targetHeight = 0.05f,
        fieldOfView = 60f
    };
    [SerializeField] private CameraProfileSettings largeProfile = new CameraProfileSettings
    {
        distance = 1.35f,
        targetHeight = 0.10f,
        fieldOfView = 62f
    };

    private Camera cameraComponent;
    private Vector3 followPosition;
    private Vector3 followVelocity;
    private Vector3 previousFocusPoint;
    private float verticalFollowVelocity;
    private float profileDistance;
    private float currentDistance;
    private float growthFramingDistance, growthFramingHeight;

    // Temporary framing never modifies the player's saved zoom or the collision mask.
    public void SetGrowthFraming(float minimumDistance, float extraHeight)
    {
        growthFramingDistance = Mathf.Max(0f, minimumDistance);
        growthFramingHeight = Mathf.Clamp(extraHeight, 0f, .3f);
    }
    private float distanceVelocity;
    private float currentTargetHeight;
    private float currentFieldOfView;
    private float userDesiredDistance;
    public float UserDesiredDistance => userDesiredDistance;
    public float OrbitYaw => yaw;
    public float OrbitPitch => pitch;

    public void SetZoomDistance(float distance)
    {
        userDesiredDistance = ClampDistance(distance);
    }

    public void SetOrbitAngles(float savedYaw, float savedPitch)
    {
        yaw = savedYaw;
        pitch = Mathf.Clamp(savedPitch, minimumPitch, maximumPitch);
        targetYaw = yaw;
        targetPitch = pitch;
        ResetSmartRecenter();
    }

    // Used after loading/teleporting the player so the disabled follow script's
    // rendered camera is already at the saved character position before fades open.
    public void SnapToTarget()
    {
        currentTargetHeight = GetProfileSettings(activeProfile).targetHeight;
        profileDistance = ClampDistance(userDesiredDistance);
        currentDistance = profileDistance;
        distanceVelocity = 0f;
        InitializeCameraTransform();
    }

    // Menu setting only; a multiplier of 1 preserves the approved sensitivity.
    public void SetLookSensitivity(float multiplier)
    {
        horizontalSensitivity = 0.12f * Mathf.Clamp(multiplier, 0.5f, 2f);
        verticalSensitivity = 0.10f * Mathf.Clamp(multiplier, 0.5f, 2f);
    }
    private float profileHeightVelocity;
    private float profileFovVelocity;
    private float yaw;
    private float pitch;
    private float targetYaw;
    private float targetPitch;
    private bool initialized;
    private bool cursorCaptured;
    private CursorLockMode previousCursorLockState;
    private bool previousCursorVisible;

    public enum SmartRecenterState
    {
        Disabled, ManualOrbit, WaitingForManualDelay, WaitingForMovement,
        WaitingForStableDirection, ControlLocked, Eating, Ready, Recentering, FeedbackHold
    }
    public SmartRecenterState RecenterState { get; private set; }
    public bool EnableSmartRecenter
    {
        get => enableSmartRecenter;
        set { if (enableSmartRecenter != value) { enableSmartRecenter = value; ResetSmartRecenter(); } }
    }
    private CharacterController recenterBody;
    private ChickPlayerController recenterPlayer;
    private ChickEatingController recenterEating;
    private float manualWait, stableTravelTime, headingAnchor, stableHeading, headingVelocity;
    private float recenterGoal, episodeHeading, recenterYawVelocity, episodeTime, heldHeading;
    private float lastRecenterSide = 1f;
    private bool headingKnown, recenterActive, feedbackHold, manualWasActive;

    private void Awake()
    {
        cameraComponent = GetComponent<Camera>();

        if (target == null)
        {
            GameObject cameraTarget = GameObject.Find("CameraTarget");
            if (cameraTarget != null)
            {
                target = cameraTarget.transform;
            }
        }

        if (playerRoot == null && target != null)
        {
            playerRoot = target.root;
        }
        BindRecenterSources();

        if (collisionMask.value == 0)
        {
            int cameraBlockerLayer = LayerMask.NameToLayer("CameraBlocker");
            if (cameraBlockerLayer >= 0)
            {
                collisionMask = 1 << cameraBlockerLayer;
            }
        }

        yaw = initialYaw;
        pitch = Mathf.Clamp(initialPitch, minimumPitch, maximumPitch);
        targetYaw = yaw;
        targetPitch = pitch;

        CameraProfileSettings profile = GetProfileSettings(activeProfile);
        userDesiredDistance = ClampDistance(defaultDistance);
        currentDistance = userDesiredDistance;
        profileDistance = currentDistance;
        currentTargetHeight = profile.targetHeight;
        currentFieldOfView = Mathf.Clamp(
            profile.fieldOfView > 0f ? profile.fieldOfView : fieldOfView,
            45f,
            75f);

        ConfigureLens();
        InitializeCameraTransform();
    }

    private void OnDisable()
    {
        RestoreCursor();
        ResetSmartRecenter();
    }

    private void OnEnable() => ResetSmartRecenter();

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            RestoreCursor();
        }
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        if (!initialized)
        {
            InitializeCameraTransform();
        }

        bool orbitInputActive = IsOrbitInputActive();
        UpdateCursorState(orbitInputActive);
        UpdateOrbit(orbitInputActive);
        UpdateOrbitSmoothing(orbitInputActive);
        // Smart Recenter experiment disconnected: mouse input is the sole yaw/pitch owner.
        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (InputSystem.settings.scrollDeltaBehavior ==
                InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange &&
                (Application.platform == RuntimePlatform.WindowsEditor ||
                 Application.platform == RuntimePlatform.WindowsPlayer))
                scroll /= 120f;
            ApplyScrollZoom(scroll);
        }
        UpdateProfileValues();

        Vector3 focusPoint = GetFocusPoint();
        // Carry locomotion into the pivot immediately. Damping only corrects
        // residual offsets, rather than allowing the player to catch the camera.
        Vector3 translation = focusPoint - previousFocusPoint;
        translation.y = 0f;
        followPosition += translation;
        previousFocusPoint = focusPoint;
        float followY = Mathf.SmoothDamp(followPosition.y, focusPoint.y,
            ref verticalFollowVelocity, Mathf.Max(0.01f, verticalFollowDamping));
        // Keep a jumping chick inside the frame even with soft vertical tracking.
        followY = Mathf.Clamp(followY, focusPoint.y - maximumVerticalLag,
            focusPoint.y + maximumVerticalLag);
        followPosition = Vector3.SmoothDamp(
            followPosition,
            focusPoint,
            ref followVelocity,
            Mathf.Max(0.01f, followDamping));
        followPosition.y = followY;
        followVelocity.y = 0f;

        float desiredDistance = Mathf.Max(ClampDistance(profileDistance), growthFramingDistance);
        float collisionDistance = ResolveCollisionDistance(followPosition, desiredDistance);
        bool obstructed = collisionDistance < desiredDistance - 0.0001f;
        float distanceDamping = obstructed ? collisionRecoveryDamping : zoomSmoothTime;

        currentDistance = Mathf.SmoothDamp(
            currentDistance,
            collisionDistance,
            ref distanceVelocity,
            Mathf.Max(0.01f, distanceDamping));
        // Never smooth through an obstacle; only recovery may trail the clear distance.
        if (obstructed && currentDistance > collisionDistance)
        {
            currentDistance = collisionDistance;
            distanceVelocity = 0f;
        }

        Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desiredPosition = followPosition +
                                  orbitRotation * (Vector3.back * currentDistance);

        transform.position = desiredPosition;
        transform.rotation = orbitRotation * Quaternion.Euler(-framingTilt, 0f, 0f);

        ConfigureLens();
    }

    public void SetCameraProfile(CameraProfile profile)
    {
        if (activeProfile == profile) return;
        float oldScale = DistanceScale;
        activeProfile = profile;
        userDesiredDistance = ClampDistance(userDesiredDistance * DistanceScale / oldScale);
    }

    private float DistanceScale => activeProfile == CameraProfile.Chick ? 1f :
        Mathf.Max(1f, GetProfileSettings(activeProfile).distance / Mathf.Max(.05f, defaultDistance));

    private void ApplyScrollZoom(float notches)
    {
        if (notches != 0f)
            SetZoomDistance(userDesiredDistance - notches * zoomSensitivity);
    }

    public void SetCharacterStage(CameraProfile profile)
    {
        SetCameraProfile(profile);
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        if (target != null && playerRoot == null)
        {
            playerRoot = target.root;
        }
        BindRecenterSources();
        ResetSmartRecenter();
    }

    private void BindRecenterSources()
    {
        recenterBody = playerRoot != null ? playerRoot.GetComponent<CharacterController>() : null;
        recenterPlayer = playerRoot != null ? playerRoot.GetComponent<ChickPlayerController>() : null;
        recenterEating = playerRoot != null ? playerRoot.GetComponent<ChickEatingController>() : null;
    }

    private void ResetSmartRecenter()
    {
        manualWait = manualInputDelay;
        stableTravelTime = headingVelocity = recenterYawVelocity = episodeTime = 0f;
        headingKnown = recenterActive = feedbackHold = manualWasActive = false;
        RecenterState = enableSmartRecenter ? SmartRecenterState.WaitingForManualDelay : SmartRecenterState.Disabled;
    }

    // Camera-only observer: never writes to a player Transform, movement reference or input.
    // All automatic yaw is applied before the unchanged collision/final-pose pipeline.
    private void TickSmartRecenter(float deltaTime, bool manual, Vector3 worldVelocity, bool controlsAvailable, bool eating)
    {
        if (!enableSmartRecenter)
        {
            ResetSmartRecenter();
            return; // OFF leaves the entire pre-existing orbit output untouched.
        }
        if (manual)
        {
            ResetSmartRecenter();
            manualWasActive = true;
            RecenterState = SmartRecenterState.ManualOrbit;
            return;
        }
        if (deltaTime <= 0f) return;
        if (manualWasActive) { manualWait = manualInputDelay; manualWasActive = false; }
        else manualWait = Mathf.Max(0f, manualWait - deltaTime);

        worldVelocity.y = 0f;
        if (!controlsAvailable || eating || worldVelocity.sqrMagnitude < recenterMovementThreshold * recenterMovementThreshold)
        {
            recenterActive = feedbackHold = headingKnown = false;
            stableTravelTime = headingVelocity = recenterYawVelocity = 0f;
            RecenterState = !controlsAvailable ? SmartRecenterState.ControlLocked :
                eating ? SmartRecenterState.Eating : SmartRecenterState.WaitingForMovement;
            return;
        }

        float rawHeading = Mathf.Atan2(worldVelocity.x, worldVelocity.z) * Mathf.Rad2Deg;
        if (!headingKnown)
        {
            stableHeading = headingAnchor = rawHeading;
            stableTravelTime = headingVelocity = 0f;
            headingKnown = true;
        }
        if (Mathf.Abs(Mathf.DeltaAngle(headingAnchor, rawHeading)) > 8f)
        {
            headingAnchor = rawHeading;
            stableTravelTime = 0f;
        }
        else stableTravelTime += deltaTime;
        float headingDifference = Mathf.DeltaAngle(stableHeading, rawHeading);
        if (Mathf.Abs(headingDifference) >= 179.5f) headingDifference = 180f * lastRecenterSide;
        stableHeading = Mathf.SmoothDamp(stableHeading, stableHeading + headingDifference,
            ref headingVelocity, movementDirectionFilterTime, Mathf.Infinity, deltaTime);

        if (manualWait > 0f) { RecenterState = SmartRecenterState.WaitingForManualDelay; return; }

        if (recenterActive && (Mathf.Abs(Mathf.DeltaAngle(episodeHeading, rawHeading)) > 10f ||
            episodeTime > Mathf.Max(4f, recenterSmoothTime * 6f)))
        {
            // Includes camera-relative steering feedback. Do not chase a moving goal.
            recenterActive = false;
            recenterYawVelocity = 0f;
            feedbackHold = true;
            heldHeading = rawHeading;
            stableTravelTime = 0f;
        }
        if (feedbackHold)
        {
            // Stability alone is NOT enough: otherwise a hold/correct/hold loop would spiral.
            // Rearm on a meaningful new travel direction, or on stop/manual input above.
            if (Mathf.Abs(Mathf.DeltaAngle(heldHeading, rawHeading)) < 25f || stableTravelTime < stableDirectionTime)
            { RecenterState = SmartRecenterState.FeedbackHold; return; }
            feedbackHold = false;
        }
        if (!recenterActive)
        {
            if (stableTravelTime < stableDirectionTime || Mathf.Abs(Mathf.DeltaAngle(stableHeading, rawHeading)) > 6f)
            { RecenterState = SmartRecenterState.WaitingForStableDirection; return; }
            float difference = Mathf.DeltaAngle(yaw, stableHeading);
            if (Mathf.Abs(difference) <= recenterActivationAngle)
            { RecenterState = SmartRecenterState.Ready; return; }
            if (Mathf.Abs(difference) >= 179.5f) difference = 180f * lastRecenterSide;
            else lastRecenterSide = Mathf.Sign(difference);
            // Snapshot the stable WORLD heading for this short episode, not a movement reference.
            recenterGoal = yaw + difference;
            episodeHeading = rawHeading;
            recenterYawVelocity = episodeTime = 0f;
            recenterActive = true;
        }
        episodeTime += deltaTime;
        RecenterState = SmartRecenterState.Recentering;
        yaw = Mathf.SmoothDamp(yaw, recenterGoal, ref recenterYawVelocity,
            recenterSmoothTime, 90f, deltaTime);
        targetYaw = yaw;
        if (Mathf.Abs(recenterGoal - yaw) <= recenterStopAngle && Mathf.Abs(recenterYawVelocity) < 3f)
        {
            recenterActive = false;
            recenterYawVelocity = 0f;
            RecenterState = SmartRecenterState.Ready;
        }
    }

    private bool IsOrbitInputActive()
    {
        // Both modes retain the existing free orbit. RMB additionally turns
        // the chick through ChickPlayerController without freezing this camera.
        return orbitMode == OrbitMode.AlwaysOrbit || Mouse.current != null;
    }

    private void UpdateOrbit(bool orbitInputActive)
    {
        Mouse mouse = Mouse.current;
        if (mouse != null && orbitInputActive)
        {
            Vector2 delta = mouse.delta.ReadValue();

            // Mouse delta is already frame-based input; do not multiply by Time.deltaTime.
            targetYaw += delta.x * horizontalSensitivity;
            float pitchDirection = invertY ? 1f : -1f;
            targetPitch += delta.y * verticalSensitivity * pitchDirection;
            targetPitch = Mathf.Clamp(targetPitch, minimumPitch, maximumPitch);
        }

    }

    private void UpdateOrbitSmoothing(bool orbitInputActive)
    {
        if (!orbitInputActive)
        {
            targetYaw = yaw;
            targetPitch = pitch;
            return;
        }
        // Short input filter, without spring velocity or additional transform damping.
        float blend = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.001f, orbitInputSmoothTime));
        yaw += (targetYaw - yaw) * blend;
        pitch = Mathf.Lerp(pitch, targetPitch, blend);
        pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);
    }

    private void UpdateCursorState(bool orbitInputActive)
    {
        if (orbitInputActive && !cursorCaptured)
        {
            previousCursorLockState = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            cursorCaptured = true;
        }
        else if (!orbitInputActive && cursorCaptured)
        {
            RestoreCursor();
        }
    }

    private void RestoreCursor()
    {
        if (!cursorCaptured)
        {
            return;
        }

        Cursor.lockState = previousCursorLockState;
        Cursor.visible = previousCursorVisible;
        cursorCaptured = false;
    }

    private void UpdateProfileValues()
    {
        CameraProfileSettings profile = GetProfileSettings(activeProfile);
        float profileFov = profile.fieldOfView > 0f
            ? profile.fieldOfView
            : fieldOfView;

        profileDistance = ClampDistance(userDesiredDistance);

        currentTargetHeight = Mathf.SmoothDamp(
            currentTargetHeight,
            profile.targetHeight,
            ref profileHeightVelocity,
            0.20f);

        currentFieldOfView = Mathf.SmoothDamp(
            currentFieldOfView,
            Mathf.Clamp(profileFov, 45f, 75f),
            ref profileFovVelocity,
            0.20f);
    }

    private CameraProfileSettings GetProfileSettings(CameraProfile profile)
    {
        switch (profile)
        {
            case CameraProfile.Chicken:
                return chickenProfile;
            case CameraProfile.Large:
                return largeProfile;
            default:
                CameraProfileSettings chick = chickProfile;
                if (chick.distance <= 0f)
                {
                    chick.distance = defaultDistance;
                }

                if (chick.fieldOfView <= 0f)
                {
                    chick.fieldOfView = fieldOfView;
                }

                return chick;
        }
    }

    private Vector3 GetFocusPoint()
    {
        Vector3 focusPoint = target.position;
        focusPoint += Vector3.up * (targetHeight + currentTargetHeight + growthFramingHeight);
        focusPoint += target.forward * targetForwardOffset;
        return focusPoint;
    }

    private float ResolveCollisionDistance(Vector3 origin, float desiredDistance)
    {
        desiredDistance = Mathf.Max(ClampDistance(desiredDistance), growthFramingDistance);

        Vector3 direction = Quaternion.Euler(pitch, yaw, 0f) * Vector3.back;
        if (Physics.SphereCast(
                origin,
                collisionRadius,
                direction,
                out RaycastHit hit,
                desiredDistance,
                collisionMask,
                QueryTriggerInteraction.Ignore))
        {
            return Mathf.Clamp(
                hit.distance - collisionPadding,
                Mathf.Min(minimumCollisionDistance, desiredDistance),
                desiredDistance);
        }

        return desiredDistance;
    }

    private float ClampDistance(float distance)
    {
        return Mathf.Clamp(
            distance,
            Mathf.Max(0.05f, minimumDistance * DistanceScale),
            Mathf.Max(minimumDistance, maximumDistance) * DistanceScale);
    }

    private void ConfigureLens()
    {
        if (cameraComponent == null)
        {
            return;
        }

        cameraComponent.fieldOfView = currentFieldOfView;
        cameraComponent.nearClipPlane = nearClipPlane;
        cameraComponent.farClipPlane = farClipPlane;
    }

    private void InitializeCameraTransform()
    {
        if (target == null)
        {
            return;
        }

        Vector3 focusPoint = GetFocusPoint();
        followPosition = focusPoint;
        previousFocusPoint = focusPoint;
        followVelocity = Vector3.zero;
        verticalFollowVelocity = 0f;

        Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offset = orbitRotation * (Vector3.back * currentDistance);
        transform.position = focusPoint + offset;

        Vector3 lookDirection = focusPoint - transform.position;
        if (lookDirection.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(
                lookDirection.normalized,
                Vector3.up);
        }

        transform.rotation = orbitRotation * Quaternion.Euler(-framingTilt, 0f, 0f);
        initialized = true;
    }
}

