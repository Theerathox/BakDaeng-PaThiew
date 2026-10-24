using UnityEngine;

// Short looping demo clips for the guide cards: real gameplay, recorded in the editor
// (Tools/Pet Game/Record Guide Demos) into a flipbook picture per clip: Resources/GuideDemos/<id>.png,
// frames of FW x FH pixels, Cols per row, played at Fps.
public static class GuideDemo
{
    public const int FW = 384, FH = 216, Cols = 8, Fps = 10;

    // true while the editor is recording a clip: no tip cards, no scene changes
    public static bool Recording;

    static string loadedId;
    static Texture2D loaded;

    public static Texture2D Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (id == loadedId) return loaded;
        if (loaded != null) Resources.UnloadAsset(loaded);
        loadedId = id;
        loaded = Resources.Load<Texture2D>("GuideDemos/" + id);
        return loaded;
    }

    public static bool Has(string id) => Get(id) != null;

    // draws the clip into r (keeps 16:9 inside it); 'start' = when it started playing (unscaled time)
    public static bool Draw(Rect r, string id, float start)
    {
        var tex = Get(id);
        if (tex == null) return false;
        int rows = Mathf.Max(1, tex.height / FH);
        int frames = rows * Cols;
        int f = Mathf.FloorToInt((Time.unscaledTime - start) * Fps) % frames;
        int col = f % Cols, row = f / Cols;
        float u = (float)FW / tex.width, v = (float)FH / tex.height;
        var uv = new Rect(col * u, 1f - (row + 1) * v, u, v);

        float h = Mathf.Min(r.height, r.width * FH / FW), w = h * FW / FH;
        var box = new Rect(r.x + (r.width - w) / 2f, r.y + (r.height - h) / 2f, w, h);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(box.x - 3f, box.y - 3f, box.width + 6f, box.height + 6f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.DrawTextureWithTexCoords(box, tex, uv);
        // progress bar so you can see where the clip loops
        GUI.color = new Color(1f, 0.85f, 0.4f, 0.8f);
        GUI.DrawTexture(new Rect(box.x, box.yMax - 4f, box.width * (f + 1) / frames, 4f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        return true;
    }
}
