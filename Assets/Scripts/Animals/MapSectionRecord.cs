using UnityEngine;

/// <summary>
/// Bookkeeping left by a map-authoring tool so its section can be removed cleanly:
/// the terrain it replaced, scene objects it hid, and objects it re-seated on the reshaped ground.
/// </summary>
[DisallowMultipleComponent]
public sealed class MapSectionRecord : MonoBehaviour
{
    public TerrainData sourceTerrain;
    public GameObject[] hidden = new GameObject[0];
    public Transform[] reseated = new Transform[0];
    public float[] reseatOffsets = new float[0];
}
