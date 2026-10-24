using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// RETRY / LEVELS buttons at the top of the screen + a level picker, so any level can be shown
// straight away (demos, testing), plus a STORY row to replay the cutscenes. Created automatically
// (no need to add it to the levels); the RETRY / LEVELS buttons show in levels, the title screen opens
// the picker with LevelSelect.Show(). Keys: L = open/close, 1-9 and 0 (= 10) while it is open, Esc = close.
// The game is paused while the picker is open.
public class LevelSelect : MonoBehaviour
{
    public static bool Open { get; private set; }

    // short names for the buttons (the scene's own title is shown when it is the current level)
    static readonly string[] Names = {
        "First Steps", "Lock It Behind", "The Rusty Gate", "Mor Lam Switchback", "One Palm, One Shot",
        "Candle in the Dark", "Drop the Bridge", "Hold the Light", "Behind the Radio", "The Great Shrine",
        "Run for the Taxi" };

    float pausedScale = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("LevelSelect");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<LevelSelect>();
    }

    // build indices of the scenes in a folder (Levels / Cutscenes), in build order
    static System.Collections.Generic.List<int> Scenes(string folder)
    {
        var list = new System.Collections.Generic.List<int>();
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            if (SceneUtility.GetScenePathByBuildIndex(i).Contains(folder)) list.Add(i);
        return list;
    }
    static System.Collections.Generic.List<int> Levels() => Scenes("/Levels/");

    static LevelSelect inst;
    public static void Show() { if (inst != null) inst.SetOpen(true); }

    bool InLevel;   // this scene has a GameState (refreshed every frame)

    void SetOpen(bool open)
    {
        if (open == Open) return;
        Open = open;
        if (open) { pausedScale = Time.timeScale; Time.timeScale = 0f; }
        else Time.timeScale = GameState.IsOver ? 0f : pausedScale;
    }

    void Load(int buildIndex)
    {
        Open = false;
        Time.timeScale = 1f;
        if (LevelTransition.Busy) return;
        SceneManager.LoadScene(buildIndex);   // instant: no walk-in, straight to the title card
    }

    void Update()
    {
        InLevel = FindFirstObjectByType<GameState>() != null;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return;
        if (InLevel && k.lKey.wasPressedThisFrame) SetOpen(!Open);
        if (Open && k.escapeKey.wasPressedThisFrame) SetOpen(false);
        if (!Open) return;
        var levels = Levels();
        Key[] digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0 };
        for (int i = 0; i < digits.Length && i < levels.Count; i++)
            if (k[digits[i]].wasPressedThisFrame) { Load(levels[i]); return; }
#else
        if (InLevel && Input.GetKeyDown(KeyCode.L)) SetOpen(!Open);
        if (Open && Input.GetKeyDown(KeyCode.Escape)) SetOpen(false);
        if (!Open) return;
        var levels = Levels();
        for (int i = 0; i < 10 && i < levels.Count; i++)
            if (Input.GetKeyDown(i == 9 ? KeyCode.Alpha0 : KeyCode.Alpha1 + i)) { Load(levels[i]); return; }
#endif
    }

    void OnGUI()
    {
        if ((!InLevel && !Open) || PetZoneDetector.SetupOpen || LevelTransition.Busy) return;
        GUI.depth = -10;   // on top of the game's own GUI
        float s = Mathf.Max(1f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = Screen.height / s;
        var btn = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = Loc.Bold };

        if (!Open)
        {
            if (GUI.Button(new Rect(W / 2f - 105f, 14f, 100f, 44f), Loc.T("RETRY"), btn)) GameState.Restart();
            if (GUI.Button(new Rect(W / 2f + 5f, 14f, 100f, 44f), Loc.T("LEVELS"), btn)) SetOpen(true);
            return;
        }

        // picker: MORNING 1-5, NIGHT 6-10, FINALE 11+, STORY (cutscenes)
        GUI.color = new Color(0f, 0f, 0f, 0.8f); GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture); GUI.color = Color.white;
        var title = new GUIStyle(GUI.skin.label) { fontSize = 32, fontStyle = Loc.Bold, alignment = TextAnchor.MiddleCenter };
        title.normal.textColor = new Color(0.78f, 1f, 0.37f);
        GUI.Label(new Rect(0, 18f, W, 44f), Loc.T("SELECT LEVEL"), title);
        var hint = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        hint.normal.textColor = new Color(0.95f, 0.92f, 0.87f, 0.8f);
        GUI.Label(new Rect(0, 58f, W, 24f), Loc.T("Keys: 1-9, 0 = level 10, L / Esc = close"), hint);

        var levels = Levels();
        var cuts = Scenes("/Cutscenes/");
        int current = SceneManager.GetActiveScene().buildIndex;
        float bw = Mathf.Min(220f, (W - 80f) / 5f - 12f), bh = 70f, gap = 40f;
        float x0 = W / 2f - (5 * (bw + 12f) - 12f) / 2f;
        var rowSt = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = Loc.Bold, alignment = TextAnchor.MiddleLeft };
        string[] rowNames = { "MORNING", "NIGHT", "FINALE", "STORY" };
        Color[] rowCols = { new Color(1f, 0.85f, 0.5f), new Color(0.62f, 0.8f, 1f), new Color(1f, 0.7f, 0.85f), new Color(0.78f, 1f, 0.37f) };
        var st = new GUIStyle(btn) { fontSize = 14, wordWrap = true, alignment = TextAnchor.MiddleCenter, richText = true };
        for (int r = 0; r < 4; r++)
        {
            float y = 118f + r * (bh + gap);
            rowSt.normal.textColor = rowCols[r];
            GUI.Label(new Rect(x0, y - 28f, 300f, 26f), Loc.T(rowNames[r]), rowSt);
            for (int c = 0; c < 5; c++)
            {
                int index; string label;
                if (r < 3)
                {
                    int i = r < 2 ? r * 5 + c : 10 + c;
                    if (i >= levels.Count) continue;
                    index = levels[i];
                    label = $"<size=24>{i + 1}</size>\n{(i < Names.Length ? Loc.T(Names[i]) : "Level " + (i + 1))}";
                }
                else
                {
                    if (c >= cuts.Count) continue;
                    index = cuts[c];
                    string n = System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(index));
                    label = Loc.T(n.Contains("_") ? n.Substring(n.IndexOf('_') + 1) : n);   // Cut1_Opening -> Opening
                }
                if (index == current) GUI.color = new Color(0.78f, 1f, 0.37f);
                if (GUI.Button(new Rect(x0 + c * (bw + 12f), y, bw, bh), label, st)) Load(index);
                GUI.color = Color.white;
            }
        }
        if (GUI.Button(new Rect(W / 2f - 80f, H - 66f, 160f, 48f), Loc.T("CLOSE"), btn)) SetOpen(false);
    }
}
