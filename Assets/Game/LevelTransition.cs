using UnityEngine;
using UnityEngine.SceneManagement;

// Scroll from one level to the next: a band slides in from the right, the next level loads
// behind it, then the band keeps sliding out to the left - it reads as the street scrolling on.
// Survives the scene load (DontDestroyOnLoad). The new level sees Arrived = true and walks the
// dog and master in from the left (GameState.WalkIn).
public class LevelTransition : MonoBehaviour
{
    public static bool Arrived;
    public static bool Busy => inst != null && inst.phase != 0;

    const float Cover = 0.55f, Reveal = 0.6f;
    static LevelTransition inst;
    int phase, target;           // 0 idle, 1 sliding in, 2 loading, 3 sliding out
    float t;
    AsyncOperation op;

    public static void Go(int buildIndex)
    {
        if (inst == null)
        {
            var go = new GameObject("LevelTransition");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<LevelTransition>();
        }
        if (inst.phase != 0) return;
        inst.target = buildIndex; inst.phase = 1; inst.t = 0f; inst.op = null;
    }

    void Update()
    {
        t += Time.unscaledDeltaTime;
        if (phase == 1 && t >= Cover)
        {
            phase = 2; t = 0f;
            Arrived = true;
            Time.timeScale = 1f;
            op = SceneManager.LoadSceneAsync(target);
        }
        else if (phase == 2 && op != null && op.isDone) { phase = 3; t = 0f; }
        else if (phase == 3 && t >= Reveal) phase = 0;
    }

    void OnGUI()
    {
        if (phase == 0) return;
        GUI.depth = -1000;
        float W = Screen.width, H = Screen.height;
        float x = phase == 1 ? Mathf.Lerp(W, 0f, Ease(t / Cover))
                : phase == 2 ? 0f
                : Mathf.Lerp(0f, -W * 1.1f, Ease(t / Reveal));
        var band = new Rect(x, 0f, W * 1.1f, H);
        GUI.color = new Color(0.05f, 0.075f, 0.14f, 1f);
        GUI.DrawTexture(band, Texture2D.whiteTexture);
        // road stripes moving with the band, so it feels like the street scrolling past
        GUI.color = new Color(0.91f, 0.64f, 0.23f, 0.9f);
        float y = H * 0.72f, dash = H * 0.12f;
        for (float dx = 0f; dx < W * 1.1f; dx += dash * 2f)
            GUI.DrawTexture(new Rect(x + dx, y, dash, H * 0.012f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(H * 0.06f), fontStyle = Loc.Bold, alignment = TextAnchor.MiddleCenter };
        st.normal.textColor = new Color(0.95f, 0.92f, 0.87f);
        string path = SceneUtility.GetScenePathByBuildIndex(target);
        if (path.Contains("/Levels/"))   // story cutscenes and the title just get the plain band
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path).Replace("Level", "Level ");
            GUI.Label(new Rect(x, H * 0.38f, W, H * 0.12f), (Loc.Thai ? name.Replace("Level", "ด่าน") : name) + "  >>", st);
        }
    }

    static float Ease(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }
}
