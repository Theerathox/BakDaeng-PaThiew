using UnityEngine;
using UnityEngine.SceneManagement;

// Background music: the day track in the morning levels / title / day cutscenes, the night track at night
// (a scene with Darkness, or a night cutscene). Soft (a pet sits next to the phone), fades between
// tracks, and goes quiet while a mor lam radio is playing nearby or the game is paused.
// Created automatically; the tracks come from Resources/SoundBank.
public class BackgroundMusic : MonoBehaviour
{
    AudioSource src;
    AudioClip want;
    float fade = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (SoundBank.I == null || (SoundBank.I.day == null && SoundBank.I.night == null)) return;
        var go = new GameObject("BackgroundMusic");
        go.transform.position = new Vector3(0f, -500f, 0f);   // 2D sound: keeps the speaker gizmo off screen
        DontDestroyOnLoad(go);
        go.AddComponent<BackgroundMusic>();
    }

    void Awake()
    {
        src = gameObject.AddComponent<AudioSource>();
        src.loop = true; src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 0f;
        SceneManager.sceneLoaded += (s, m) => Pick();
        Pick();
    }

    void Pick()
    {
        var b = SoundBank.I;
        bool night = FindFirstObjectByType<Darkness>() != null;
        var cut = FindFirstObjectByType<CutscenePlayer>();
        if (cut != null) night = cut.story == CutscenePlayer.Story.Shelter || cut.story == CutscenePlayer.Story.Spirit;
        want = night ? (b.night != null ? b.night : b.day) : (b.day != null ? b.day : b.night);
    }

    void Update()
    {
        var b = SoundBank.I;
        if (b == null) return;
        float dt = Time.unscaledDeltaTime;
        if (src.clip != want)   // fade out, swap, fade in
        {
            fade = Mathf.MoveTowards(fade, 0f, dt * 1.5f);
            if (fade <= 0f || !src.isPlaying) { src.clip = want; if (want != null) src.Play(); }
        }
        else fade = Mathf.MoveTowards(fade, 1f, dt * 0.8f);

        float duck = 1f;
        var radio = Radio.Heard(Camera.main != null ? Camera.main.transform.position.x : 0f, 14f);
        if (radio != null) duck = 0.15f;                    // the radio's song takes over
        if (PetPause.Paused || PetZoneDetector.SetupOpen) duck = Mathf.Min(duck, 0.4f);
        src.volume = b.musicVolume * Sfx.Volume * fade * duck;
    }
}
