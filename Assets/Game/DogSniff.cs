using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// The dog sniffs: the camera is CLOSE to your pet - it fills the picture (or F key / SNIFF button).
// Silent, unlike a bark: at night things within 'radius' show up for a moment, and nothing hears it.
// A buried ScentSpot within 'digRange' is dug up (it can open a gate, lower a bridge...).
// Added to the dog automatically (DogWalker).
public class DogSniff : MonoBehaviour
{
    public float radius = 5f;
    public float digRange = 1.8f;
    public float cooldown = 1.5f;

    public static DogSniff Instance { get; private set; }

    PetZoneDetector detector;
    float ready;
    bool wasClose;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        detector = FindFirstObjectByType<PetZoneDetector>();
    }

    void Update()
    {
        // ONE sniff each time the camera comes close (move it back, then close again to sniff again)
        bool close = detector != null && detector.Close;
        bool want = close && !wasClose;
        wasClose = close;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) want = true;
#else
        if (Input.GetKeyDown(KeyCode.F)) want = true;
#endif
        if (want) Sniff();
    }

    public void Sniff()
    {
        if (!GameState.Started || GameState.IsOver || GameState.Cutscene || LevelSelect.Open || PetPause.Paused || Guide.Open || PetZoneDetector.SetupOpen) return;
        if (!Abilities.Has(Ability.Sniff) || Time.time < ready) return;   // learned later in the story
        ready = Time.time + cooldown;
        DogPose.On(gameObject).Sniff();   // nose down, twitching, scent puffs
        Sfx.Play("sniff", 0.8f, Random.Range(0.9f, 1.1f));
        var spot = ScentSpot.Nearest(transform.position.x, digRange);
        if (spot != null) { spot.Dig(); return; }
        var dark = FindFirstObjectByType<Darkness>();
        if (dark != null) dark.Sniff(transform.position, radius);
        var near = ScentSpot.Nearest(transform.position.x, radius);
        GameState.Toast(near != null ? Loc.T("Sniff sniff... something is buried close by!") : dark != null ? Loc.T("Sniff sniff... (quietly, nothing heard it)") : Loc.T("Sniff sniff... nothing here."));
        if (near != null) near.Glint();
    }

    void OnGUI()
    {
        var w = GetComponent<DogWalker>();
        if (w == null || !w.touchButtons || !Abilities.Has(Ability.Sniff) || !GameState.Started || GameState.IsOver || GameState.Cutscene || PetZoneDetector.SetupOpen || LevelSelect.Open || PetPause.Paused) return;
        float s = Mathf.Max(1f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = Screen.height / s;
        if (GUI.Button(new Rect(W - 252f, H - 76f, 110f, 56f), Loc.T("SNIFF"))) Sniff();
    }
}
