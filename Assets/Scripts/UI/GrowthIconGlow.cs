using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Small, cached alpha-silhouette halo behind a HUD icon.</summary>
internal sealed class GrowthIconGlow : IDisposable
{
    private const int Padding = 8;
    private readonly Image icon;
    private readonly Image halo;
    private readonly Texture2D mask;
    private readonly float aspect;

    public GrowthIconGlow(Image icon)
    {
        this.icon = icon;
        Sprite sprite = icon.sprite;
        aspect = sprite.rect.width / sprite.rect.height;
        mask = BuildMask(sprite);
        halo = new Image { image = mask, pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
        halo.name = icon.name + "Glow";
        halo.style.position = Position.Absolute;
        halo.style.opacity = 0f;
        icon.parent.Insert(icon.parent.IndexOf(icon), halo);
        icon.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        PositionHalo();
    }

    public void SetIntensity(float value) => halo.style.opacity = Mathf.Clamp01(value) * .7f;

    private void OnGeometryChanged(GeometryChangedEvent evt) => PositionHalo();

    private void PositionHalo()
    {
        Rect rect = icon.contentRect;
        if (float.IsNaN(rect.width) || rect.width <= 0f || rect.height <= 0f) return;
        float height = Mathf.Min(rect.height, rect.width / aspect);
        float width = height * aspect;
        float padding = height * Padding / 144f;
        halo.style.left = icon.layout.x + rect.x + (rect.width - width) * .5f - padding;
        halo.style.top = icon.layout.y + rect.y + (rect.height - height) * .5f - padding;
        halo.style.width = width + padding * 2f;
        halo.style.height = height + padding * 2f;
    }

    private static Texture2D BuildMask(Sprite sprite)
    {
        // Read once at HUD resolution; the original imported texture need not be readable.
        int width = Mathf.Max(1, Mathf.RoundToInt(144f * sprite.rect.width / sprite.rect.height));
        const int height = 144;
        var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var sample = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color32[] source;
        try
        {
            Rect r = sprite.rect;
            var texture = sprite.texture;
            Graphics.Blit(texture, target, new Vector2(r.width / texture.width, r.height / texture.height),
                new Vector2(r.x / texture.width, r.y / texture.height));
            RenderTexture.active = target;
            sample.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            source = sample.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            ReleaseTexture(sample);
        }

        int outputWidth = width + Padding * 2;
        int outputHeight = height + Padding * 2;
        var pixels = new Color32[outputWidth * outputHeight];
        // Dilation with a feathered falloff follows the actual icon silhouette, not its box.
        for (int y = 0; y < outputHeight; y++)
        for (int x = 0; x < outputWidth; x++)
        {
            float alpha = 0f;
            for (int dy = -Padding; dy <= Padding; dy++)
            for (int dx = -Padding; dx <= Padding; dx++)
            {
                int sx = x - Padding + dx, sy = y - Padding + dy;
                if (sx < 0 || sx >= width || sy < 0 || sy >= height) continue;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float weight = 1f - Mathf.SmoothStep(0f, 1f, (distance - 2f) / (Padding - 2f));
                alpha = Mathf.Max(alpha, source[sy * width + sx].a * weight);
            }
            pixels[y * outputWidth + x] = new Color32(120, 245, 104, (byte)alpha);
        }
        var result = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false)
        { name = "Growth icon halo", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        result.SetPixels32(pixels);
        result.Apply(false, true);
        return result;
    }

    public void Dispose()
    {
        icon.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        halo.RemoveFromHierarchy();
        ReleaseTexture(mask);
    }

    private static void ReleaseTexture(Texture2D texture)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
        else UnityEngine.Object.DestroyImmediate(texture);
    }
}
