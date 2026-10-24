using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Win/lose state, level intro card, short hint toasts and the end-of-level overlay.
// Reaching a level's exit does NOT stop the game: dog and master keep walking off the right edge,
// the screen scrolls on (LevelTransition) and they walk in from the left of the next level.
// Lose: R / Restart reloads the level. Win overlay (shrine, last level): Next scrolls on to the next level.
public class GameState : MonoBehaviour
{
    [Tooltip("Shown on the intro card, e.g. 'Level 1 - First Steps'")]
    public string levelTitle = "";
    [Tooltip("One-line goal shown under the title")]
    public string levelGoal = "";
    public float introTime = 2.5f;
    [Tooltip("Dog and master walk in from this far left when arriving from the previous level")]
    public float walkInDistance = 7f;
    bool cleared;
    Ability newAbility; float abilityLeft, abilityShown;
    string goalText = "";

    public static bool IsOver { get; private set; }
    public static bool Won { get; private set; }
    // walk-in / title / walk-out: players can't control, zombies wait, nobody gets hurt
    public static bool Cutscene { get; private set; }
    // false until the walk-in and the title card are done; zombies only appear after that
    public static bool Started { get; private set; }
    static string reason = "";
    static float loseAt;
    static string toast = "";
    static float toastUntil;
    static GameState inst;
    float introLeft;

    void Awake()
    {
        inst = this;
        IsOver = false; Won = false; Cutscene = true; Started = false; toast = ""; cleared = false;
        Time.timeScale = 1f; introLeft = 0f;
    }

    void Start() { StartCoroutine(Intro()); }

    // walk in (when coming from the previous level) -> title card -> zombies appear, play
    IEnumerator Intro()
    {
        Cutscene = true;
        if (LevelTransition.Arrived) { LevelTransition.Arrived = false; yield return WalkIn(); }
        if (levelTitle.Length > 0)
        {
            introLeft = introTime;
            while (introLeft > 0f) yield return null;   // counts down in Update (paused while the setup screen is open)
        }
        // a trick learned in this level: show how to do it (tap / Space to go on)
        foreach (Ability a in System.Enum.GetValues(typeof(Ability)))
        {
            if (!Abilities.NewHere(a)) continue;
            newAbility = a; abilityLeft = 12f; abilityShown = Time.unscaledTime;
            Sfx.Play("clear", 0.8f);
            while (abilityLeft > 0f) yield return null;
            abilityLeft = 0f;
        }
        if (Abilities.Has(Ability.Shield) && FindFirstObjectByType<SpiritShield>() == null) gameObject.AddComponent<SpiritShield>();
        Cutscene = false;
        Started = true;
    }

    public static void Lose(string why)
    {
        if (IsOver || Cutscene || !Started) return;
        IsOver = true; Won = false;
        reason = why; loseAt = Time.unscaledTime;   // the master falls first, then the CAUGHT! screen
        if (SoundBank.Clip("zombiekill") != null)
        {
            Sfx.Play("zombiekill");                                   // the zombie gets him...
            if (inst != null) inst.StartCoroutine(inst.LoseSoundLater());   // ...then the sad tune with the CAUGHT! card
        }
        else Sfx.Play("lose");
        Time.timeScale = 0f;
    }

    System.Collections.IEnumerator LoseSoundLater()
    {
        yield return new WaitForSecondsRealtime(1.3f);
        if (IsOver && !Won) Sfx.Play("lose");
    }

    public static void Win(string msg)
    {
        if (IsOver) return;
        IsOver = true; Won = true;
        reason = msg;
        Sfx.Play("win");
        Time.timeScale = 0f;
    }

    // called by LevelExit: scroll on to the next level, or the final win screen on the last one
    public static void LevelComplete(float exitX)
    {
        if (IsOver || Cutscene || !Started || inst == null) return;
        if (!HasNext) { Win(Loc.T("You made it out of town. THE END - thanks for playing with your pet!")); return; }
        inst.StartCoroutine(inst.WalkOut(exitX));
    }

    // a goal that ends the level on the spot (shelter, shrine, taxi): message card, then on to the next scene
    // (often a cutscene). The very last scene of the game shows the win screen instead.
    public static void ReachGoal(string msg)
    {
        if (IsOver || Cutscene || !Started || inst == null) return;
        if (!HasNext) { Win(msg); return; }
        inst.StartCoroutine(inst.GoalOut(msg));
    }

    IEnumerator GoalOut(string msg)
    {
        Cutscene = true; cleared = true; goalText = msg;
        Sfx.Play("win");
        yield return new WaitForSeconds(3f);
        LevelTransition.Go(SceneManager.GetActiveScene().buildIndex + 1);
    }

    IEnumerator WalkOut(float exitX)
    {
        Cutscene = true; cleared = true;
        Sfx.Play("clear");
        var dog = FindFirstObjectByType<DogWalker>();
        if (dog != null) dog.AutoWalkTo(exitX + 14f);
        yield return new WaitForSeconds(1.4f);
        LevelTransition.Go(SceneManager.GetActiveScene().buildIndex + 1);
    }

    IEnumerator WalkIn()
    {
        var dog = FindFirstObjectByType<DogWalker>();
        var master = FindFirstObjectByType<BlindFollower>();
        if (dog != null)
        {
            float startX = dog.transform.position.x;
            var p = dog.transform.position; p.x = Mathf.Max(startX - walkInDistance, dog.minX); dog.transform.position = p;
            if (master != null) { var m = master.transform.position; m.x = p.x - 2.5f; master.transform.position = m; }
            dog.AutoWalkTo(startX);
            float t = 0f;
            while (dog.AutoWalking && t < 8f) { t += Time.deltaTime; yield return null; }
        }
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

    public static void Toast(string msg) { toast = Loc.T(msg); toastUntil = Time.unscaledTime + 3f; }

    void Update()
    {
        if (!PetZoneDetector.SetupOpen && introLeft > 0f) introLeft -= Time.unscaledDeltaTime;
        if (!PetZoneDetector.SetupOpen && !LevelSelect.Open && abilityLeft > 0f)
        {
            abilityLeft -= Time.unscaledDeltaTime;
            if (Time.unscaledTime - abilityShown > 0.8f && Tapped()) abilityLeft = 0f;   // read it, then tap to play
        }
        if (!IsOver || LevelSelect.Open) return;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null && k.rKey.wasPressedThisFrame) Restart();
        if (k != null && Won && k.nKey.wasPressedThisFrame) Next();
#else
        if (Input.GetKeyDown(KeyCode.R)) Restart();
        if (Won && Input.GetKeyDown(KeyCode.N)) Next();
#endif
    }

    public static void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // next scene only counts if it is another level or a story cutscene (test scenes may follow the levels in Build Settings)
    static bool HasNext
    {
        get
        {
            int i = SceneManager.GetActiveScene().buildIndex + 1;
            if (i >= SceneManager.sceneCountInBuildSettings) return false;
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            return path.Contains("/Levels/") || path.Contains("/Cutscenes/");
        }
    }

    static void Next()
    {
        Time.timeScale = 1f;
        LevelTransition.Go(HasNext ? SceneManager.GetActiveScene().buildIndex + 1 : 0);
    }

    void OnGUI()
    {
        if (PetZoneDetector.SetupOpen) return;
        float s = Mathf.Max(1f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = Screen.height / s;
        var ink = new Color(0.95f, 0.92f, 0.87f);

        // hint toast (top-left, away from the pet cam window)
        if (!IsOver && toast.Length > 0 && Time.unscaledTime < toastUntil)
        {
            var st = Style(20, ink, Loc.Bold);
            float w = Mathf.Min(W - 420f, st.CalcSize(new GUIContent(toast)).x + 40f);
            var r = new Rect(24f, 72f, w, 44f);   // below the RETRY / LEVELS buttons
            GUI.color = new Color(0f, 0f, 0f, 0.65f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.Label(r, toast, st);
        }

        // level clear card
        if (cleared && !IsOver)
        {
            bool goal = goalText.Length > 0;
            var r = new Rect(W / 2f - 330f, H * 0.28f, 660f, goal ? 130f : 80f);
            GUI.color = new Color(0f, 0f, 0f, 0.7f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.Label(new Rect(r.x, r.y + 10f, r.width, 56f), Loc.T("LEVEL CLEAR!"), Style(40, new Color(0.5f, 0.82f, 0.76f), Loc.Bold));
            if (goal)
            {
                var st = Style(19, ink, FontStyle.Normal); st.wordWrap = true;
                GUI.Label(new Rect(r.x + 20f, r.y + 66f, r.width - 40f, 56f), Loc.T(goalText), st);
            }
        }

        // new ability card (with a short demo clip when there is one)
        if (!IsOver && abilityLeft > 0f)
        {
            var a = newAbility;
            string clip = a == Ability.Crawl ? "crawl" : a == Ability.Sniff ? "key" : a == Ability.Run ? "run" : "shield";
            bool hasClip = GuideDemo.Has(clip);
            var r = hasClip ? new Rect(W / 2f - 340f, Mathf.Max(70f, H * 0.5f - 252f), 600f, 505f) : new Rect(W / 2f - 340f, H * 0.2f, 600f, 320f);   // left of the PET CAM window
            GUI.color = new Color(0.03f, 0.04f, 0.08f, 0.92f); GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.85f, 0.4f); GUI.DrawTexture(new Rect(r.x, r.y, r.width, 4f), Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.Label(new Rect(r.x, r.y + 14f, r.width, 50f), Abilities.Title(a), Style(36, new Color(1f, 0.85f, 0.4f), Loc.Bold));
            float y = r.y + 68f;
            if (hasClip) { GuideDemo.Draw(new Rect(r.x + 24f, y, r.width - 48f, 245f), clip, abilityShown); y += 257f; }
            var body = Style(22, ink, Loc.Bold); body.wordWrap = true;
            GUI.Label(new Rect(r.x + 24f, y, r.width - 48f, 56f), Abilities.Pet(a), body); y += 58f;
            var what = Style(hasClip ? 17 : 18, ink, FontStyle.Normal); what.wordWrap = true;
            GUI.Label(new Rect(r.x + 24f, y, r.width - 48f, hasClip ? 48f : 70f), Abilities.What(a), what); y += hasClip ? 50f : 82f;
            GUI.Label(new Rect(r.x + 24f, y, r.width - 48f, 30f), Abilities.Button(a), Style(16, new Color(0.66f, 0.69f, 0.78f), FontStyle.Normal));
            if (Time.unscaledTime - abilityShown > 0.8f) GUI.Label(new Rect(r.x, r.yMax - 40f, r.width, 30f), Loc.T("tap to play"), Style(16, new Color(0.78f, 1f, 0.37f), Loc.Bold));
        }

        // intro card
        if (!IsOver && introLeft > 0f && levelTitle.Length > 0)
        {
            float a = Mathf.Clamp01(introLeft / 0.6f);
            var r = new Rect(W / 2f - 330f, H * 0.3f, 660f, 110f);
            GUI.color = new Color(0f, 0f, 0f, 0.7f * a); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.Label(new Rect(r.x, r.y + 10f, r.width, 50f), Loc.T(levelTitle), Style(34, new Color(0.78f, 1f, 0.37f, a), Loc.Bold));
            GUI.Label(new Rect(r.x, r.y + 60f, r.width, 36f), Loc.T(levelGoal), Style(20, new Color(ink.r, ink.g, ink.b, a), FontStyle.Normal));
        }

        if (!IsOver) return;
        if (!Won && Time.unscaledTime - loseAt < 1.3f) return;   // let the fall play
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(0, H / 2f - 90f, W, 70f), Won ? Loc.T("CLEAR!") : Loc.T("CAUGHT!"),
                  Style(56, Won ? new Color(0.5f, 0.82f, 0.76f) : new Color(0.78f, 1f, 0.37f), Loc.Bold));
        GUI.Label(new Rect(0, H / 2f - 20f, W, 30f), Loc.T(reason), Style(20, ink, FontStyle.Normal));
        var btn = new GUIStyle(GUI.skin.button) { fontSize = 20 };
        if (Won)
        {
            if (GUI.Button(new Rect(W / 2f - 200f, H / 2f + 30f, 190f, 54f), Loc.T("Retry (R)"), btn)) Restart();
            if (GUI.Button(new Rect(W / 2f + 10f, H / 2f + 30f, 190f, 54f), HasNext ? Loc.T("Next (N)") : Loc.T("Play again (N)"), btn)) Next();
        }
        else if (GUI.Button(new Rect(W / 2f - 95f, H / 2f + 30f, 190f, 54f), Loc.T("Restart (R)"), btn)) Restart();
    }

    static GUIStyle Style(int size, Color c, FontStyle f)
    {
        var st = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = f, alignment = TextAnchor.MiddleCenter };
        st.normal.textColor = c;
        return st;
    }
}
