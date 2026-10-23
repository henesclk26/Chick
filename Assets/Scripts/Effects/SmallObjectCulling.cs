using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Frame-rate help for the farm's thousands of small props (pebbles, seeds, mushrooms, flowers, small plants, fruit):
/// each is drawn and casts a shadow as one draw call, even far away where it covers a pixel or two. This skips the
/// draw where a prop would be less than <see cref="hidePixels"/> tall on screen, and its shadow where it would be less
/// than <see cref="shadowPixels"/> tall (such a shadow is a soft speck the shadow filter blurs away). Both are measured
/// against the actual screen height and the main camera's field of view, with a little hysteresis so nothing flickers
/// at the threshold. Only forceRenderingOff and shadowCastingMode are touched, never enabled, so gameplay that shows
/// and hides renderers is unaffected; everything is restored when this is disabled. Props in LOD groups (they cull
/// themselves), skinned or animated ones, the birds and anything larger than <see cref="maxSize"/> are left alone.
/// Presentation only.
/// </summary>
[DefaultExecutionOrder(250)]
public sealed class SmallObjectCulling : MonoBehaviour
{
    [SerializeField, Min(.1f), Tooltip("Props larger than this (metres, largest side) are never touched.")]
    private float maxSize = 1.5f;
    [SerializeField, Min(.5f), Tooltip("Below this height on screen (pixels) a prop is not drawn.")]
    private float hidePixels = 1.5f;
    [SerializeField, Min(1f), Tooltip("Below this height on screen (pixels) a prop casts no shadow.")]
    private float shadowPixels = 4f;
    [SerializeField, Range(1f, 1.5f), Tooltip("A prop comes back once it is this much above a threshold again.")]
    private float hysteresis = 1.2f;
    [SerializeField, Min(50), Tooltip("Props checked per frame; the whole set is gone through every few frames.")]
    private int perFrame = 600;

    private sealed class Prop
    {
        public Renderer renderer;
        public Transform transform;
        public Vector3 offset;          // bounds centre from the transform, taken at the scan (cheaper than bounds)
        public float size;
        public UnityEngine.Rendering.ShadowCastingMode shadows;
        public bool hidden, shadowless;
    }

    private readonly List<Prop> props = new List<Prop>();
    private int cursor;
    private bool scanned;

    private void OnDisable() => RestoreAll();

    private void LateUpdate()
    {
        if (!scanned) { Scan(); scanned = true; }
        var cam = Camera.main;
        if (cam == null || props.Count == 0) return;

        // a prop of size s at distance d is s * pixelsPerMetre / d pixels tall; compared squared, without a root:
        // px < limit  <=>  (s * pixelsPerMetre)^2 < limit^2 * d^2
        float pixelsPerMetre = Screen.height/(2f*Mathf.Tan(cam.fieldOfView*.5f*Mathf.Deg2Rad));
        float hide2 = hidePixels*hidePixels, hideBack2 = hide2*hysteresis*hysteresis;
        float shadow2 = shadowPixels*shadowPixels, shadowBack2 = shadow2*hysteresis*hysteresis;
        Vector3 eye = cam.transform.position;
        int n = Mathf.Min(perFrame, props.Count);
        for (int i = 0; i < n; i++)
        {
            if (cursor >= props.Count) cursor = 0;
            var p = props[cursor++];
            if (p.renderer == null) continue;
            float d2 = Mathf.Max((p.transform.position + p.offset - eye).sqrMagnitude, 1e-4f);
            float s = p.size*pixelsPerMetre, px2 = s*s/d2;

            bool hide = p.hidden ? px2 < hideBack2 : px2 < hide2;
            if (hide != p.hidden) { p.hidden = hide; p.renderer.forceRenderingOff = hide; }

            if (p.shadows == UnityEngine.Rendering.ShadowCastingMode.Off) continue;
            bool noShadow = p.shadowless ? px2 < shadowBack2 : px2 < shadow2;
            if (noShadow != p.shadowless)
            {
                p.shadowless = noShadow;
                p.renderer.shadowCastingMode = noShadow ? UnityEngine.Rendering.ShadowCastingMode.Off : p.shadows;
            }
        }
    }

    private void Scan()
    {
        props.Clear();
        var inLod = new HashSet<Renderer>();
        foreach (var group in FindObjectsByType<LODGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            foreach (var lod in group.GetLODs())
                foreach (var r in lod.renderers) if (r != null) inLod.Add(r);

        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (inLod.Contains(r)) continue;
            var bounds = r.bounds;
            var size = bounds.size;
            float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (largest > maxSize || largest <= 0f) continue;
            // the birds, the animals and anything that copies another renderer's shadow setting
            if (r.GetComponentInParent<Animator>(true) != null || r.GetComponentInParent<CharacterController>(true) != null) continue;
            if (r.GetComponentInParent<WatermelonSliceBites>(true) != null) continue;
            props.Add(new Prop { renderer = r, transform = r.transform, offset = bounds.center - r.transform.position, size = largest, shadows = r.shadowCastingMode });
        }
        cursor = 0;
    }

    private void RestoreAll()
    {
        foreach (var p in props)
        {
            if (p.renderer == null) continue;
            if (p.hidden) p.renderer.forceRenderingOff = false;
            if (p.shadowless) p.renderer.shadowCastingMode = p.shadows;
            p.hidden = p.shadowless = false;
        }
        scanned = false;
    }
}
