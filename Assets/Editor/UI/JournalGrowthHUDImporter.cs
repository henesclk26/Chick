using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>Imports the approved journal HUD as separate frame, fill and cursor sprites.</summary>
public sealed class JournalGrowthHUDImporter : AssetPostprocessor
{
    public const string AtlasPath = "Assets/Resources/JournalGrowthHUD/HudAtlas.png";

    private void OnPreprocessTexture()
    {
        if (assetPath != AtlasPath) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = 100;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var previous = provider.GetSpriteRects();
        // Rectangles measured on the final 1536 x 1024 source; y is top-down here.
        var sprites = new[]
        {
            Slice("Frame", 25, 181, 1486, 183, new Vector4(92, 0, 92, 0)),
            Slice("Fill", 98, 446, 1340, 128),
            Slice("Cursor", 781, 746, 133, 134)
        };
        foreach (var sprite in sprites)
        {
            var old = previous.FirstOrDefault(x => x.name == sprite.name);
            if (old != null) sprite.spriteID = old.spriteID;
        }
        provider.SetSpriteRects(sprites);
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(
            sprites.Select(x => new SpriteNameFileIdPair(x.name, x.spriteID)));
        provider.Apply();
    }

    private static SpriteRect Slice(string name, int x, int y, int width, int height, Vector4 border = default)
        => new SpriteRect
        {
            name = name, spriteID = GUID.Generate(),
            rect = new Rect(x, 1024 - y - height, width, height),
            pivot = new Vector2(.5f, .5f), alignment = SpriteAlignment.Center, border = border
        };
}
