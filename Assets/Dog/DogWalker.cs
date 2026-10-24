using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Moves the rigged dog left/right and drives its Animator ("IsWalking").
// Modes:
//   AutoPatrol  - walks back and forth between minX and maxX, pausing at each end
//   Keyboard    - Left/Right arrows (or A/D)
//   PetCamera   - uses PetZoneDetector: 5 zones (run back / back / stop / forward / run forward) + poses
// RUN   (outer zone, Shift, RUN button): fast - your master runs too - but LOUD: footsteps wake sleepers
//                                         and bring zombies to look (a small noise every step)
// CRAWL (pet LOW in the camera picture, C, CRAWL button): slow but SILENT - cans don't rattle, your master tiptoes
[RequireComponent(typeof(Animator))]
public class DogWalker : MonoBehaviour
{
    public enum Mode { AutoPatrol, Keyboard, PetCamera }

    public Mode mode = Mode.AutoPatrol;
    public float speed = 1.6f;
    public float runSpeed = 3.0f;
    public float crawlSpeed = 0.75f;
    [Tooltip("While running, a footstep noise this loud (1 = a bark) every 'stepNoiseEvery' seconds")]
    public float stepNoise = 0.5f;
    public float stepNoiseEvery = 0.6f;
    public float minX = -7.5f;
    public float maxX = 7.5f;
    public float pauseTime = 1.5f;
    [Tooltip("Only used in PetCamera mode")]
    public PetZoneDetector detector;
    [Tooltip("On-screen ◀ ▶ buttons (phones / testing without a pet)")]
    public bool touchButtons = true;

    Animator anim;
    float touchAxis;
    bool touchRun, touchCrawl;
    float stepTimer, pawTimer;

    public bool Running { get; private set; }     // moving at run speed
    public bool Crouching { get; private set; }   // crawling (also when standing still with the pet low in the picture)
    // set by the editor-only LevelSolver (automatic play-tests)
    public static bool TestRun, TestCrawl;
    bool autoWalk; float autoTarget;

    // scripted walk (level walk-in / walk-out): ignores player input and level edges
    public void AutoWalkTo(float x) { autoWalk = true; autoTarget = x; }
    public bool AutoWalking => autoWalk;
    float dir = 1f;
    float pause;
    float baseScaleX, baseScaleY = 1f;
    static readonly int IsWalking = Animator.StringToHash("IsWalking");

    void Start()
    {
        anim = GetComponent<Animator>();
        baseScaleX = Mathf.Abs(transform.localScale.x);
        baseScaleY = Mathf.Abs(transform.localScale.y);
        if (detector == null) detector = FindFirstObjectByType<PetZoneDetector>();
        if (GetComponent<DogBark>() != null && GetComponent<DogSniff>() == null) gameObject.AddComponent<DogSniff>();   // levels built before sniffing existed
    }

    void Update()
    {
        if (autoWalk && anim != null)
        {
            float d = autoTarget - transform.position.x;
            if (Mathf.Abs(d) < 0.05f) { autoWalk = false; anim.SetBool(IsWalking, false); return; }
            var ap = transform.position;
            ap.x += Mathf.Sign(d) * Mathf.Min(speed * Time.deltaTime, Mathf.Abs(d));
            transform.position = ap;
            anim.SetBool(IsWalking, true);
            Face(Mathf.Sign(d));
            return;
        }
        if (GameState.IsOver || GameState.Cutscene) { anim.SetBool(IsWalking, false); Running = false; anim.speed = 1f; return; }
        float move = 0f;
        bool run = KeyRun() || touchRun || TestRun, crawl = KeyCrawl() || touchCrawl || TestCrawl;
        switch (mode)
        {
            case Mode.AutoPatrol: move = Patrol(); break;
            case Mode.Keyboard: move = ManualAxis(); break;
            case Mode.PetCamera:
                if (detector != null)
                {
                    move = PetZoneDetector.Dir(detector.CurrentMove);
                    run |= PetZoneDetector.IsRun(detector.CurrentMove);
                    crawl |= detector.Crouching;
                }
                // arrow keys / A-D / on-screen buttons still work, handy for testing without a pet
                float k = ManualAxis();
                if (Mathf.Abs(k) > 0.01f) move = k;
                break;
        }

        run &= Abilities.Has(Ability.Run);       // learned later in the story (Abilities)
        crawl &= Abilities.Has(Ability.Crawl);
        bool walking = Mathf.Abs(move) > 0.01f;
        Crouching = crawl;
        Running = walking && run && !crawl;
        anim.SetBool(IsWalking, walking);
        anim.speed = !walking ? 1f : crawl ? 0.55f : Running ? 1.8f : 1f;
        if (!walking) return;

        if (Running && stepNoise > 0f)   // running is loud
        {
            stepTimer -= Time.deltaTime;
            if (stepTimer <= 0f) { stepTimer = stepNoiseEvery; Noise.Emit(transform.position, stepNoise); }
        }
        float v = crawl ? crawlSpeed : Running ? runSpeed : speed;
        var p = transform.position;
        float nx = Mathf.Clamp(p.x + move * v * Time.deltaTime, minX, maxX);
        float before = p.x;
        p.x = Gate.ClampMove(p.x, nx, 1.2f);   // closed gates block the dog
        transform.position = p;
        Face(Mathf.Sign(move));
        // soft paw taps (none while crawling - crawling is silent)
        if (!crawl && Mathf.Abs(p.x - before) > 1e-4f)
        {
            pawTimer -= Time.deltaTime;
            if (pawTimer <= 0f) { pawTimer = Running ? 0.17f : 0.26f; Sfx.Play("paw", Running ? 0.3f : 0.2f, Random.Range(0.9f, 1.15f)); }
        }
    }

    float Patrol()
    {
        if (pause > 0f) { pause -= Time.deltaTime; return 0f; }
        float x = transform.position.x;
        if (dir > 0f && x >= maxX - 0.001f) { dir = -1f; pause = pauseTime; Face(dir); return 0f; }
        if (dir < 0f && x <= minX + 0.001f) { dir = 1f; pause = pauseTime; Face(dir); return 0f; }
        return dir;
    }

    // set by the editor-only LevelSolver (automatic play-tests); 0 in normal play
    public static float TestAxis;
    float ManualAxis() => Mathf.Clamp(KeyboardAxis() + touchAxis + TestAxis, -1f, 1f);

    // crawling: the dog squashes down onto its feet (after the Animator has posed it)
    // crawling is drawn by DogPose (legs bend, body goes low) - the dog itself is not squashed any more

    bool KeyRun()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current; return k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed);
#else
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
    }

    bool KeyCrawl()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current; return k != null && k.cKey.isPressed;
#else
        return Input.GetKey(KeyCode.C);
#endif
    }

    void OnGUI()
    {
        if (!touchButtons || GameState.IsOver || GameState.Cutscene || PetZoneDetector.SetupOpen || LevelSelect.Open || PetPause.Paused || Guide.Open) { touchAxis = 0f; return; }
        float s = Mathf.Max(1f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float H = Screen.height / s;
        var st = new GUIStyle(GUI.skin.button) { fontSize = 34 };
        bool l = GUI.RepeatButton(new Rect(16f, H - 190f, 96f, 96f), "<", st);
        bool r = GUI.RepeatButton(new Rect(124f, H - 190f, 96f, 96f), ">", st);
        if (Event.current.type == EventType.Repaint) touchAxis = (r ? 1f : 0f) - (l ? 1f : 0f);
        // RUN / CRAWL toggles above the arrows (same as the pet in the outer zones / low in the picture)
        var tg = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = Loc.Bold };
        if (Abilities.Has(Ability.Run))
        {
            GUI.color = touchRun ? new Color(0.78f, 1f, 0.37f) : Color.white;
            if (GUI.Button(new Rect(16f, H - 246f, 96f, 48f), Loc.T("RUN"), tg)) { touchRun = !touchRun; if (touchRun) touchCrawl = false; }
        }
        if (Abilities.Has(Ability.Crawl))
        {
            GUI.color = touchCrawl ? new Color(0.91f, 0.64f, 0.23f) : Color.white;
            if (GUI.Button(new Rect(124f, H - 246f, 96f, 48f), Loc.T("CRAWL"), tg)) { touchCrawl = !touchCrawl; if (touchCrawl) touchRun = false; }
        }
        GUI.color = Color.white;
    }

    float KeyboardAxis()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return 0f;
        float v = 0f;
        if (k.rightArrowKey.isPressed || k.dKey.isPressed) v += 1f;
        if (k.leftArrowKey.isPressed || k.aKey.isPressed) v -= 1f;
        return v;
#else
        return Input.GetAxisRaw("Horizontal");
#endif
    }

    void Face(float d)
    {
        var s = transform.localScale;
        s.x = baseScaleX * (d < 0f ? -1f : 1f);
        transform.localScale = s;
    }
}
