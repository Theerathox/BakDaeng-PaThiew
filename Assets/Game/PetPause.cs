using UnityEngine;
using UnityEngine.SceneManagement;

// The pet walked out of the picture? The game waits instead of going on without it.
// After 'lostTime' seconds with no pet seen (camera running, pet controls), the level pauses with a
// "Where is your pet?" screen and resumes by itself as soon as the pet is back. "Play with buttons"
// ignores it until the pet is seen again. Off: SETUP > POSES > "Pause when the pet leaves".
// Created automatically, nothing to add to the scenes.
public class PetPause : MonoBehaviour
{
    public static bool Paused { get; private set; }
    public float lostTime = 1.5f;
    public float backTime = 0.4f;

    float lost, back, prevScale = 1f;
    bool ignoreUntilSeen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("PetPause");
        DontDestroyOnLoad(go);
        go.AddComponent<PetPause>();
        SceneManager.sceneLoaded += (s, m) => Paused = false;
    }

    void Update()
    {
        var det = PetZoneDetector.Instance;
        var dog = det != null ? FindFirstObjectByType<DogWalker>() : null;
        bool watching = det != null && det.isActiveAndEnabled && det.pauseWhenLost && det.CameraRunning
                        && dog != null && dog.mode == DogWalker.Mode.PetCamera
                        && GameState.Started && !GameState.IsOver && !GameState.Cutscene
                        && !PetZoneDetector.SetupOpen && !LevelSelect.Open && !LevelTransition.Busy && !Guide.Open;
        float dt = Time.unscaledDeltaTime;
        if (det != null && det.HasAnimal) ignoreUntilSeen = false;

        if (!Paused)
        {
            lost = watching && !ignoreUntilSeen && !det.HasAnimal ? lost + dt : 0f;
            if (lost >= lostTime) { Paused = true; back = 0f; prevScale = Time.timeScale > 0f ? Time.timeScale : 1f; Time.timeScale = 0f; }
            return;
        }
        if (!watching && !PetZoneDetector.SetupOpen && !LevelSelect.Open) { Resume(false); return; }   // level ended / changed
        if (!PetZoneDetector.SetupOpen && !LevelSelect.Open) Time.timeScale = 0f;   // stay paused (e.g. after the setup screen)
        back = det.HasAnimal ? back + dt : 0f;
        if (back >= backTime) Resume(true);
    }

    void Resume(bool welcome)
    {
        Paused = false; lost = 0f;
        if (!GameState.IsOver && !PetZoneDetector.SetupOpen && !LevelSelect.Open) Time.timeScale = prevScale;
        if (welcome) GameState.Toast(Loc.T("Welcome back! Let's go."));
    }

    void OnGUI()
    {
        if (!Paused || PetZoneDetector.SetupOpen || LevelSelect.Open) return;
        GUI.depth = -20;
        float s = Mathf.Max(1f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = Screen.height / s;
        GUI.color = new Color(0.02f, 0.03f, 0.07f, 0.85f); GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture); GUI.color = Color.white;

        var t = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = Loc.Bold, alignment = TextAnchor.MiddleCenter };
        t.normal.textColor = new Color(0.91f, 0.64f, 0.23f);
        GUI.Label(new Rect(0, H * 0.1f, W, 60f), Loc.T("WHERE IS YOUR PET?"), t);
        var b = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        b.normal.textColor = new Color(0.95f, 0.92f, 0.87f);
        GUI.Label(new Rect(W * 0.15f, H * 0.1f + 60f, W * 0.7f, 50f), Loc.T("Bring your pet back in front of the camera - the game waits for you."), b);

        var det = PetZoneDetector.Instance;
        if (det != null && det.CameraTexture != null)   // live picture so you can see what the camera sees
        {
            float pw = Mathf.Min(420f, W * 0.4f), ph = pw * det.CameraTexture.height / Mathf.Max(1f, det.CameraTexture.width);
            var r = new Rect(W / 2f - pw / 2f, H * 0.1f + 120f, pw, ph);
            GUI.DrawTextureWithTexCoords(r, det.CameraTexture, det.mirror ? new Rect(1, 0, -1, 1) : new Rect(0, 0, 1, 1));
            var hint = new GUIStyle(b) { fontSize = 16 };
            GUI.Label(new Rect(r.x, r.yMax + 6f, r.width, 26f), det.HasAnimal ? Loc.T("Found you!") : Loc.T("looking for your pet..."), hint);
        }

        var btn = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = Loc.Bold };
        if (GUI.Button(new Rect(W / 2f - 250f, H - 110f, 240f, 56f), Loc.T("PLAY WITH BUTTONS"), btn)) { ignoreUntilSeen = true; Resume(false); }
        if (GUI.Button(new Rect(W / 2f + 10f, H - 110f, 240f, 56f), Loc.T("CAMERA SETUP"), btn) && det != null) det.OpenSetup(0);
    }
}
