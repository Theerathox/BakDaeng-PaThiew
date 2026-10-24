using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// The guardian spirit's gift (after the great shrine, level 11+). Call LOUDER than a bark - past the
// second mark on the mic meter (VoiceTrigger.OnBigVoice) - or press S / the SHIELD button:
// a circle of holy light surrounds the master for 'duration' seconds. While it lasts zombies and the
// horde can't hurt him and won't step into it, so he can walk straight through a crowd.
// Then it must recharge ('cooldown' counts from the moment it was called).
// Put one on the "Game" object of a level to unlock it there.
public class SpiritShield : MonoBehaviour
{
    public float duration = 5f;
    public float cooldown = 12f;
    public float radius = 1.8f;

    public static SpiritShield Instance { get; private set; }
    public static bool Available => Instance != null && Instance.isActiveAndEnabled && Abilities.Has(Ability.Shield);
    public bool Active => holy != null && holy.gameObject.activeSelf;
    public float ReadyIn => Mathf.Max(0f, readyAt - Time.time);

    Transform master;
    Lantern holy;
    SpriteRenderer glow, rim;
    LightHole hole;
    float until, readyAt;
    static Sprite disc;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }
    void OnEnable() { VoiceTrigger.OnBigVoice += Call; }
    void OnDisable() { VoiceTrigger.OnBigVoice -= Call; }

    void Start()
    {
        var m = FindFirstObjectByType<BlindFollower>();
        if (m == null) return;
        master = m.transform;
        var go = new GameObject("SpiritShield_Light");
        go.transform.SetParent(master, false);
        go.SetActive(false);
        holy = go.AddComponent<Lantern>();   // a moving candle holy: same rules (zombies stay out, master is safe)
        holy.radius = radius;
        if (disc == null) disc = MakeDisc();
        glow = MakeCircle(go.transform, "glow", radius * 2f, new Color(1f, 0.85f, 0.4f, 0.1f), Darkness.DarkOrder + 2);   // light: the master stays visible inside
        rim = MakeCircle(go.transform, "rim", radius * 2f, new Color(1f, 0.9f, 0.55f, 0.5f), Darkness.DarkOrder + 3);
    }

    SpriteRenderer MakeCircle(Transform parent, string name, float size, Color c, int order)
    {
        var sr = new GameObject(name).AddComponent<SpriteRenderer>();
        sr.transform.SetParent(parent, false);
        sr.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        sr.transform.localScale = Vector3.one * size;
        sr.sprite = disc; sr.color = c; sr.sortingOrder = order;
        return sr;
    }

    static Sprite MakeDisc()   // soft-edged circle, 1 unit across
    {
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float r = new Vector2(x + 0.5f - N / 2f, y + 0.5f - N / 2f).magnitude / (N / 2f);
                float a = r > 1f ? 0f : Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0.6f, 1f, r)) * Mathf.Clamp01((1f - r) * 12f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
    }

    public void Call()
    {
        if (holy == null || !GameState.Started || GameState.IsOver || GameState.Cutscene || LevelSelect.Open || PetPause.Paused || Guide.Open) return;
        if (Active) return;
        if (Time.time < readyAt) { GameState.Toast(Loc.F("The spirit is gathering its power... {0} s", ReadyIn.ToString("0"))); return; }
        until = Time.time + duration;
        readyAt = Time.time + cooldown;
        holy.gameObject.SetActive(true);
        GameState.Toast(Loc.T("SPIRIT SHIELD! Walk through them - quick!"));
        Sfx.Play("shield", 0.8f);
        var dark = FindFirstObjectByType<Darkness>();
        if (dark != null) hole = dark.AddLight(master, new Vector3(0f, 1.2f, 0f), radius + 0.3f, -1f, 0f, true);
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.sKey.wasPressedThisFrame) Call();
#else
        if (Input.GetKeyDown(KeyCode.S)) Call();
#endif
        if (!Active) return;
        float left = until - Time.time;
        float pulse = 1f + 0.05f * Mathf.Sin(Time.time * 8f);
        bool blink = left < 1.2f && Mathf.Repeat(Time.time, 0.25f) < 0.12f;   // about to fade
        glow.transform.localScale = Vector3.one * radius * 2f * pulse;
        var c = rim.color; c.a = blink ? 0.15f : 0.5f; rim.color = c;
        if (left <= 0f || GameState.IsOver)
        {
            holy.gameObject.SetActive(false);
            if (hole != null) { Destroy(hole.gameObject); hole = null; }
            if (!GameState.IsOver) { GameState.Toast(Loc.T("The shield faded!")); Sfx.Play("ping", 0.5f, 0.7f); }
        }
    }

    // shield charge, above the mic meter (bottom-left) + a SHIELD button above BARK (bottom-right)
    void OnGUI()
    {
        if (holy == null || PetZoneDetector.SetupOpen || LevelSelect.Open || GameState.IsOver) return;
        float s = Mathf.Max(1f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = Screen.height / s;
        var r = new Rect(16f, H - 92f, 180f, 18f);
        GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(r, Texture2D.whiteTexture);
        float k = Active ? Mathf.Clamp01((until - Time.time) / duration) : 1f - Mathf.Clamp01(ReadyIn / Mathf.Max(0.01f, cooldown - duration));
        GUI.color = Active ? new Color(1f, 0.85f, 0.4f) : k >= 1f ? new Color(0.5f, 0.82f, 0.76f) : new Color(0.55f, 0.55f, 0.6f);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * k, r.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        var st = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = Loc.Bold, alignment = TextAnchor.MiddleCenter };
        st.normal.textColor = Color.black;
        GUI.Label(r, Active ? Loc.T("SHIELD ON") : ReadyIn <= 0f ? Loc.T("SHIELD READY - call LOUD!") : Loc.T("SHIELD RECHARGING"), st);
        if (GUI.Button(new Rect(W - 130f, H - 140f, 110f, 56f), Loc.T("SHIELD"))) Call();
    }
}
