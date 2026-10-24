using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Teaches every piece of the world the FIRST time it comes on screen (game pauses, a ring points at it,
// a card explains what it does and the hidden rules - e.g. a falling palm knocks zombies into the water).
// The "?" button (top) opens the GUIDE BOOK: every entry, any time. Seen tips are remembered
// (PlayerPrefs "guide.seen"); the book can reset them or switch tips off.
// Created automatically, nothing to add to the scenes.
public class Guide : MonoBehaviour
{
    public static bool Open { get; private set; }

    class Entry
    {
        public string id, title, caption; public string[] demos, lines;
        public Func<Transform> find;   // something of this kind on screen right now (null = none)
    }

    List<Entry> entries;
    Entry showing; Transform pointAt; bool fromBook, book;
    float prevScale = 1f, shownAt, checkTimer;
    HashSet<string> seen;
    static Texture2D ringTex;
    static Guide inst;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("Guide");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<Guide>();
        SceneManager.sceneLoaded += (s, m) => { if (inst != null) inst.Close(false); };
    }

    void Awake()
    {
        seen = new HashSet<string>(PlayerPrefs.GetString("guide.seen", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        entries = new List<Entry>
        {
            // id, title, where to find it, ONE short line, demo clip(s) ("LABEL=clip" for a labelled one), more rules (guide book)
            E("exit", "NEXT STREET >>", Find<LevelExit>(), "Lead your master to the sign.", null,
              "The street scrolls on to the next level.",
              "Your master must be able to follow you - no closed gate or water in his way."),
            E("gate", "GATE", Find<Gate>(g => g.radius > 0f && !g.rusty), "Bark next to it: open. Bark again: close.", D("gate"),
              "A closed gate stops everyone - zombies too - and they can't hit through it.",
              "It won't close on someone standing in the doorway: walk through first, then bark."),
            E("chaser", "ZOMBIE", Find<ZombieChaser>(z => z.kind == ZombieChaser.Kind.Chaser && !z.asleep && !z.Gone), "It follows your master. Shut a gate between them!", D("chaser"),
              "Arms up = it is about to hit - move away!",
              "Every bark makes your master RUN to you, faster than any zombie.",
              "Zombies hear barks too: they walk to where you barked, look around, then go back."),
            E("rusty", "RUSTY GATE", Find<Gate>(g => g.rusty), "Opens very slowly - keep your master safe while you wait.", D("rusty"),
              "About 9 seconds - you can't hurry it.",
              "Shut the zombies out first, or let your master wait in candle light."),
            E("locked", "LOCKED GATE", Find<Gate>(g => g.radius <= 0f && Sign(g) == "LOCKED"), "Barking does nothing - SNIFF out its buried key.", D("key"),
              "The key is buried somewhere close by."),
            E("levergate", "LEVER GATE", Find<Gate>(g => g.radius <= 0f && Sign(g) != "LOCKED"), "Barking does nothing - a LEVER moves it.", D("lever")),
            E("scent", "SOMETHING BURIED", Find<ScentSpot>(), "SNIFF right next to it and Bak Daeng digs it up.", D("key"),
              "Barking won't find it.",
              "Sniff = bring the camera close to your pet (or the SNIFF button)."),
            E("lever", "LEVER", Find<Lever>(), "Bark at it: everything linked to it flips at once.", D("lever"),
              "The lines show what is linked, and what will happen to it.",
              "Two linked gates: one opens while the other shuts. Think about which side you'll be on!"),
            E("radio", "MOR LAM RADIO", Find<Radio>(r => r.radius > 0f), "Bark: zombies run to it and DANCE - walk right past them.", D("radio"),
              "Every zombie that hears it comes.",
              "Never stand between a running zombie and the radio. Some songs end: then the zombies go back."),
            E("guard", "GUARD ZOMBIE", Find<ZombieChaser>(z => z.kind == ZombieChaser.Kind.Guard && !z.asleep && !z.Gone), "Hits fast - never walk past it!", D("guard"),
              "Get it out of the way: lure it with a noise (bark, radio), lock it behind a gate, or drop it in the water."),
            E("canal", "CANAL", Find<Canal>(c => !c.bridged), "Nobody can cross water - zombies neither.", D("canal"),
              "Zombies can't hit across it either.",
              "You need a bridge: a drawbridge, or a palm tree."),
            E("palm", "LEANING PALM", Find<PalmBridge>(p => p.radius > 0f), "Bark: it falls across - and knocks zombies into the water!", D("palm"),
              "It stays there as a bridge - for zombies too.",
              "The orange stripe shows where it lands. It's LOUD, and it only falls once."),
            E("drawbridge", "DRAWBRIDGE", Find<Drawbridge>(), "Raise it while a zombie is on it: splash!", D("drawbridge"),
              "Bark at the winch (or its lever) to lower or raise it.",
              "It won't lift while you or your master are on it."),
            E("cans", "TIN CANS", Find<NoiseTrap>(), "Walking through = CLANG. Crawl past them.", D("WALK=cans_walk", "CRAWL=crawl"),
              "As loud as a bark: sleepers wake, zombies come to look.",
              "Crawl = tilt the camera up, your pet LOW in the picture (or the CRAWL button)."),
            E("sleeper", "SLEEPING ZOMBIE", Find<ZombieChaser>(z => z.asleep), "Any noise close by wakes it up.", D("sleeper"),
              "Barks, cans, running footsteps, a falling palm. Crawl, and keep quiet."),
            E("candle", "SPIRIT CANDLE", Find<Lantern>(l => l.GetComponentInParent<BlindFollower>() == null), "Zombies can't step into the light.", D("candle"),
              "Bark to call your master in, and keep still while he waits there. Works on a horde too."),
            E("horde", "HORDE", Find<HordeWall>(), "KEEP MOVING! A closed gate stops it.", D("horde"),
              "It is only a little slower than you.",
              "Gates, water and candle light stop it."),
            E("dark", "NIGHT", () => Darkness.Active ? FindFirstObjectByType<DogWalker>()?.transform : null, "Bark = a sound wave shows what's around. SNIFF = silent.", D("dark"),
              "You only see around Bak Daeng.",
              "Everything hears the bark. Sniff shows close by without a sound."),
            E("shelter", "SHELTER", Find<Shrine>(s => s.placeName == "shelter"), "Stand just past it, then bark to go in.", null),
            E("shrine", "SPIRIT SHRINE", Find<Shrine>(s => s.placeName != "shelter"), "Bring your master next to it, then bark.", null),
            E("taxi", "TAXI", Find<Taxi>(), "Reach it with your master to escape!", null),
        };
    }

    static Entry E(string id, string title, Func<Transform> find, string caption, string[] demos, params string[] lines) =>
        new Entry { id = id, title = title, find = find, caption = caption, demos = demos ?? new string[0], lines = lines };
    static string[] D(params string[] clips) => clips;

    static Func<Transform> Find<T>(Func<T, bool> ok = null) where T : Component => () =>
    {
        foreach (var c in FindObjectsByType<T>(FindObjectsSortMode.None))
            if (c.gameObject.activeInHierarchy && (ok == null || ok(c)) && OnScreen(c.transform)) return c.transform;
        return null;
    };

    static string Sign(Gate g) { var t = g.transform.Find("leverSign"); var tm = t != null ? t.GetComponent<TextMesh>() : null; return tm != null ? tm.text : ""; }

    static bool OnScreen(Transform t)
    {
        var cam = Camera.main;
        if (cam == null) return false;
        float half = cam.orthographicSize * cam.aspect;
        return Mathf.Abs(t.position.x - cam.transform.position.x) < half - 0.8f;
    }

    static bool TipsOn => PlayerPrefs.GetInt("guide.off", 0) == 0;
    static bool InLevel => !GuideDemo.Recording && FindFirstObjectByType<GameState>() != null;

    void Update()
    {
        if (Open)
        {
            if (showing != null && Time.unscaledTime - shownAt > 0.5f && Tapped()) { if (fromBook) { showing = null; pointAt = null; } else Close(true); }
            else if (book && showing == null && Esc()) Close(true);
            if (Open && !GameState.IsOver && !LevelSelect.Open && !PetZoneDetector.SetupOpen) Time.timeScale = 0f;
            return;
        }
        if (!TipsOn || !InLevel || !GameState.Started || GameState.IsOver || GameState.Cutscene || LevelSelect.Open || PetZoneDetector.SetupOpen || PetPause.Paused || LevelTransition.Busy) return;
        checkTimer -= Time.unscaledDeltaTime;
        if (checkTimer > 0f) return;
        checkTimer = 0.25f;
        foreach (var e in entries)
        {
            if (seen.Contains(e.id)) continue;
            var t = e.find();
            if (t == null) continue;
            seen.Add(e.id); PlayerPrefs.SetString("guide.seen", string.Join(",", seen)); PlayerPrefs.Save();
            Show(e, t, false);
            break;
        }
    }

    void Show(Entry e, Transform t, bool book)
    {
        if (!Open) { prevScale = Time.timeScale > 0f ? Time.timeScale : 1f; }
        Open = true; showing = e; pointAt = t; fromBook = book; shownAt = Time.unscaledTime;
        Time.timeScale = 0f;
        if (!book) Sfx.Play("ping", 0.6f, 1.3f);
    }

    void Close(bool restore)
    {
        bool was = Open;
        Open = false; book = false; showing = null; pointAt = null;
        if (was && restore && !GameState.IsOver) Time.timeScale = prevScale;
    }

    static bool Tapped()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame)) return true;
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
        return Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0);
#endif
    }

    static bool Esc()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    // ------------------------------------------------------------------ drawing
    void OnGUI()
    {
        if (!InLevel || PetZoneDetector.SetupOpen || LevelSelect.Open || LevelTransition.Busy) return;
        GUI.depth = -15;
        float s = Mathf.Max(1f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = Screen.height / s;
        var ink = new Color(0.95f, 0.92f, 0.87f);
        var gold = new Color(1f, 0.85f, 0.4f);

        if (!Open)
        {
            if (!GameState.IsOver && GUI.Button(new Rect(W / 2f + 115f, 14f, 54f, 44f), "?", new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = Loc.Bold }))
            { book = true; Show(null, null, true); showing = null; }
            return;
        }

        GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture); GUI.color = Color.white;

        if (showing != null)
        {
            // ring around the thing on screen
            if (pointAt != null && Camera.main != null)
            {
                Vector3 sp = Camera.main.WorldToScreenPoint(pointAt.position + Vector3.up * (pointAt.GetComponent<Canal>() != null ? 0.1f : 1.2f));
                float px = sp.x / s, py = (Screen.height - sp.y) / s;
                float rr = 70f + 6f * Mathf.Sin(Time.unscaledTime * 5f);
                GUI.color = gold; GUI.DrawTexture(new Rect(px - rr, py - rr, rr * 2f, rr * 2f), Ring()); GUI.color = Color.white;
            }
            DrawCard(showing, W, ink, gold, fromBook);
            return;
        }

        // the guide book
        var panel = new Rect(W / 2f - 420f, 70f, 840f, H - 130f);
        GUI.color = new Color(0.03f, 0.04f, 0.08f, 0.95f); GUI.DrawTexture(panel, Texture2D.whiteTexture); GUI.color = Color.white;
        var title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = Loc.Bold, alignment = TextAnchor.MiddleCenter };
        title.normal.textColor = gold;
        GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 44f), Loc.T("GUIDE BOOK"), title);
        var btn = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = Loc.Bold, wordWrap = true };
        int cols = 4; float bw = (panel.width - 40f - (cols - 1) * 10f) / cols, bh = 44f;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var br = new Rect(panel.x + 20f + (i % cols) * (bw + 10f), panel.y + 62f + (i / cols) * (bh + 8f), bw, bh);
            GUI.color = seen.Contains(e.id) ? Color.white : new Color(1f, 1f, 1f, 0.6f);
            if (GUI.Button(br, Loc.T(e.title), btn)) { showing = e; pointAt = null; fromBook = true; shownAt = Time.unscaledTime; }
            GUI.color = Color.white;
        }
        float fy = panel.yMax - 58f;
        if (GUI.Button(new Rect(panel.x + 20f, fy, 220f, 44f), TipsOn ? Loc.T("TIPS: ON") : Loc.T("TIPS: OFF"), btn)) { PlayerPrefs.SetInt("guide.off", TipsOn ? 1 : 0); PlayerPrefs.Save(); }
        if (GUI.Button(new Rect(panel.x + 250f, fy, 220f, 44f), Loc.T("SHOW TIPS AGAIN"), btn)) { seen.Clear(); PlayerPrefs.DeleteKey("guide.seen"); PlayerPrefs.Save(); }
        if (GUI.Button(new Rect(panel.xMax - 180f, fy, 160f, 44f), Loc.T("CLOSE"), btn)) Close(true);
    }

    // NEW: title, the demo clip(s) playing, ONE short line. From the guide book: + the extra rules.
    void DrawCard(Entry e, float W, Color ink, Color gold, bool more)
    {
        var clips = new List<(string label, string id)>();
        foreach (var d in e.demos)
        {
            int k = d.IndexOf('=');
            string label = k > 0 ? d.Substring(0, k) : "", id = k > 0 ? d.Substring(k + 1) : d;
            if (GuideDemo.Has(id)) clips.Add((label, id));
        }
        var cap = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = Loc.Bold, wordWrap = true, alignment = TextAnchor.MiddleCenter };
        cap.normal.textColor = ink;
        var b = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
        b.normal.textColor = new Color(ink.r, ink.g, ink.b, 0.85f);
        float cw = 620f, inner = cw - 48f;
        float clipH = clips.Count == 0 ? 0f : clips.Count == 1 ? 252f : (inner - 16f) / 2f * 9f / 16f + 26f;
        float capH = cap.CalcHeight(new GUIContent(Loc.T(e.caption)), inner);
        float moreH = 0f;
        bool showMore = more || clips.Count == 0;
        if (showMore) foreach (var line in e.lines) moreH += b.CalcHeight(new GUIContent("•  " + Loc.T(line)), inner) + 4f;
        var r = new Rect(W / 2f - cw / 2f - 20f, 60f, cw, 58f + clipH + (clipH > 0f ? 12f : 0f) + capH + (moreH > 0f ? moreH + 8f : 0f) + 44f);
        GUI.color = new Color(0.03f, 0.04f, 0.08f, 0.94f); GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = gold; GUI.DrawTexture(new Rect(r.x, r.y, r.width, 4f), Texture2D.whiteTexture); GUI.color = Color.white;
        var t = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = Loc.Bold, alignment = TextAnchor.MiddleCenter };
        t.normal.textColor = gold;
        GUI.Label(new Rect(r.x, r.y + 8f, r.width, 44f), (more ? "" : Loc.T("NEW: ")) + Loc.T(e.title), t);
        float y = r.y + 56f;
        if (clips.Count == 1) { GuideDemo.Draw(new Rect(r.x + 24f, y, inner, 252f), clips[0].id, shownAt); y += clipH + 12f; }
        else if (clips.Count > 1)
        {
            float w = (inner - 16f) / 2f;
            var lab = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = Loc.Bold, alignment = TextAnchor.MiddleCenter };
            lab.normal.textColor = gold;
            for (int i = 0; i < 2; i++)
            {
                float x = r.x + 24f + i * (w + 16f);
                GUI.Label(new Rect(x, y, w, 24f), Loc.T(clips[i].label), lab);
                GuideDemo.Draw(new Rect(x, y + 26f, w, w * 9f / 16f), clips[i].id, shownAt);
            }
            y += clipH + 12f;
        }
        GUI.Label(new Rect(r.x + 24f, y, inner, capH), Loc.T(e.caption), cap); y += capH + 8f;
        if (showMore)
            foreach (var line in e.lines)
            {
                float lh = b.CalcHeight(new GUIContent("•  " + Loc.T(line)), inner);
                GUI.Label(new Rect(r.x + 24f, y, inner, lh), "•  " + Loc.T(line), b); y += lh + 4f;
            }
        var hint = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
        hint.normal.textColor = new Color(0.78f, 1f, 0.37f);
        GUI.Label(new Rect(r.x, r.yMax - 36f, r.width, 28f), more ? Loc.T("tap to go back") : Loc.T("tap to continue   ·   ? = guide book"), hint);
    }

    static Texture2D Ring()
    {
        if (ringTex != null) return ringTex;
        const int N = 128;
        ringTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float r = new Vector2(x + 0.5f - N / 2f, y + 0.5f - N / 2f).magnitude / (N / 2f);
                ringTex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - Mathf.Abs(r - 0.9f) * 14f)));
            }
        ringTex.Apply();
        return ringTex;
    }
}
