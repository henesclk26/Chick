using UnityEngine;

// Feeds the ground colour map to Chick/InteractiveFoliage materials that have the _GROUND_TINT keyword (the grass):
// every blade takes the colour of the ground under it, green over grass and golden over earth, so a clump on the edge
// of a path turns from one to the other. The map (rgb: the grass colour of the ground, a: its earth share) is baked by
// Tools/Chick/Ground/Grass Ground Tint (V1). Global shader values, so they are set again whenever this is enabled.
[ExecuteAlways]
public class GroundTintGlobals : MonoBehaviour
{
    static readonly int MapId = Shader.PropertyToID("_ChickGroundTintMap");
    static readonly int RectId = Shader.PropertyToID("_ChickGroundTintRect");
    static readonly int ParamsId = Shader.PropertyToID("_ChickGroundTintParams");
    static readonly int DryId = Shader.PropertyToID("_ChickGroundTintDry");

    [SerializeField] Texture2D groundColourMap;
    [Tooltip("World min x, world min z, 1 / world size x, 1 / world size z")]
    [SerializeField] Vector4 worldRect = new Vector4(0, 0, .01f, .01f);
    [Range(0, 1)] [SerializeField] float strength = .9f;
    [Range(.5f, 2f)] [SerializeField] float brightness = 1.15f;
    [Range(0, 2f)] [SerializeField] float saturation = 1.15f;
    [Tooltip("The colour the grass takes over earth (paths, earth patches)")]
    [SerializeField] Color dryColour = new Color(.84f, .62f, .25f);
    [Tooltip("The share of earth in the ground where a blade is half green, half golden")]
    [Range(.1f, .9f)] [SerializeField] float dryFrom = .3f;

    public void Configure(Texture2D map, Vector4 rect)
    {
        groundColourMap = map;
        worldRect = rect;
        Apply();
    }

    void OnEnable() => Apply();
    void OnValidate() => Apply();

    void Apply()
    {
        if (groundColourMap == null) return;
        Shader.SetGlobalTexture(MapId, groundColourMap);
        Shader.SetGlobalVector(RectId, worldRect);
        Shader.SetGlobalVector(ParamsId, new Vector4(strength, brightness, saturation, 0));
        var dry = dryColour.linear;
        Shader.SetGlobalVector(DryId, new Vector4(dry.r, dry.g, dry.b, dryFrom));
    }
}
