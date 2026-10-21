using UnityEngine;

/// <summary>Small bird capsules need tolerance at terrain triangle seams, not a zero step height.</summary>
public static class BirdGroundTraversal
{
    public static void Configure(CharacterController body)
    {
        if (body == null) return;
        body.minMoveDistance = 0f;
        body.skinWidth = body.radius * .1f;
        // Only tiny ground irregularities: crates, melons and fences still require a jump.
        body.stepOffset = Mathf.Min(.04f, body.height * .12f);
        // The controller rests its capsule a skin width above the ground. The birds' feet are at the pivot, so the
        // capsule's foot sits that skin width above the pivot: then the feet, not the capsule, touch the ground.
        // (All bird capsules have their base at the pivot; set from the height, so calling this again changes nothing.)
        Vector3 center = body.center;
        body.center = new Vector3(center.x, body.height * .5f + body.skinWidth, center.z);
    }
}
