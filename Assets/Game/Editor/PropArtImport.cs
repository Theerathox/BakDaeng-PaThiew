using UnityEditor;
using UnityEngine;

// Pictures in Resources/PropArt are imported as sprites standing on their bottom edge (the key: centred).
class PropArtImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        string p = assetPath.Replace('\\', '/');
        if (!p.Contains("Resources/PropArt/")) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.spritePixelsPerUnit = 200f;
        ti.maxTextureSize = 1024;
        ti.textureCompression = TextureImporterCompression.Compressed;
        var s = new TextureImporterSettings();
        ti.ReadTextureSettings(s);
        s.spriteAlignment = (int)(p.EndsWith("/key.png") ? SpriteAlignment.Center : SpriteAlignment.BottomCenter);
        s.spriteMeshType = SpriteMeshType.FullRect;
        ti.SetTextureSettings(s);
    }
}
