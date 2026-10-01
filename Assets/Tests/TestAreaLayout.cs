using UnityEngine;

// World-space origin of the developer test area. Kept far from the farm terrain
// (beyond the gameplay camera's 1000 m far clip) so terrain edits never overlap it.
public static class TestAreaLayout
{
    public static readonly Vector3 Origin = new Vector3(0f, 0f, -1200f);
}
