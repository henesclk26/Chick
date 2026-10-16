using UnityEngine;

// Rocks a rocking chair gently back and forth on its runners and keeps someone seated in it. The chair rolls on the
// arc of its runners (it tilts about the arc's centre and the contact point travels with it), so it rocks like a real
// one instead of pivoting on a pin. The sitter is a Humanoid character without an animation controller (Synty's
// SM_Chr_Farmer_Male_Old_01): it is parented to the chair and posed seated by turning its bones from the T-pose, so it
// rocks along with the chair. Seat() is run by the editor setup, so the sitter is seated in the scene as well and the
// pose is stored here; play mode only reapplies it.
[DisallowMultipleComponent]
public sealed class RockingChair : MonoBehaviour
{
    [SerializeField, Tooltip("Humanoid character sitting in the chair (its Animator needs no controller).")]
    private Animator sitter;

    [Header("Rocking")]
    [SerializeField, Tooltip("Tilt each way, in degrees.")]
    private float rockAngle = 5f;
    [SerializeField, Tooltip("Seconds for one full back-and-forth.")]
    private float period = 3.4f;
    [SerializeField, Tooltip("Radius of the runners' curve, in metres (SM_Prop_Chair_Rocking_01: about 0.65).")]
    private float runnerRadius = 0.65f;

    [Header("Seat")]
    [SerializeField, Tooltip("Where the sitter's hips go, in the chair's local space (seat top 0.52 m up, near the back).")]
    private Vector3 hipsOnSeat = new Vector3(0f, 0.63f, -0.1f);
    [SerializeField, Tooltip("How far the sitter leans back against the backrest, in degrees.")]
    private float recline = 12f;
    [SerializeField, Tooltip("Fallback wrist spot on the armrests, chair space (x mirrored for the left hand), used only " +
                             "if the armrests cannot be measured from the chair mesh.")]
    private Vector3 wristOnArmrest = new Vector3(0.31f, 0.77f, 0.1f);
    [SerializeField, Tooltip("Height of the wrist bone above the armrest top: about half a hand's thickness.")]
    private float wristAboveArmrest = 0.03f;
    [SerializeField, Tooltip("How far back from the armrest's front end the wrist sits, so the fingers end at the front.")]
    private float wristBackFromEnd = 0.13f;

    [SerializeField, HideInInspector] private Transform[] poseBones = new Transform[0];
    [SerializeField, HideInInspector] private Quaternion[] poseRotations = new Quaternion[0];   // in the sitter's space

    private Vector3 restPosition;
    private Quaternion restRotation;
    private float phase;

    // Humanoid bones posed for sitting, parents before children.
    private static readonly HumanBodyBones[] Posed =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
        HumanBodyBones.Neck, HumanBodyBones.Head,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
        HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
    };

    private void Start()
    {
        restPosition = transform.position;
        restRotation = transform.rotation;
        phase = Random.value * period;
        // Set up without the editor menu: take the sitter's current pose as its T-pose.
        if (sitter != null && poseBones.Length == 0)
        {
            Transform body = sitter.transform;
            Seat(sitter, bone => Quaternion.Inverse(body.rotation) * bone.rotation);
        }
    }

    private void LateUpdate()
    {
        // Rolling on the runners: tilt about the centre of their curve while that centre moves along the ground by
        // the arc length rolled, so the runners never slide or sink.
        float angle = rockAngle * Mathf.Sin((Time.time + phase) / period * Mathf.PI * 2f);
        Vector3 up = restRotation * Vector3.up, forward = restRotation * Vector3.forward, right = restRotation * Vector3.right;
        Vector3 centre = restPosition + up * runnerRadius;
        Quaternion tilt = Quaternion.AngleAxis(angle, right);
        Vector3 rolled = centre + forward * (runnerRadius * angle * Mathf.Deg2Rad);
        transform.SetPositionAndRotation(rolled + tilt * (restPosition - centre), tilt * restRotation);

        ApplyPose();
    }

    /// <summary>
    /// Parents the character to the chair, poses it seated and puts its hips on the seat. restInRoot gives each bone's
    /// T-pose rotation relative to the character's root (the editor reads it from the prefab, so reseating never
    /// stacks the pose on itself).
    /// </summary>
    public void Seat(Animator character, System.Func<Transform, Quaternion> restInRoot)
    {
        sitter = character;
        Transform body = sitter.transform;
        body.SetParent(transform, true);
        body.localRotation = Quaternion.identity;   // Synty characters and this chair both face +Z

        var bones = new System.Collections.Generic.List<Transform>();
        var rotations = new System.Collections.Generic.List<Quaternion>();
        foreach (HumanBodyBones bone in Posed)
        {
            Transform t = sitter.GetBoneTransform(bone);
            if (t == null) continue;
            bones.Add(t);
            rotations.Add(SeatedTurn(bone) * restInRoot(t));
        }
        poseBones = bones.ToArray();
        poseRotations = rotations.ToArray();
        ApplyPose();

        Transform hips = sitter.GetBoneTransform(HumanBodyBones.Hips);
        if (hips != null) body.position += transform.TransformPoint(hipsOnSeat) - hips.position;

        GripArmrests(restInRoot);
        // Keep the finished pose (arms and fingers included) to reapply every frame.
        var posed = new System.Collections.Generic.List<Transform>(poseBones);
        foreach (HumanBodyBones finger in Fingers)
        {
            Transform t = sitter.GetBoneTransform(finger);
            if (t != null) posed.Add(t);
        }
        poseBones = posed.ToArray();
        poseRotations = new Quaternion[poseBones.Length];
        for (int i = 0; i < poseBones.Length; i++) poseRotations[i] = Quaternion.Inverse(body.rotation) * poseBones[i].rotation;
    }

    // Finger joints, root to tip for each finger, both hands.
    private static readonly HumanBodyBones[] Fingers =
    {
        HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
        HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
        HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
        HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
        HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
        HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
        HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
        HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
        HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
        HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
    };

    // Each hand on its armrest: the arm reaches there with the elbow out and back, and the hand lies flat along the
    // armrest with straight fingers (curled fingers looked clawed).
    private void GripArmrests(System.Func<Transform, Quaternion> restInRoot)
    {
        Transform root = sitter.transform;
        foreach (bool left in new[] { true, false })
        {
            Transform upper = sitter.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            Transform lower = sitter.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Transform hand = sitter.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (upper == null || lower == null || hand == null) continue;
            float side = left ? -1f : 1f;
            Vector3 wrist = transform.TransformPoint(MeasureArmrest(side, out Vector3 spot)
                ? spot
                : new Vector3(side * wristOnArmrest.x, wristOnArmrest.y, wristOnArmrest.z));
            Vector3 elbowHint = upper.position + root.TransformDirection(new Vector3(side * 0.4f, -0.25f, -0.35f));
            SolveTwoBone(upper, lower, hand, wrist, elbowHint);

            // T-pose hands point out sideways, palm down: turn them to point forward, tipped a touch down the armrest.
            Quaternion along = Quaternion.AngleAxis(6f, Vector3.right) * Quaternion.AngleAxis(left ? 90f : -90f, Vector3.up);
            hand.rotation = root.rotation * along * restInRoot(hand);
            foreach (HumanBodyBones finger in Fingers)
            {
                Transform t = sitter.GetBoneTransform(finger);
                if (t == null || !t.IsChildOf(hand)) continue;
                t.localRotation = Quaternion.Inverse(restInRoot(t.parent)) * restInRoot(t);   // straight, as in the T-pose
            }
        }
    }

    // The wrist spot on one armrest, from the chair's own mesh: the top of the armrest (highest point along the chair's
    // side, forward of the seat's middle and below the backrest top) and its front end.
    private bool MeasureArmrest(float side, out Vector3 spot)
    {
        spot = default;
        var filter = GetComponentInChildren<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return false;
        Vector3[] vertices;
        try { vertices = filter.sharedMesh.vertices; }
        catch (System.Exception) { return false; }   // mesh not readable in a build
        float top = float.MinValue, front = float.MinValue, x = 0f;
        foreach (Vector3 v in vertices)
        {
            Vector3 p = transform.InverseTransformPoint(filter.transform.TransformPoint(v));
            if (p.x * side < 0.22f || p.z < 0f || p.y < 0.6f || p.y > 0.9f) continue;
            if (p.y > top) { top = p.y; x = p.x; }
            front = Mathf.Max(front, p.z);
        }
        if (top == float.MinValue) return false;
        spot = new Vector3(x, top + wristAboveArmrest, front - wristBackFromEnd);
        return true;
    }

    // Two-bone reach: turns upper and lower so that end lands on target, bending at the joint towards hint.
    private static void SolveTwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hint)
    {
        Vector3 a = upper.position;
        float ab = Vector3.Distance(a, lower.position), bc = Vector3.Distance(lower.position, end.position);
        Vector3 toTarget = target - a;
        float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(ab - bc) + 1e-3f, ab + bc - 1e-3f);
        Vector3 dir = toTarget.normalized;
        Vector3 bend = Vector3.ProjectOnPlane(hint - a, dir).normalized;
        float cosA = Mathf.Clamp((ab * ab + d * d - bc * bc) / (2f * ab * d), -1f, 1f);
        Vector3 joint = a + dir * (ab * cosA) + bend * (ab * Mathf.Sqrt(1f - cosA * cosA));
        upper.rotation = Quaternion.FromToRotation(lower.position - a, joint - a) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(end.position - lower.position, a + dir * d - lower.position) * lower.rotation;
    }

    // The turn from the T-pose to sitting, in the character's own space (+Z forward, +X right).
    private Quaternion SeatedTurn(HumanBodyBones bone)
    {
        // Tips a bone about the character's right axis: one pointing up leans back, one pointing down swings forward.
        Quaternion Pitch(float degrees) => Quaternion.AngleAxis(-degrees, Vector3.right);
        switch (bone)
        {
            case HumanBodyBones.Hips: return Pitch(recline * 0.3f);
            case HumanBodyBones.Spine: return Pitch(recline * 0.6f);
            case HumanBodyBones.Chest:
            case HumanBodyBones.UpperChest: return Pitch(recline);
            case HumanBodyBones.Neck: return Pitch(recline * 0.5f);
            case HumanBodyBones.Head: return Pitch(recline * 0.2f);
            // Thighs forward along the seat, shins hanging down a little forward of upright, feet flat.
            case HumanBodyBones.LeftUpperLeg:
            case HumanBodyBones.RightUpperLeg: return Pitch(88f);
            case HumanBodyBones.LeftLowerLeg:
            case HumanBodyBones.RightLowerLeg: return Pitch(10f);
            case HumanBodyBones.LeftFoot:
            case HumanBodyBones.RightFoot: return Quaternion.identity;
            // Arms down at the sides and a little forward, forearms resting along the armrests.
            case HumanBodyBones.LeftUpperArm: return Pitch(22f) * Quaternion.AngleAxis(76f, Vector3.forward);
            case HumanBodyBones.RightUpperArm: return Pitch(22f) * Quaternion.AngleAxis(-76f, Vector3.forward);
            case HumanBodyBones.LeftLowerArm:
            case HumanBodyBones.LeftHand: return Pitch(85f) * Quaternion.AngleAxis(76f, Vector3.forward);
            case HumanBodyBones.RightLowerArm:
            case HumanBodyBones.RightHand: return Pitch(85f) * Quaternion.AngleAxis(-76f, Vector3.forward);
            default: return Quaternion.identity;
        }
    }

    // Applied every frame so nothing (an Animator, a reload) can leave the sitter standing up.
    private void ApplyPose()
    {
        if (sitter == null) return;
        Quaternion root = sitter.transform.rotation;
        for (int i = 0; i < poseBones.Length && i < poseRotations.Length; i++)
            if (poseBones[i] != null) poseBones[i].rotation = root * poseRotations[i];
    }
}
