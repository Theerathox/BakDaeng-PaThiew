using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Thai / English. Every text on screen goes through Loc.T("English text"): in Thai mode it is looked up in
// LocThai.Table (Game/LocThai.cs); anything not in the table stays English. Loc.F("... {0} ...", x) for
// texts with numbers. The Thai font (Kanit, SIL Open Font License) is in Resources/Fonts.
// Language: the button on the title screen (saved in PlayerPrefs "lang"). Thai by default.
public static class Loc
{
    public static bool Thai
    {
        get => PlayerPrefs.GetInt("lang", 1) == 1;
        set { PlayerPrefs.SetInt("lang", value ? 1 : 0); PlayerPrefs.Save(); LocRuntime.Refresh(); }
    }

    public static string T(string en)
    {
        if (string.IsNullOrEmpty(en) || !Thai) return en;
        return LocThai.Table.TryGetValue(en, out var th) ? Shape(th) : en;
    }

    // Unity's text has no Thai mark positioning: a tone mark on top of an upper vowel (เริ่ม, ที่) would sit
    // inside the vowel and vanish. The font has raised / left-shifted copies of the marks at U+F701-F712
    // (added to Kanit for this game), so swap them in where needed. Cached.
    static readonly Dictionary<string, string> shaped = new Dictionary<string, string>();
    const string Tones = "\u0E48\u0E49\u0E4A\u0E4B\u0E4C";
    public static string Shape(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (shaped.TryGetValue(s, out var r)) return r;
        var sb = new System.Text.StringBuilder(s.Length);
        char baseC = ' ';
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c >= '\u0E01' && c <= '\u0E2E') baseC = c;
            bool tall = baseC == '\u0E1B' || baseC == '\u0E1D' || baseC == '\u0E1F' || baseC == '\u0E2C';   // ป ฝ ฟ ฬ
            int up = UpperIndex(c);
            if (up >= 0 && tall) { sb.Append(UpperNarrow[up]); continue; }
            int k = Tones.IndexOf(c);
            if (k >= 0)
            {
                char prev = i > 0 ? s[i - 1] : ' ', next = i + 1 < s.Length ? s[i + 1] : ' ';
                if (UpperIndex(prev) >= 0 || next == '\u0E33') { sb.Append((char)(0xF70A + k)); continue; }   // raised
                if (tall) { sb.Append((char)(0xF705 + k)); continue; }                                       // shifted left
            }
            sb.Append(c);
        }
        r = sb.ToString();
        if (shaped.Count < 2000) shaped[s] = r;
        return r;
    }
    const string Upper = "\u0E31\u0E34\u0E35\u0E36\u0E37\u0E4D\u0E47";
    static readonly char[] UpperNarrow = { '\uF710', '\uF701', '\uF702', '\uF703', '\uF704', '\uF711', '\uF712' };
    static int UpperIndex(char c) => Upper.IndexOf(c);

    // Unity's fake bold adds a gap after every Thai mark (เที่ ยว), and Kanit Medium is already heavy:
    // use this instead of FontStyle.Bold for on-screen text.
    public static FontStyle Bold => Thai ? FontStyle.Normal : FontStyle.Bold;

    public static string F(string en, params object[] args)
    {
        try { return Shape(string.Format(T(en), args)); } catch { return string.Format(en, args); }
    }

    static Font font; static bool looked;
    public static Font ThaiFont { get { if (!looked) { looked = true; font = Resources.Load<Font>("Fonts/KanitGame"); } return font; } }
    static Font legacy;
    // font for world text (TextMesh): Thai font in Thai mode, the built-in one otherwise
    public static Font WorldFont => Thai && ThaiFont != null ? ThaiFont : (legacy != null ? legacy : legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
}

// Puts the Thai font on all on-screen (IMGUI) text, and translates the world texts in each scene
// (signs: NEXT >>, LEVER, LOCKED, TAXI...). Created automatically.
public class LocRuntime : MonoBehaviour
{
    static LocRuntime inst;
    readonly Dictionary<TextMesh, string> original = new Dictionary<TextMesh, string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("Loc");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<LocRuntime>();
        SceneManager.sceneLoaded += (s, m) => { if (inst != null) inst.original.Clear(); Refresh(); };
        Refresh();
    }

    public static void Refresh()
    {
        if (inst == null) return;
        foreach (var tm in FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (tm == null || tm.name == "zzz" || tm.name == "shadow" || tm.name.StartsWith("BarkHint")) continue;   // BarkHints translate themselves
            if (!inst.original.TryGetValue(tm, out var en)) { en = tm.text; inst.original[tm] = en; }
            string want = Loc.T(en);
            if (tm.text == want && tm.font == Loc.WorldFont) continue;
            tm.text = want;
            tm.font = Loc.WorldFont;
            var mr = tm.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = tm.font.material;
        }
    }

    void OnGUI()
    {
        GUI.depth = 1000;   // runs early; the font stays on the skin for everything drawn after
        var f = Loc.Thai ? Loc.ThaiFont : null;
        if (GUI.skin.font != f) GUI.skin.font = f;
    }
}
