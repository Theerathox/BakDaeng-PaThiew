using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// The PLAYER barks / calls into the microphone to make the dog bark.
// Human-only filter: it fires only on a HELD, voiced sound (a steady pitch in the human
// voice range for ~0.35 s, e.g. "hooong~"). A real dog's bark is short and noisy, so it is ignored.
// Fallbacks: on-screen BARK button, or the B key.
// Mic settings (device, loudness, clearness, hold time) are tuned on the setup screen's MIC tab
// (PetZoneDetector) and saved in PlayerPrefs, like the camera settings.
public class VoiceTrigger : MonoBehaviour
{
    public static event Action OnVoice;
    // a LOUD held call (past the second mark) - asks the spirit for its shield (SpiritShield, level 11+)
    public static event Action OnBigVoice;
    public static VoiceTrigger Instance { get; private set; }

    [Header("Microphone")]
    public bool useMicrophone = true;
    [Tooltip("Minimum loudness (RMS). Raise it in noisy rooms.")]
    [Range(0.001f, 0.2f)] public float loudness = 0.02f;
    [Tooltip("How periodic (voiced) the sound must be, 0..1")]
    [Range(0.2f, 0.95f)] public float voicedness = 0.45f;
    [Tooltip("Seconds the voice must be held. Dog barks are shorter than this.")]
    public float holdTime = 0.35f;
    public float cooldown = 0.8f;
    public float minPitchHz = 80f, maxPitchHz = 450f;
    [Tooltip("Second mark: a call this many times the bark loudness = spirit shield (only where SpiritShield is unlocked)")]
    public float shieldLoudness = 3f;

    [Header("UI")]
    public bool showButton = true;

    const int Rate = 16000, Window = 1024;
    const string PrefKey = "mic.settings.v1";
    AudioClip mic;
    string device;
    readonly float[] buf = new float[Window];
    float level, held, cool, pitch, clarity, peak;
    bool voiced, latched;

    // read by the MIC setup tab
    public float Level => level;
    public float Pitch => pitch;
    public float Clarity => clarity;     // 0..1 how voice-like the sound is right now
    public bool Voiced => voiced;
    public float Held => held;
    public bool MicOn => mic != null;
    public string DeviceName => device;
    public float LastDetected { get; private set; } = -99f;   // unscaled time of the last held "woof" (also during setup)

    [Serializable]
    public class Settings { public string device; public bool use; public float loudness, voicedness, holdTime; }
    Settings defaults;

    public Settings Capture() => new Settings { device = device, use = useMicrophone, loudness = loudness, voicedness = voicedness, holdTime = holdTime };

    public void Apply(Settings s)
    {
        bool restart = s.use != useMicrophone || (s.device != null && s.device != device);
        useMicrophone = s.use; loudness = s.loudness; voicedness = s.voicedness; holdTime = s.holdTime;
        if (restart) StartMic(s.device);
    }

    public void ResetToDefaults() => Apply(defaults);

    public void Save() { PlayerPrefs.SetString(PrefKey, JsonUtility.ToJson(Capture())); PlayerPrefs.Save(); }

    public static void Fire() { OnVoice?.Invoke(); }

    void Awake()
    {
        Instance = this;
        defaults = Capture();
        if (PlayerPrefs.HasKey(PrefKey))
        {
            try
            {
                var s = JsonUtility.FromJson<Settings>(PlayerPrefs.GetString(PrefKey));
                useMicrophone = s.use; loudness = s.loudness; voicedness = s.voicedness; holdTime = s.holdTime; device = s.device;
            }
            catch { Debug.LogWarning("VoiceTrigger: saved mic settings unreadable"); }
        }
    }

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            var cb = new UnityEngine.Android.PermissionCallbacks();
            cb.PermissionGranted += _ => StartMic(device);
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, cb);
            return;
        }
#endif
        StartMic(device);
    }

    // (re)start the microphone; a missing / unknown name falls back to the first device
    public void StartMic(string name)
    {
        if (mic != null) { Microphone.End(device); mic = null; }
        level = 0f; voiced = false; held = 0f;
        var devs = Microphone.devices;
        if (!useMicrophone || devs.Length == 0) return;
        device = Array.IndexOf(devs, name) >= 0 ? name : devs[0];
        mic = Microphone.Start(device, true, 1, Rate);
    }

    public void StepDevice(int dir)
    {
        var devs = Microphone.devices;
        if (devs.Length == 0) return;
        int i = Mathf.Max(0, Array.IndexOf(devs, device));
        StartMic(devs[(i + dir + devs.Length) % devs.Length]);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;   // keeps working on the (paused) setup screen
        cool -= dt;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame) TryFire();
#else
        if (Input.GetKeyDown(KeyCode.B)) TryFire();
#endif
        if (mic == null || (GameState.IsOver && !PetZoneDetector.SetupOpen)) return;
        int pos = Microphone.GetPosition(device);
        if (pos <= 0) return;
        int start = pos - Window; if (start < 0) start += mic.samples;
        mic.GetData(buf, start);
        Analyse();

        if (!voiced) { held = 0f; latched = false; peak = 0f; }
        else if (!latched)
        {
            held += dt;
            peak = Mathf.Max(peak, level);
            if (held >= holdTime)   // must stop before the next bark
            {
                LastDetected = Time.unscaledTime; latched = true;
                if (SpiritShield.Available && peak >= loudness * shieldLoudness) TryFireBig(); else TryFire();
            }
        }
    }

    void TryFire()
    {
        if (cool > 0f || GameState.IsOver || GameState.Cutscene || PetZoneDetector.SetupOpen) return;
        cool = cooldown;
        Fire();
    }

    void TryFireBig()
    {
        if (cool > 0f || GameState.IsOver || GameState.Cutscene || PetZoneDetector.SetupOpen) return;
        cool = cooldown;
        OnBigVoice?.Invoke();
    }

    // RMS loudness + autocorrelation pitch / periodicity
    void Analyse()
    {
        double e = 0; for (int i = 0; i < Window; i++) e += buf[i] * buf[i];
        float rms = Mathf.Sqrt((float)(e / Window));
        level = Mathf.Lerp(level, rms, 0.4f);
        voiced = false;
        if (rms < loudness * 0.5f || e <= 0) { clarity = Mathf.Lerp(clarity, 0f, 0.3f); return; }
        int rate = mic != null ? mic.frequency : Rate;
        int minLag = Mathf.FloorToInt(rate / maxPitchHz), maxLag = Mathf.Min(Window / 2, Mathf.CeilToInt(rate / minPitchHz));
        float best = 0f; int bestLag = 0;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double s = 0;
            for (int i = 0; i < Window - lag; i++) s += buf[i] * buf[i + lag];
            float r = (float)(s / e);
            if (r > best) { best = r; bestLag = lag; }
        }
        clarity = Mathf.Lerp(clarity, Mathf.Clamp01(best), 0.5f);
        voiced = rms >= loudness && best >= voicedness;
        if (best >= voicedness && bestLag > 0) pitch = (float)rate / bestLag;
    }

    void OnGUI()
    {
        if (PetZoneDetector.SetupOpen) return;
        float sc = Mathf.Max(1f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
        float W = Screen.width / sc, H = Screen.height / sc;

        // mic meter, bottom-left (tap MIC to open the mic setup)
        var r = new Rect(16f, H - 40f, 180f, 14f);
        GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(r, Texture2D.whiteTexture);
        float k = Mathf.Clamp01(level / (loudness * 4f));
        GUI.color = voiced ? new Color(0.5f, 0.82f, 0.76f) : new Color(0.66f, 0.69f, 0.78f);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * k, r.height), Texture2D.whiteTexture);
        GUI.color = new Color(0.91f, 0.64f, 0.23f);
        GUI.DrawTexture(new Rect(r.x + r.width * 0.25f, r.y - 2f, 2f, r.height + 4f), Texture2D.whiteTexture);
        if (SpiritShield.Available)   // second mark: call this loud for the spirit shield
        {
            GUI.color = new Color(1f, 0.85f, 0.4f);
            GUI.DrawTexture(new Rect(r.x + r.width * Mathf.Clamp01(shieldLoudness / 4f) - 1f, r.y - 4f, 3f, r.height + 8f), Texture2D.whiteTexture);
            GUI.color = new Color(0.91f, 0.64f, 0.23f);
        }
        if (held > 0f) GUI.DrawTexture(new Rect(r.x, r.y + r.height + 2f, r.width * Mathf.Clamp01(held / holdTime), 3f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        var st = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        st.normal.textColor = new Color(0.79f, 0.81f, 0.87f);
        GUI.Label(new Rect(16f, H - 62f, 200f, 20f), mic == null ? Loc.T("MIC OFF  (press B / BARK)") : Loc.T("MIC  - hold a 'woof' to bark"), st);
        if (GUI.Button(new Rect(204f, H - 52f, 52f, 30f), Loc.T("MIC")) && PetZoneDetector.Instance != null) PetZoneDetector.Instance.OpenSetup(3);

        // BARK button, bottom-right (thumb reach on phones)
        if (showButton && GUI.Button(new Rect(W - 130f, H - 76f, 110f, 56f), Loc.T("BARK"))) TryFire();
    }

    void OnDestroy() { if (mic != null) Microphone.End(device); if (Instance == this) Instance = null; }
}
