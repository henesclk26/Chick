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
    }
}
