using UnityEngine;
using Unity.InferenceEngine;

// Opens a camera (laptop webcam or phone), finds the best cat/dog with YOLO26n,
// and turns where the pet is IN THE PICTURE into commands - the player moves the camera, the pet doesn't
// have to do tricks:
//   left / right of the picture -> BACK / FORWARD (the outer part of each side = RUN)
//   LOW in the picture (tilt the camera up, below the CRAWL line) -> Crouching: the dog crawls, silently
//   BIG in the picture (camera close to the pet)                 -> Close: the dog sniffs
//   (optional, off by default: the pet lying down - box much wider than tall - also = crawl)
//
// CAMERA SETUP screen (SETUP button on the PET CAM window, or opens by itself on first launch):
//   - big live preview with drag-able zone lines, detection-area crop, zone labels
//   - placement checklist: pet seen / light / distance / fully in frame / AI speed + one tip line
//   - CAMERA tab: device, 480p/720p, mirror, rotated image, flip box, which side is "forward"
//   - ZONES tab: stop-zone center + width, auto-calibrate (walk the pet across), detection area
//   - FEEL tab: sensitivity, response delay, smoothness, keep-tracking time, fps line
//   - Save & Play stores everything in PlayerPrefs; Cancel restores; game is paused while open
public class PetZoneDetector : MonoBehaviour
{
    public enum Move { Stop, Forward, Back, RunForward, RunBack }

    [Header("Model")]
    public ModelAsset modelAsset;
    [Tooltip("Score needed to START tracking an animal")]
    [Range(0f, 1f)] public float enterScore = 0.35f;
    [Tooltip("Keep tracking until the score drops below this")]
    [Range(0f, 1f)] public float exitScore = 0.15f;
    [Tooltip("Seconds to keep the last position when the animal is briefly lost")]
    public float lostGrace = 0.6f;
    [Tooltip("When the pet was last seen at the EDGE of the picture (half out of frame), keep its last place this long")]
    public float edgeGrace = 2f;
    [Tooltip("Higher = follows faster, lower = smoother")]
    public float smoothing = 8f;

    [Header("Camera")]
    public int cameraIndex = 0;
    public bool hiRes = false;         // 1280x720 instead of 640x480 (sharper, slower)
    public bool mirror = false;        // flip left/right (front cameras)
    public bool swapAxes = false;      // use vertical axis if the phone image is rotated
    public bool flipBoxY = false;      // flip box drawing if it appears upside down

    [Header("Zones (0 = left, 1 = right of the preview)")]
    [Range(0f, 1f)] public float backEdge = 0.3f;
    [Range(0f, 1f)] public float forwardEdge = 0.7f;
    public bool forwardIsRight = true;
    public float holdTime = 0.3f;      // seconds a zone must hold before the command changes
    [Tooltip("Outer part of each side zone that means RUN (0 = no run zones)")]
    [Range(0f, 0.9f)] public float runShare = 0.45f;

    [Header("Crawl / sniff (from where the pet is in the picture)")]
    [Tooltip("Crawl when the pet is LOW in the picture: its middle below this line (0 = top, 1 = bottom)")]
    [Range(0.5f, 0.95f)] public float crawlLine = 0.68f;
    [Tooltip("Also crawl when the pet lies down (box much wider than tall). Off: only the camera decides")]
    public bool crawlWhenLying = false;
    [Tooltip("Box width / height at or above this = lying down -> crawl (only with crawlWhenLying)")]
    public float lieShape = 1.9f;
    [Tooltip("Box covering this much of the picture = close to the camera -> sniff")]
    [Range(0.1f, 0.9f)] public float closeArea = 0.35f;
    [Tooltip("...AND this many times bigger than the pet usually looks (so a pet that is simply near the camera doesn't sniff all the time)")]
    public float closeRatio = 1.8f;
    [Tooltip("Seconds the camera must stay close before the dog sniffs")]
    public float sniffHold = 0.6f;
    [Tooltip("Seconds a pose must hold before it counts")]
    public float poseHold = 0.4f;
    [Tooltip("Pause the game when the pet leaves the picture")]
    public bool pauseWhenLost = true;

    [Header("Detection area (pets outside are ignored)")]
    [Range(0f, 1f)] public float roiLeft = 0f;
    [Range(0f, 1f)] public float roiRight = 1f;
    [Range(0f, 1f)] public float roiTop = 0f;
    [Range(0f, 1f)] public float roiBottom = 1f;

    [Header("Setup screen")]
    [Tooltip("Load the settings the player saved on the setup screen (overrides the values above)")]
    public bool loadSaved = true;
    [Tooltip("Open the setup screen automatically when nothing has been saved yet")]
    public bool setupOnFirstRun = true;

    public Move CurrentMove { get; private set; } = Move.Stop;
    public bool HasAnimal { get; private set; }
    public string AnimalName { get; private set; } = "-";
    public float Score { get; private set; }
    public float PosX { get; private set; } = 0.5f;   // smoothed 0..1 in preview space (after mirror/swap)
    public float PosY { get; private set; } = 0.5f;   // smoothed 0 (top) .. 1 (bottom) of the preview
    public bool Crouching { get; private set; }       // low in the picture (or lying down), held poseHold
    public bool Close { get; private set; }           // close to the camera (held poseHold)
    public float Shape { get; private set; }          // smoothed box width / height (in pixels)
    public float Area { get; private set; }           // smoothed box area, 0..1 of the picture
    public bool CameraRunning => cam != null && cam.isPlaying && cam.width >= 32;
    public Texture CameraTexture => cam;
    public float LeftRunEdge => backEdge * (1f - runShare);
    public float RightRunEdge => forwardEdge + (1f - forwardEdge) * runShare;
    public static bool IsRun(Move m) => m == Move.RunForward || m == Move.RunBack;
    public static float Dir(Move m) => m == Move.Forward || m == Move.RunForward ? 1f : m == Move.Back || m == Move.RunBack ? -1f : 0f;
    public static bool SetupOpen { get; private set; }
    public static PetZoneDetector Instance { get; private set; }

    const int Size = 320;
    const int CatClass = 15, DogClass = 16;
    const string PrefKey = "petcam.settings.v1";

    WebCamTexture cam;
    Worker worker;
    Tensor<float> input;
    Tensor<float> pendingOutput;
    bool busy;

    Rect box;                // raw model box, 0..1
    bool outsideArea;        // a pet was seen, but outside the detection area
    float lastSeenTime = -999f;
    // the pet's full size in the picture, remembered while it is completely inside: when it is half out of the
    // frame (camera turned to walk / tilted to crawl) its box only covers the visible part, so the real middle
    // is worked out from the edge that is still visible + this size
    float fullW = -1f, fullH = -1f;
    float normalArea = -1f;   // how big the pet usually looks (slow average while not sniffing)
    public float NormalArea => normalArea;
    bool atEdge;
    Move pending = Move.Stop;
    float pendingSince;
    string savedCamName;
    bool crouchPending, closePending; float crouchSince, closeSince;

    // stats
    int camFrames, aiFrames; float statTimer; float camFps, aiFps, gameFps;

    // ---------------------------------------------------------------- saved settings
    [System.Serializable]
    class Settings
    {
        public string cam;
        public bool hiRes, mirror, swapAxes, flipBoxY, forwardIsRight, showDebug;
        public float backEdge, forwardEdge, holdTime, enterScore, exitScore, smoothing, lostGrace;
        public float roiLeft, roiRight, roiTop, roiBottom;
        public float runShare = 0.45f, lieShape = 1.9f, closeArea = 0.35f, poseHold = 0.4f, crawlLine = 0.68f;
        public bool pauseWhenLost = true, crawlWhenLying = false;
    }

    Settings Capture()
    {
        var d = WebCamTexture.devices;
        return new Settings
        {
            cam = (cameraIndex >= 0 && cameraIndex < d.Length) ? d[cameraIndex].name : savedCamName,
            hiRes = hiRes, mirror = mirror, swapAxes = swapAxes, flipBoxY = flipBoxY,
            forwardIsRight = forwardIsRight, showDebug = showDebug,
            backEdge = backEdge, forwardEdge = forwardEdge, holdTime = holdTime,
            enterScore = enterScore, exitScore = exitScore, smoothing = smoothing, lostGrace = lostGrace,
            roiLeft = roiLeft, roiRight = roiRight, roiTop = roiTop, roiBottom = roiBottom,
            runShare = runShare, lieShape = lieShape, closeArea = closeArea, poseHold = poseHold, pauseWhenLost = pauseWhenLost,
            crawlLine = crawlLine, crawlWhenLying = crawlWhenLying,
        };
    }

    void Apply(Settings s)
    {
        savedCamName = s.cam;
        hiRes = s.hiRes; mirror = s.mirror; swapAxes = s.swapAxes; flipBoxY = s.flipBoxY;
        forwardIsRight = s.forwardIsRight; showDebug = s.showDebug;
        backEdge = s.backEdge; forwardEdge = s.forwardEdge; holdTime = s.holdTime;
        enterScore = s.enterScore; exitScore = s.exitScore; smoothing = s.smoothing; lostGrace = s.lostGrace;
        roiLeft = s.roiLeft; roiRight = s.roiRight; roiTop = s.roiTop; roiBottom = s.roiBottom;
        runShare = s.runShare; lieShape = s.lieShape > 0f ? s.lieShape : 1.9f; closeArea = s.closeArea > 0f ? s.closeArea : 0.35f;
        poseHold = s.poseHold; pauseWhenLost = s.pauseWhenLost;
        crawlLine = s.crawlLine > 0.4f ? s.crawlLine : 0.68f; crawlWhenLying = s.crawlWhenLying;
    }

    Settings defaults;      // inspector values, used by "Reset"
    Settings beforeSetup;   // used by "Cancel"

    void Awake()
    {
        Instance = this;
        defaults = Capture();
        defaults.cam = null;
        if (loadSaved && PlayerPrefs.HasKey(PrefKey))
        {
            try { Apply(JsonUtility.FromJson<Settings>(PlayerPrefs.GetString(PrefKey))); }
            catch { Debug.LogWarning("PetZoneDetector: saved settings unreadable, using defaults"); }
        }
    }

    public void SaveSettings()
    {
        PlayerPrefs.SetString(PrefKey, JsonUtility.ToJson(Capture()));
        PlayerPrefs.Save();
    }

    // ---------------------------------------------------------------- camera
    void Start()
    {
        var backend = SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU;
        worker = new Worker(ModelLoader.Load(modelAsset), backend);
        input = new Tensor<float>(new TensorShape(1, 3, Size, Size));
        if (setupOnFirstRun && !PlayerPrefs.HasKey(PrefKey)) OpenSetup();
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
        {
            var cb = new UnityEngine.Android.PermissionCallbacks();
            cb.PermissionGranted += _ => StartDefaultCamera();
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera, cb);
            return;
        }
#endif
        StartDefaultCamera();
    }

    void StartDefaultCamera()
    {
        var devs = WebCamTexture.devices;
        if (!string.IsNullOrEmpty(savedCamName))      // the camera the player picked last time
            for (int i = 0; i < devs.Length; i++)
                if (devs[i].name == savedCamName) { StartCamera(i); return; }
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
        for (int i = 0; i < devs.Length; i++) if (!devs[i].isFrontFacing) { cameraIndex = i; break; }   // phones: back camera
#endif
        StartCamera(cameraIndex);
    }

    public void StartCamera(int index)
    {
        if (cam != null) { cam.Stop(); Destroy(cam); cam = null; }
        var devices = WebCamTexture.devices;
        if (devices.Length == 0) { Debug.LogWarning("No camera found"); return; }
        cameraIndex = (index % devices.Length + devices.Length) % devices.Length;
        savedCamName = devices[cameraIndex].name;
        cam = hiRes ? new WebCamTexture(devices[cameraIndex].name, 1280, 720, 30)
                    : new WebCamTexture(devices[cameraIndex].name, 640, 480, 30);
        cam.Play();
        Debug.Log("Camera: " + devices[cameraIndex].name + (hiRes ? " 720p" : " 480p"));
    }

    void Update()
    {
        UpdateStats();
        if (cam == null || !cam.isPlaying || cam.width < 32) return;
        if (cam.didUpdateThisFrame) camFrames++;

        // 1) Collect a finished result (non-blocking)
        if (busy && pendingOutput != null && pendingOutput.IsReadbackRequestDone())
        {
            try
            {
                using var output = pendingOutput.ReadbackAndClone();
                Process(output);
                aiFrames++;
            }
            catch (System.InvalidOperationException) { /* readback dropped (camera switched / paused) - skip this frame */ }
            busy = false;
        }

        // 2) Start a new inference on the newest camera frame
        if (!busy && cam.didUpdateThisFrame)
        {
            TextureConverter.ToTensor(cam, input, new TextureTransform());
            worker.Schedule(input);
            pendingOutput = worker.PeekOutput() as Tensor<float>;
            pendingOutput.ReadbackRequest();
            busy = true;
        }

        UpdateMove();
        if (SetupOpen) UpdateSetup();
    }

    // Model box (0..1) -> preview space: x along the zones, y up/down on the preview
    void ToView(float cx, float cy, out float vx, out float vy)
    {
        vx = swapAxes ? cy : cx;
        vy = swapAxes ? cx : cy;
        if (mirror) vx = 1f - vx;
        if (flipBoxY) vy = 1f - vy;
    }

    void Process(Tensor<float> output)
    {
        int n = output.shape[2];
        float best = 0f, bestAny = 0f; int bestI = -1; int bestC = -1;
        for (int i = 0; i < n; i++)
        {
            float c = output[0, 4 + CatClass, i];
            float d = output[0, 4 + DogClass, i];
            float sc = Mathf.Max(c, d);
            if (sc <= best) continue;
            if (sc > bestAny) bestAny = sc;
            ToView(output[0, 0, i] / Size, output[0, 1, i] / Size, out float vx, out float vy);
            if (vx < roiLeft || vx > roiRight || vy < roiTop || vy > roiBottom) continue;   // outside detection area
            best = sc; bestI = i; bestC = c >= d ? CatClass : DogClass;
        }
        Score = best;
        outsideArea = bestAny >= enterScore && best < enterScore;

        // Hysteresis: harder to start tracking, easier to keep tracking
        float threshold = HasAnimal ? exitScore : enterScore;
        if (bestI < 0 || best < threshold) return;   // keep last state; grace timer decides

        AnimalName = bestC == CatClass ? "cat" : "dog";
        float cx = output[0, 0, bestI] / Size, cy = output[0, 1, bestI] / Size;
        float w = output[0, 2, bestI] / Size, h = output[0, 3, bestI] / Size;
        box = new Rect(cx - w / 2f, cy - h / 2f, w, h);

        ToView(cx - w / 2f, cy - h / 2f, out float ax, out float ay);
        ToView(cx + w / 2f, cy + h / 2f, out float bx, out float by);
        float x0 = Mathf.Min(ax, bx), x1 = Mathf.Max(ax, bx), y0 = Mathf.Min(ay, by), y1 = Mathf.Max(ay, by);
        const float E = 0.025f;
        bool cutL = x0 < E, cutR = x1 > 1f - E, cutT = y0 < E, cutB = y1 > 1f - E;
        float vw = x1 - x0, vh = y1 - y0;
        if (!cutL && !cutR) fullW = fullW < 0f ? vw : Mathf.Lerp(fullW, vw, 0.15f);
        if (!cutT && !cutB) fullH = fullH < 0f ? vh : Mathf.Lerp(fullH, vh, 0.15f);
        float x = (x0 + x1) / 2f, yv = (y0 + y1) / 2f;
        if (fullW > vw)
        {
            if (cutR && !cutL) x = x0 + fullW / 2f;        // half out on the right: its middle is further right
            else if (cutL && !cutR) x = x1 - fullW / 2f;
        }
        if (fullH > vh)
        {
            if (cutB && !cutT) yv = y0 + fullH / 2f;       // half out at the bottom (camera tilted up = crawl)
            else if (cutT && !cutB) yv = y1 - fullH / 2f;
        }
        atEdge = cutL || cutR || cutB;
        // unscaled time: keeps working while the game is paused on the setup screen
        float dt = Mathf.Max(Time.unscaledDeltaTime, 1f / 60f);
        // box shape in real pixels (the model sees the frame squashed to a square)
        float shape = h > 1e-4f ? (w * CamAspect()) / h : 1f;
        if (swapAxes) shape = 1f / Mathf.Max(1e-4f, shape);   // rotated picture: width and height swap
        float area = w * h;
        float k = 1f - Mathf.Exp(-smoothing * dt * 2f);
        if (!HasAnimal) { PosX = x; PosY = yv; Shape = shape; Area = area; }   // jump on first detection
        else
        {
            PosX = Mathf.Lerp(PosX, x, 1f - Mathf.Exp(-smoothing * dt * 3f));
            PosY = Mathf.Lerp(PosY, yv, 1f - Mathf.Exp(-smoothing * dt * 3f));
            Shape = Mathf.Lerp(Shape, shape, k); Area = Mathf.Lerp(Area, area, k);
        }
        if (!closePending) normalArea = normalArea < 0f ? Area : Mathf.Lerp(normalArea, Area, 1f - Mathf.Exp(-dt / 4f));
        HasAnimal = true;
        lastSeenTime = Time.unscaledTime;
    }

    void UpdateMove()
    {
        float now = Time.unscaledTime;
        // lost right after being at the edge = it just slipped out of the frame: keep going the same way a bit longer
        float grace = atEdge ? Mathf.Max(lostGrace, edgeGrace) : lostGrace;
        if (HasAnimal && now - lastSeenTime > grace) { HasAnimal = false; AnimalName = "-"; fullW = fullH = -1f; atEdge = false; normalArea = -1f; }

        Move candidate = Move.Stop;
        if (HasAnimal)
        {
            bool runOn = runShare > 0.01f && Abilities.Has(Ability.Run);   // run zones only once the dog has learned to run
            bool runR = runOn && PosX > RightRunEdge, runL = runOn && PosX < LeftRunEdge;
            if (PosX > forwardEdge) candidate = forwardIsRight ? (runR ? Move.RunForward : Move.Forward) : (runR ? Move.RunBack : Move.Back);
            else if (PosX < backEdge) candidate = forwardIsRight ? (runL ? Move.RunBack : Move.Back) : (runL ? Move.RunForward : Move.Forward);
        }
        if (candidate != pending) { pending = candidate; pendingSince = now; }
        if (pending != CurrentMove && now - pendingSince >= holdTime) CurrentMove = pending;

        // crawl / sniff: must hold for poseHold seconds. Close (big) wins over low.
        bool close = HasAnimal && Area >= closeArea && (normalArea < 0f || Area >= normalArea * closeRatio);
        bool lie = HasAnimal && !close && (PosY >= crawlLine || (crawlWhenLying && Shape >= lieShape));
        if (lie != crouchPending) { crouchPending = lie; crouchSince = now; }
        if (close != closePending) { closePending = close; closeSince = now; }
        Crouching = crouchPending && now - crouchSince >= (lie ? poseHold : 0.15f);
        Close = closePending && now - closeSince >= Mathf.Max(poseHold, sniffHold);
    }

    void UpdateStats()
    {
        statTimer += Time.unscaledDeltaTime;
        gameFps = Mathf.Lerp(gameFps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.1f);
        if (statTimer >= 1f)
        {
            camFps = camFrames / statTimer; aiFps = aiFrames / statTimer;
            camFrames = aiFrames = 0; statTimer = 0f;
        }
    }

    // ---------------------------------------------------------------- setup screen logic
    int tab;                         // 0 camera, 1 zones, 2 feel, 3 mic
    VoiceTrigger.Settings micBefore; float volBefore;
    int micCal; float micCalEnd, micFloor, micPeak, micBestClarity; string micMsg = "";
    float prevTimeScale = 1f;
    float roomLight = -1f, lightTimer;   // average brightness 0..1 (-1 = unknown)
    Color32[] pix;
    bool calibrating; float calEnd, calMin, calMax; int calCount; string calMsg = "";
    int dragLine;                    // 0 none, 1 back edge, 2 forward edge

    public void OpenSetup() => OpenSetup(tab);

    public void OpenSetup(int startTab)
    {
        if (SetupOpen) return;
        SetupOpen = true;
        tab = Mathf.Clamp(startTab, 0, 4);
        beforeSetup = Capture();
        var vt = VoiceTrigger.Instance;
        micBefore = vt != null ? vt.Capture() : null; volBefore = Sfx.Volume; micCal = 0; micMsg = "";
        prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        calibrating = false; calMsg = "";
    }

    public void CloseSetup(bool save)
    {
        if (!SetupOpen) return;
        var vt = VoiceTrigger.Instance;
        if (save) { SaveSettings(); if (vt != null) vt.Save(); PlayerPrefs.Save(); }
        else
        {
            if (beforeSetup != null) ApplyAndRestart(beforeSetup);
            if (vt != null && micBefore != null) vt.Apply(micBefore);
            Sfx.Volume = volBefore;
        }
        micCal = 0;
        SetupOpen = false;
        calibrating = false;
        Time.timeScale = GameState.IsOver ? 0f : (prevTimeScale > 0f ? prevTimeScale : 1f);
    }

    void ApplyAndRestart(Settings s)
    {
        string oldCam = savedCamName; bool oldRes = hiRes;
        Apply(s);
        if (string.IsNullOrEmpty(s.cam)) savedCamName = oldCam;
        if (savedCamName != oldCam || hiRes != oldRes) StartDefaultCamera();
    }

    void UpdateSetup()
    {
        // brightness check twice a second
        lightTimer -= Time.unscaledDeltaTime;
        if (lightTimer <= 0f)
        {
            lightTimer = 0.5f;
            try
            {
                int len = cam.width * cam.height;
                if (pix == null || pix.Length != len) pix = new Color32[len];
                cam.GetPixels32(pix);
                long sum = 0; int cnt = 0;
                for (int i = 0; i < len; i += 97) { var p = pix[i]; sum += (p.r * 3 + p.g * 6 + p.b) / 10; cnt++; }
                roomLight = cnt > 0 ? sum / (255f * cnt) : -1f;
            }
            catch { roomLight = -1f; }
        }

        if (calibrating)
        {
            if (HasAnimal && Time.unscaledTime - lastSeenTime < 0.2f)
            {
                calMin = Mathf.Min(calMin, PosX); calMax = Mathf.Max(calMax, PosX); calCount++;
            }
            if (Time.unscaledTime >= calEnd)
            {
                calibrating = false;
                float range = calMax - calMin;
                if (calCount < 15) calMsg = Loc.T("Didn't see the pet enough. Try again in better light.");
                else if (range < 0.2f) calMsg = Loc.T("Pet didn't move far enough. Lead it from one side to the other.");
                else
                {
                    backEdge = calMin + range / 3f;
                    forwardEdge = calMin + range * 2f / 3f;
                    calMsg = $"Done! Pet used {range * 100f:F0}% of the picture. Zones set to fit.";
                }
            }
        }
    }

    void StartCalibration()
    {
        calibrating = true;
        calEnd = Time.unscaledTime + 8f;
        calMin = 1f; calMax = 0f; calCount = 0; calMsg = "";
    }

    // ---------------------------------------------------------------- GUI
    [Header("Layout")]
    [Tooltip("On = small camera window in the top-right corner (game). Off = full-screen debug view.")]
    public bool pipMode = true;
    public float pipWidth = 340f;
    [Tooltip("Show score / fps lines")]
    public bool showDebug = false;

    static readonly Color Ink = new Color(0.95f, 0.92f, 0.87f);
    static readonly Color Muted = new Color(0.66f, 0.69f, 0.78f);
    static readonly Color Panel = new Color(0.055f, 0.078f, 0.141f, 0.92f);
    static readonly Color Line = new Color(0.17f, 0.23f, 0.40f);
    static readonly Color Teal = new Color(0.5f, 0.82f, 0.76f);
    static readonly Color Amber = new Color(0.91f, 0.64f, 0.23f);
    static readonly Color Good = new Color(0.47f, 0.85f, 0.5f);
    static readonly Color Bad = new Color(0.95f, 0.45f, 0.38f);
    static readonly Color Lime = new Color(0.78f, 1f, 0.37f);

    static void Fill(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white; }

    static void Frame(Rect r, float t, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    static GUIStyle Txt(int size, Color c, TextAnchor a = TextAnchor.MiddleLeft, bool bold = false)
    {
        var st = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = a, fontStyle = bold ? Loc.Bold : FontStyle.Normal, wordWrap = true };
        st.normal.textColor = c;
        return st;
    }

    GUIStyle Btn(int size) => new GUIStyle(GUI.skin.button) { fontSize = size };

    Color MoveColor(Move m) => m == Move.Forward ? Teal : m == Move.Back ? Amber : m == Move.RunForward ? Lime : m == Move.RunBack ? Bad : Ink;

    // what the PET CAM window shows: the move + crawl / sniff, e.g. "RUN >>", "FORWARD · CRAWL", "SNIFF"
    string Command()
    {
        string m = CurrentMove == Move.RunForward ? Loc.T("RUN >>") : CurrentMove == Move.RunBack ? Loc.T("<< RUN") : Loc.T(CurrentMove.ToString().ToUpper());
        if (Crouching && Abilities.Has(Ability.Crawl)) return m + " · " + Loc.T("CRAWL");   // (Close never overlaps Crouching)
        if (Close && Abilities.Has(Ability.Sniff)) return Loc.T("SNIFF");
        return m;
    }

    void OnGUI()
    {
        float s = Mathf.Max(1f, Screen.height / 720f);   // scale UI up on phones
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = Screen.height / s;

        if (SetupOpen) { GUI.depth = -100; DrawSetup(W, H); return; }
        GUI.depth = 0;

        float aspect = CamAspect();
        Rect r;
        if (pipMode)
        {
            float pw = pipWidth, ph = pw / aspect;
            float extra = showDebug ? 20f : 0f;
            var panel = new Rect(W - pw - 32f, 16f, pw + 16f, ph + 66f + extra);
            Fill(panel, Panel);
            Frame(panel, 1f, Line);
            GUI.Label(new Rect(panel.x + 10f, panel.y + 4f, 80f, 24f), Loc.T("PET CAM"), Txt(14, Muted));
            if (GUI.Button(new Rect(panel.x + 84f, panel.y + 5f, 70f, 22f), Loc.T("SETUP"), Btn(12))) OpenSetup();
            GUI.Label(new Rect(panel.x + 10f, panel.y + 4f, panel.width - 20f, 24f), Command(), Txt(18, MoveColor(CurrentMove), TextAnchor.MiddleRight, true));
            r = new Rect(panel.x + 8f, panel.y + 32f, pw, ph);
            float y = r.yMax + 6f;
            GUI.Label(new Rect(r.x + 2f, y, pw, 24f), HasAnimal ? $"{Loc.T(AnimalName)}  {Score:F2}" : (outsideArea ? Loc.T("pet outside area") : Loc.T("no animal")), Txt(13, Muted));
            if (showDebug) GUI.Label(new Rect(r.x + 2f, y + 22f, pw, 20f), $"cam {camFps:F0} · AI {aiFps:F0} · game {gameFps:F0} fps", Txt(13, Muted));
            DrawFeed(r, false);
        }
        else
        {
            float sh = H - 140, dw = W, dh = W / aspect;
            if (dh > sh) { dh = sh; dw = sh * aspect; }
            r = new Rect((W - dw) / 2f, 120, dw, dh);
            if (GUI.Button(new Rect(10, 10, 160, 48), Loc.T("SETUP"), Btn(18))) OpenSetup();
            GUI.Label(new Rect(W - 620, 10, 600, 50), Command(), Txt(40, MoveColor(CurrentMove), TextAnchor.MiddleRight, true));
            GUI.Label(new Rect(W - 620, 60, 600, 24), $"{(HasAnimal ? AnimalName : Loc.T("no animal"))}   score {Score:F2}   x {PosX:F2}   cam {camFps:F0} / AI {aiFps:F0} / game {gameFps:F0} fps", Txt(13, Muted, TextAnchor.MiddleRight));
            DrawFeed(r, true);
        }
    }

    float CamAspect() => (cam != null && cam.width >= 32) ? (float)cam.width / cam.height : 4f / 3f;

    // camera picture + zones + detection area + pet box. big = setup / debug view
    void DrawFeed(Rect r, bool big)
    {
        if (cam == null || cam.width < 32)
        {
            Fill(r, new Color(0.13f, 0.16f, 0.23f));
            GUI.Label(r, cam == null && WebCamTexture.devices.Length == 0 ? Loc.T("no camera found") : Loc.T("starting camera..."), Txt(big ? 22 : 13, Muted, TextAnchor.MiddleCenter));
            return;
        }

        GUI.DrawTextureWithTexCoords(r, cam, mirror ? new Rect(1, 0, -1, 1) : new Rect(0, 0, 1, 1));

        // detection area: darken what is ignored
        bool hasRoi = roiLeft > 0.001f || roiRight < 0.999f || roiTop > 0.001f || roiBottom < 0.999f;
        if (hasRoi)
        {
            var dim = new Color(0f, 0f, 0f, 0.6f);
            Fill(new Rect(r.x, r.y, r.width * roiLeft, r.height), dim);
            Fill(new Rect(r.x + r.width * roiRight, r.y, r.width * (1f - roiRight), r.height), dim);
            Fill(new Rect(r.x + r.width * roiLeft, r.y, r.width * (roiRight - roiLeft), r.height * roiTop), dim);
            Fill(new Rect(r.x + r.width * roiLeft, r.y + r.height * roiBottom, r.width * (roiRight - roiLeft), r.height * (1f - roiBottom)), dim);
            Frame(new Rect(r.x + r.width * roiLeft, r.y + r.height * roiTop, r.width * (roiRight - roiLeft), r.height * (roiBottom - roiTop)), 1f, new Color(1f, 1f, 1f, 0.5f));
        }

        // zone tints (left zone / right zone) + dashed lines
        Color leftCol = forwardIsRight ? Amber : Teal, rightCol = forwardIsRight ? Teal : Amber;
        float a = big ? 0.16f : 0.10f;
        Fill(new Rect(r.x, r.y, r.width * backEdge, r.height), new Color(leftCol.r, leftCol.g, leftCol.b, a));
        Fill(new Rect(r.x + r.width * forwardEdge, r.y, r.width * (1f - forwardEdge), r.height), new Color(rightCol.r, rightCol.g, rightCol.b, a));
        GUI.color = new Color(Ink.r, Ink.g, Ink.b, 0.75f);
        float lw = big ? 3f : 2f;
        for (float yy = r.y; yy < r.yMax; yy += 10f)
        {
            float hh = Mathf.Min(5f, r.yMax - yy);
            GUI.DrawTexture(new Rect(r.x + r.width * backEdge - lw / 2f, yy, lw, hh), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x + r.width * forwardEdge - lw / 2f, yy, lw, hh), Texture2D.whiteTexture);
        }
        GUI.color = Color.white;
        if (runShare > 0.01f && (big || Abilities.Has(Ability.Run)))   // run zones: the outer part of each side, stronger tint + thin solid line
        {
            Fill(new Rect(r.x, r.y, r.width * LeftRunEdge, r.height), new Color(leftCol.r, leftCol.g, leftCol.b, a * 1.6f));
            Fill(new Rect(r.x + r.width * RightRunEdge, r.y, r.width * (1f - RightRunEdge), r.height), new Color(rightCol.r, rightCol.g, rightCol.b, a * 1.6f));
            Fill(new Rect(r.x + r.width * LeftRunEdge - 1f, r.y, 2f, r.height), new Color(Ink.r, Ink.g, Ink.b, 0.45f));
            Fill(new Rect(r.x + r.width * RightRunEdge - 1f, r.y, 2f, r.height), new Color(Ink.r, Ink.g, Ink.b, 0.45f));
        }

        // crawl band: the bottom of the picture (tilt the camera up so the pet sits in it)
        if (big || Abilities.Has(Ability.Crawl))
        {
            float cy = r.y + r.height * crawlLine;
            Fill(new Rect(r.x, cy, r.width, r.yMax - cy), new Color(0.62f, 0.45f, 0.9f, Crouching ? 0.34f : big ? 0.18f : 0.12f));
            GUI.color = new Color(0.8f, 0.7f, 1f, 0.85f);
            for (float xx = r.x; xx < r.xMax; xx += 10f) GUI.DrawTexture(new Rect(xx, cy - lw / 2f, Mathf.Min(5f, r.xMax - xx), lw), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (big || r.yMax - cy > 16f)
                GUI.Label(new Rect(r.x + 6f, cy + 1f, 140f, big ? 26f : 16f), Loc.T("CRAWL"), Txt(big ? 16 : 11, new Color(0.85f, 0.75f, 1f), TextAnchor.UpperLeft, true));
        }

        if (big)
        {
            // zone names along the top
            string leftName = forwardIsRight ? Loc.T("BACK") : Loc.T("FORWARD"), rightName = forwardIsRight ? Loc.T("FORWARD") : Loc.T("BACK");
            Move lm = forwardIsRight ? Move.Back : Move.Forward, rm = forwardIsRight ? Move.Forward : Move.Back;
            Move lr = forwardIsRight ? Move.RunBack : Move.RunForward, rrm = forwardIsRight ? Move.RunForward : Move.RunBack;
            bool runs = runShare > 0.01f;
            float l0 = runs ? LeftRunEdge : 0f, r1 = runs ? RightRunEdge : 1f;
            if (runs) ZoneLabel(new Rect(r.x, r.y + 38f, r.width * l0, 28f), Loc.T("RUN"), leftCol, CurrentMove == lr);
            ZoneLabel(new Rect(r.x + r.width * l0, r.y + 6f, r.width * (backEdge - l0), 28f), leftName, leftCol, CurrentMove == lm);
            ZoneLabel(new Rect(r.x + r.width * backEdge, r.y + 6f, r.width * (forwardEdge - backEdge), 28f), Loc.T("STOP"), Ink, CurrentMove == Move.Stop);
            ZoneLabel(new Rect(r.x + r.width * forwardEdge, r.y + 6f, r.width * (r1 - forwardEdge), 28f), rightName, rightCol, CurrentMove == rm);
            if (runs) ZoneLabel(new Rect(r.x + r.width * r1, r.y + 38f, r.width * (1f - r1), 28f), Loc.T("RUN"), rightCol, CurrentMove == rrm);
        }

        // pet box
        bool fresh = Time.unscaledTime - lastSeenTime < 0.15f;
        if (HasAnimal && !swapAxes)
        {
            float bx = mirror ? 1f - box.xMax : box.x;
            float by = flipBoxY ? 1f - box.yMax : box.y;
            var br = new Rect(r.x + bx * r.width, r.y + by * r.height, box.width * r.width, box.height * r.height);
            Frame(br, big ? 3f : 2f, fresh ? Lime : new Color(Lime.r, Lime.g, Lime.b, 0.45f));
            if (big) GUI.Label(new Rect(br.x + 4f, br.yMax - 26f, 200f, 24f), $"{Loc.T(AnimalName)} {Score * 100f:F0}%", Txt(15, Lime));
        }
        if (HasAnimal)
        {
            float d = big ? 18f : 10f;
            Fill(new Rect(r.x + Mathf.Clamp01(PosX) * r.width - d / 2f, r.y + Mathf.Clamp01(PosY) * r.height - d / 2f, d, d), Amber);   // where the pet's middle is
        }
        Frame(r, 1f, Line);
    }

    void ZoneLabel(Rect r, string text, Color c, bool active)
    {
        if (r.width < 30f) return;
        float iw = Mathf.Min(120f, r.width - 4f);
        var inner = new Rect(r.center.x - iw / 2f, r.y, iw, r.height);
        Fill(inner, new Color(0f, 0f, 0f, active ? 0.75f : 0.45f));
        if (active) Frame(inner, 2f, c);
        GUI.Label(inner, text, Txt(16, active ? c : new Color(c.r, c.g, c.b, 0.7f), TextAnchor.MiddleCenter, true));
    }

    // ---------------------------------------------------------------- setup screen drawing
    void DrawSetup(float W, float H)
    {
        Fill(new Rect(0, 0, W, H), new Color(0.035f, 0.05f, 0.095f, 0.98f));

        float panelW = Mathf.Min(470f, W * 0.42f);
        float px = W - panelW - 16f;

        // title + one-line placement guide
        GUI.Label(new Rect(24f, 10f, px - 40f, 34f), Loc.T("CAMERA & MIC SETUP"), Txt(26, Ink, TextAnchor.MiddleLeft, true));
        GUI.Label(new Rect(24f, 42f, px - 40f, 26f), Loc.T("Phone sideways, 1.5 - 3 m from your pet, lens at pet height. Drag the white lines to resize zones."), Txt(15, Muted));

        // preview
        float aspect = CamAspect();
        float aw = px - 48f, ah = H - 80f - 118f;
        float dw = aw, dh = aw / aspect;
        if (dh > ah) { dh = ah; dw = ah * aspect; }
        var r = new Rect(24f + (aw - dw) / 2f, 76f, dw, dh);
        DrawFeed(r, true);
        DragZoneLines(r);
        if (calibrating) DrawCalibration(r);

        // big command readout, bottom-right of preview
        var cmdR = new Rect(r.xMax - 190f, r.yMax - 52f, 180f, 44f);
        Fill(cmdR, new Color(0f, 0f, 0f, 0.6f));
        GUI.Label(cmdR, Command(), Txt(28, MoveColor(CurrentMove), TextAnchor.MiddleCenter, true));

        DrawChecklist(new Rect(24f, r.yMax + 12f, px - 48f, 100f));

        // right panel
        var panel = new Rect(px, 12f, panelW, H - 24f);
        Fill(panel, Panel);
        Frame(panel, 1f, Line);
        string[] tabs = { "CAMERA", "ZONES", "FEEL", "MIC", "POSES" };
        float tw = (panelW - 24f) / tabs.Length;
        for (int i = 0; i < tabs.Length; i++)
        {
            var tr = new Rect(px + 12f + i * tw, panel.y + 12f, tw - 6f, 46f);
            if (i == tab) { Fill(tr, new Color(0.17f, 0.23f, 0.40f)); Frame(tr, 2f, Teal); GUI.Label(tr, Loc.T(tabs[i]), Txt(15, Teal, TextAnchor.MiddleCenter, true)); }
            else if (GUI.Button(tr, Loc.T(tabs[i]), Btn(15))) tab = i;
        }

        float x = px + 18f, w = panelW - 36f, y = panel.y + 74f;
        if (tab == 0) CameraTab(x, w, ref y);
        else if (tab == 1) ZonesTab(x, w, ref y);
        else if (tab == 2) FeelTab(x, w, ref y);
        else if (tab == 3) MicTab(x, w, ref y);
        else PosesTab(x, w, ref y);

        // footer
        float fy = panel.yMax - 66f, bw = (w - 16f) / 3f;
        if (GUI.Button(new Rect(x, fy, bw, 54f), Loc.T("Reset"), Btn(17)))
        {
            if (tab == 3) { VoiceTrigger.Instance?.ResetToDefaults(); Sfx.Volume = 0.8f; micMsg = Loc.T("Mic back to default settings."); }
            else { ApplyAndRestart(defaults); calMsg = Loc.T("Back to default settings."); }
        }
        if (GUI.Button(new Rect(x + bw + 8f, fy, bw, 54f), Loc.T("Cancel"), Btn(17))) CloseSetup(false);
        var save = Btn(18); save.fontStyle = Loc.Bold; save.normal.textColor = Teal; save.hover.textColor = Teal;
        if (GUI.Button(new Rect(x + 2f * (bw + 8f), fy, bw, 54f), Loc.T("Save & Play"), save)) CloseSetup(true);
    }

    void CameraTab(float x, float w, ref float y)
    {
        var devices = WebCamTexture.devices;
        Label(x, y, w, Loc.T("Camera")); y += 26f;
        if (GUI.Button(new Rect(x, y, 56f, 48f), "<", Btn(22))) StartCamera(cameraIndex - 1);
        string nm = devices.Length == 0 ? Loc.T("none") : $"{cameraIndex + 1}/{devices.Length}  {devices[Mathf.Clamp(cameraIndex, 0, devices.Length - 1)].name}";
        if (devices.Length > 0 && devices[Mathf.Clamp(cameraIndex, 0, devices.Length - 1)].isFrontFacing) nm += "  (front)";
        Fill(new Rect(x + 62f, y, w - 124f, 48f), new Color(0f, 0f, 0f, 0.35f));
        GUI.Label(new Rect(x + 70f, y, w - 140f, 48f), nm, Txt(14, Ink, TextAnchor.MiddleCenter));
        if (GUI.Button(new Rect(x + w - 56f, y, 56f, 48f), ">", Btn(22))) StartCamera(cameraIndex + 1);
        y += 62f;

        Label(x, y, w, Loc.T("Picture")); y += 26f;
        int q = Seg(new Rect(x, y, w, 46f), hiRes ? 1 : 0, Loc.T("Fast 480p"), Loc.T("Sharp 720p"));
        if (q != (hiRes ? 1 : 0)) { hiRes = q == 1; StartCamera(cameraIndex); }
        y += 60f;

        Label(x, y, w, Loc.T("Forward is on the ... side of the picture")); y += 26f;
        forwardIsRight = Seg(new Rect(x, y, w, 46f), forwardIsRight ? 1 : 0, Loc.T("LEFT"), Loc.T("RIGHT")) == 1;
        y += 60f;

        mirror = Toggle(new Rect(x, y, w, 46f), mirror, Loc.T("Mirror left / right")); y += 54f;
        swapAxes = Toggle(new Rect(x, y, w, 46f), swapAxes, Loc.T("Picture is rotated (use up/down)")); y += 54f;
        flipBoxY = Toggle(new Rect(x, y, w, 46f), flipBoxY, Loc.T("Pet box upside down? Flip it")); y += 54f;
    }

    void ZonesTab(float x, float w, ref float y)
    {
        float center = (backEdge + forwardEdge) / 2f, width = forwardEdge - backEdge;
        float nc = SliderRow(x, w, ref y, Loc.T("STOP zone position"), center < 0.47f ? Loc.T("left") : center > 0.53f ? Loc.T("right") : Loc.T("middle"), center, 0.15f, 0.85f);
        float nw = SliderRow(x, w, ref y, Loc.T("STOP zone width"), $"{width * 100f:F0}%", width, 0.06f, 0.8f);
        if (!Mathf.Approximately(nc, center) || !Mathf.Approximately(nw, width))
        {
            backEdge = Mathf.Clamp(nc - nw / 2f, 0.02f, 0.94f);
            forwardEdge = Mathf.Clamp(nc + nw / 2f, backEdge + 0.04f, 0.98f);
        }

        runShare = SliderRow(x, w, ref y, Loc.T("RUN zones  (outer part of each side, 0 = off)"), Pct(runShare), runShare, 0f, 0.8f);

        string calLabel = calibrating ? $"Recording... {Mathf.CeilToInt(calEnd - Time.unscaledTime)}  (tap to stop)" : Loc.T("AUTO: walk your pet across (8 s)");
        if (GUI.Button(new Rect(x, y, w, 50f), calLabel, Btn(16))) { if (calibrating) calEnd = Time.unscaledTime; else StartCalibration(); }
        y += 54f;
        if (calMsg.Length > 0) { GUI.Label(new Rect(x, y, w, 40f), calMsg, Txt(13, calMsg.StartsWith(Loc.T("Done")) ? Good : Amber)); }
        y += 40f;

        Label(x, y, w, Loc.T("Detection area  (pets outside are ignored)")); y += 24f;
        float half = (w - 12f) / 2f, y0 = y;
        roiLeft = SliderRow(x, half, ref y, Loc.T("Left"), $"{roiLeft * 100f:F0}%", roiLeft, 0f, 0.45f);
        roiTop = SliderRow(x, half, ref y, Loc.T("Top"), $"{roiTop * 100f:F0}%", roiTop, 0f, 0.45f);
        float y1 = y; y = y0;
        float rr = SliderRow(x + half + 12f, half, ref y, Loc.T("Right"), $"{(1f - roiRight) * 100f:F0}%", 1f - roiRight, 0f, 0.45f);
        float rb = SliderRow(x + half + 12f, half, ref y, Loc.T("Bottom"), $"{(1f - roiBottom) * 100f:F0}%", 1f - roiBottom, 0f, 0.45f);
        roiRight = 1f - rr; roiBottom = 1f - rb;
        y = Mathf.Max(y, y1);
    }

    // ---------------------------------------------------------------- MIC tab
    void MicTab(float x, float w, ref float y)
    {
        var vt = VoiceTrigger.Instance;
        if (vt == null) { GUI.Label(new Rect(x, y, w, 60f), Loc.T("No voice input in this scene."), Txt(16, Muted)); return; }
        UpdateMicCalibration(vt);

        Label(x, y, w, Loc.T("Microphone")); y += 26f;
        var devs = Microphone.devices;
        if (GUI.Button(new Rect(x, y, 56f, 46f), "<", Btn(22))) vt.StepDevice(-1);
        string nm = devs.Length == 0 ? Loc.T("no microphone found") : (vt.MicOn ? vt.DeviceName : Loc.T("off"));
        Fill(new Rect(x + 62f, y, w - 124f, 46f), new Color(0f, 0f, 0f, 0.35f));
        GUI.Label(new Rect(x + 70f, y, w - 140f, 46f), nm, Txt(14, Ink, TextAnchor.MiddleCenter));
        if (GUI.Button(new Rect(x + w - 56f, y, 56f, 46f), ">", Btn(22))) vt.StepDevice(1);
        y += 56f;

        // live meter: loudness bar with the threshold line, clearness bar, hold progress, WOOF flash
        float max = Mathf.Max(0.05f, vt.loudness * 3f);
        var bar = new Rect(x, y, w - 110f, 28f);
        Fill(bar, new Color(1f, 1f, 1f, 0.08f));
        Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(vt.Level / max), bar.height), vt.Voiced ? Teal : Muted);
        Fill(new Rect(bar.x + bar.width * Mathf.Clamp01(vt.loudness / max) - 1.5f, bar.y - 4f, 3f, bar.height + 8f), Amber);
        var clar = new Rect(x, y + 34f, w - 110f, 10f);
        Fill(clar, new Color(1f, 1f, 1f, 0.08f));
        Fill(new Rect(clar.x, clar.y, clar.width * vt.Clarity, clar.height), Lime);
        Fill(new Rect(clar.x + clar.width * vt.voicedness - 1.5f, clar.y - 3f, 3f, clar.height + 6f), Amber);
        Fill(new Rect(x, y + 48f, (w - 110f) * Mathf.Clamp01(vt.Held / Mathf.Max(0.05f, vt.holdTime)), 5f), Teal);
        bool woof = Time.unscaledTime - vt.LastDetected < 0.8f;
        var flag = new Rect(x + w - 100f, y, 100f, 54f);
        Fill(flag, woof ? Teal : new Color(0f, 0f, 0f, 0.35f));
        GUI.Label(flag, woof ? Loc.T("WOOF!") : Loc.T("say\n\"hooong\""), Txt(woof ? 22 : 13, woof ? new Color(0.05f, 0.08f, 0.14f) : Muted, TextAnchor.MiddleCenter, true));
        GUI.Label(new Rect(x, y + 54f, w, 22f), vt.Voiced ? $"voice {vt.Pitch:F0} Hz" : Loc.T("loud bar = volume, green = how voice-like, orange = needed"), Txt(13, Muted));
        y += 82f;

        vt.loudness = SliderRow(x, w, ref y, Loc.T("Loudness needed  (raise in a noisy room)"), $"{vt.loudness * 1000f:F0}", vt.loudness, 0.004f, 0.12f);
        vt.voicedness = SliderRow(x, w, ref y, Loc.T("Voice clearness needed"), Pct(vt.voicedness), vt.voicedness, 0.25f, 0.9f);
        vt.holdTime = SliderRow(x, w, ref y, Loc.T("Hold the sound for"), $"{vt.holdTime:F2} s", vt.holdTime, 0.15f, 0.8f);

        string cl = micCal == 1 ? $"Stay quiet... {Mathf.CeilToInt(micCalEnd - Time.unscaledTime)}"
                  : micCal == 2 ? $"Now say \"hooong\" 3 times... {Mathf.CeilToInt(micCalEnd - Time.unscaledTime)}"
                  : Loc.T("AUTO: quiet 3 s, then say \"hooong\" 3x");
        if (GUI.Button(new Rect(x, y, w, 46f), cl, Btn(15)) && micCal == 0)
        { micCal = 1; micCalEnd = Time.unscaledTime + 3f; micFloor = 0f; micPeak = 0f; micBestClarity = 0f; micMsg = ""; }
        y += 50f;
        if (micMsg.Length > 0) GUI.Label(new Rect(x, y, w, 36f), micMsg, Txt(13, micMsg.StartsWith(Loc.T("Done")) ? Good : Amber));
        y += 36f;

        bool use = Toggle(new Rect(x, y, w * 0.5f - 6f, 46f), vt.useMicrophone, Loc.T("Use mic"));
        if (use != vt.useMicrophone) { vt.useMicrophone = use; vt.StartMic(vt.DeviceName); }
        float vol = Slider(new Rect(x + w * 0.5f + 6f, y + 18f, w * 0.5f - 6f, 30f), Sfx.Volume, 0f, 1f);
        GUI.Label(new Rect(x + w * 0.5f + 6f, y - 4f, w * 0.5f - 6f, 22f), Loc.F("Game sound {0}%", (vol * 100f).ToString("F0")), Txt(13, Muted));
        if (!Mathf.Approximately(vol, Sfx.Volume)) Sfx.Volume = vol;
    }

    // step 1: measure the room's noise; step 2: measure the player's "hooong"; set thresholds between them
    void UpdateMicCalibration(VoiceTrigger vt)
    {
        if (micCal == 0) return;
        if (micCal == 1)
        {
            micFloor = Mathf.Max(micFloor, vt.Level);
            if (Time.unscaledTime >= micCalEnd) { micCal = 2; micCalEnd = Time.unscaledTime + 6f; }
            return;
        }
        micPeak = Mathf.Max(micPeak, vt.Level);
        if (vt.Level > micFloor * 2f) micBestClarity = Mathf.Max(micBestClarity, vt.Clarity);
        if (Time.unscaledTime < micCalEnd) return;
        micCal = 0;
        if (micPeak < micFloor * 2.5f || micPeak < 0.006f) { micMsg = Loc.T("Didn't hear a clear voice. Move closer to the phone and try again."); return; }
        vt.loudness = Mathf.Clamp(Mathf.Sqrt(micFloor * 2.5f * micPeak * 0.5f), 0.004f, 0.12f);   // between noise and voice
        if (micBestClarity > 0.3f) vt.voicedness = Mathf.Clamp(micBestClarity * 0.8f, 0.25f, 0.9f);
        micMsg = $"Done! Room noise {micFloor * 1000f:F0}, your voice {micPeak * 1000f:F0}. Try a \"hooong\" - WOOF! should light up.";
    }

    void FeelTab(float x, float w, ref float y)
    {
        // sensitivity 0..1 -> enter score 0.6 (strict) .. 0.15 (finds pets easily)
        float sens = Mathf.InverseLerp(0.6f, 0.15f, enterScore);
        float ns = SliderRow(x, w, ref y, Loc.T("Sensitivity  (higher = finds pets easier, more mistakes)"), $"{Pct(sens)}", sens, 0f, 1f);
        if (!Mathf.Approximately(ns, sens)) { enterScore = Mathf.Lerp(0.6f, 0.15f, ns); exitScore = enterScore * 0.45f; }

        holdTime = SliderRow(x, w, ref y, Loc.T("Response delay  (lower = reacts faster)"), $"{holdTime:F2} s", holdTime, 0f, 1f);
        smoothing = SliderRow(x, w, ref y, Loc.T("Follow speed  (lower = smoother, less jitter)"), $"{smoothing:F0}", smoothing, 1f, 20f);
        lostGrace = SliderRow(x, w, ref y, Loc.T("Keep tracking when pet is hidden"), $"{lostGrace:F1} s", lostGrace, 0.1f, 2.5f);
        showDebug = Toggle(new Rect(x, y, w, 46f), showDebug, Loc.T("Show fps on the PET CAM window")); y += 56f;
        GUI.Label(new Rect(x, y, w, 60f), $"camera {camFps:F0} fps  ·  AI {aiFps:F0} fps  ·  game {gameFps:F0} fps\n{(cam != null ? cam.width + Loc.T("x") + cam.height + Loc.T("  rotation ") + cam.videoRotationAngle + "°" : "")}", Txt(13, Muted));
    }

    static string Pct(float v) => $"{v * 100f:F0}%";

    // ---------------------------------------------------------------- POSES tab: camera low = crawl, camera close = sniff
    void PosesTab(float x, float w, ref float y)
    {
        string now = !HasAnimal ? Loc.T("no pet seen") : Crouching ? Loc.T("CRAWL (pet low in the picture)") : Close ? Loc.T("SNIFF (camera close)") : Loc.T("walk");
        var big = new Rect(x, y, w, 44f);
        Fill(big, new Color(0f, 0f, 0f, 0.35f));
        GUI.Label(big, now, Txt(20, Crouching ? new Color(0.8f, 0.7f, 1f) : Close ? Lime : Ink, TextAnchor.MiddleCenter, true));
        y += 54f;

        Label(x, y, w, $"Height in the picture  {Pct(PosY)}  (CRAWL below the line)"); y += 24f;
        PoseBar(new Rect(x, y, w, 16f), PosY, crawlLine, new Color(0.62f, 0.45f, 0.9f)); y += 26f;
        crawlLine = SliderRow(x, w, ref y, Loc.T("CRAWL line (higher = tilt more)"), Pct(crawlLine), crawlLine, 0.5f, 0.95f);

        Label(x, y, w, $"Size in the picture  {Pct(Area)}   (usual {(normalArea < 0f ? "-" : Pct(normalArea))}, sniff at x{closeRatio:F1})"); y += 24f;
        PoseBar(new Rect(x, y, w, 16f), Mathf.Clamp01(Area / 0.9f), closeArea / 0.9f, Lime); y += 26f;
        closeArea = SliderRow(x, w, ref y, Loc.T("SNIFF when the pet fills"), Pct(closeArea), closeArea, 0.1f, 0.8f);

        poseHold = SliderRow(x, w, ref y, Loc.T("Hold for"), $"{poseHold:F1} s", poseHold, 0.1f, 1.5f);
        crawlWhenLying = Toggle(new Rect(x, y, w, 40f), crawlWhenLying, Loc.T("Also crawl when the pet lies down")); y += 46f;
        pauseWhenLost = Toggle(new Rect(x, y, w, 40f), pauseWhenLost, Loc.T("Pause when the pet leaves the picture")); y += 46f;
        GUI.Label(new Rect(x, y, w, 40f), Loc.T("Your pet doesn't need to do anything: tilt the camera UP so the pet sits low in the picture = crawl. Bring the camera CLOSE = sniff."), Txt(13, Muted));
    }

    void PoseBar(Rect r, float v, float mark, Color c)
    {
        Fill(r, new Color(1f, 1f, 1f, 0.08f));
        Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(v), r.height), HasAnimal ? c : Muted);
        Fill(new Rect(r.x + r.width * Mathf.Clamp01(mark) - 1.5f, r.y - 4f, 3f, r.height + 8f), Ink);
    }

    void DrawChecklist(Rect r)
    {
        bool camOk = cam != null && cam.width >= 32;
        bool? seen = camOk ? HasAnimal : (bool?)null;
        bool? lightOk = roomLight < 0f ? (bool?)null : roomLight > 0.2f && roomLight < 0.92f;
        float area = box.width * box.height;
        bool? distOk = HasAnimal ? area > 0.015f && area < 0.45f : (bool?)null;
        bool? inFrame = HasAnimal ? box.xMin > 0.01f && box.xMax < 0.99f && box.yMin > 0.01f && box.yMax < 0.99f : (bool?)null;
        bool? speedOk = camOk && aiFps > 0f ? aiFps >= 8f : (bool?)null;

        float x = r.x;
        Chip(ref x, r.y, seen, "Pet seen");
        Chip(ref x, r.y, lightOk, "Light");
        Chip(ref x, r.y, distOk, "Distance");
        Chip(ref x, r.y, inFrame, "Whole pet in picture");
        Chip(ref x, r.y, speedOk, Loc.F("AI speed {0}", aiFps.ToString("F0")));

        string tip; Color tc = Amber;
        if (!camOk) tip = "Starting camera... if nothing shows, pick another camera in CAMERA.";
        else if (lightOk == false && roomLight <= 0.2f) tip = "Too dark - turn on a light or face a window. The AI needs to see fur and ears.";
        else if (lightOk == false) tip = "Too bright - don't point the camera at a window or lamp.";
        else if (outsideArea) tip = "Your pet is outside the detection area - make it bigger in ZONES.";
        else if (seen == false) tip = calibrating ? "Bring your pet into the picture..." : "No pet seen - bring your pet in front of the camera (whole body, side view is best).";
        else if (distOk == false && area >= 0.45f) tip = "Too close - move the phone back so your pet looks smaller.";
        else if (distOk == false) tip = "Too far - move closer, your pet looks tiny.";
        else if (inFrame == false) tip = "Part of your pet is outside the picture - move the phone back or aim it again.";
        else if (speedOk == false) tip = "AI is slow on this phone - choose Fast 480p in CAMERA.";
        else { tip = "Looks good! Lead your pet into each zone and watch the command change."; tc = Good; }
        GUI.Label(new Rect(r.x, r.y + 46f, r.width, 50f), Loc.T(tip), Txt(18, tc));
    }

    void Chip(ref float x, float y, bool? ok, string text)
    {
        var st = Txt(15, Ink, TextAnchor.MiddleLeft);
        st.wordWrap = false;
        text = Loc.T(text);
        float tw = st.CalcSize(new GUIContent(text)).x;
        var r = new Rect(x, y, tw + 44f, 36f);
        Fill(r, new Color(0f, 0f, 0f, 0.35f));
        Color c = ok == null ? Muted : ok.Value ? Good : Bad;
        Frame(r, 1f, new Color(c.r, c.g, c.b, 0.6f));
        Fill(new Rect(r.x + 12f, r.center.y - 6f, 12f, 12f), c);
        GUI.Label(new Rect(r.x + 32f, r.y, tw + 10f, r.height), text, st);
        x += r.width + 8f;
    }

    void DrawCalibration(Rect r)
    {
        Fill(new Rect(r.x, r.center.y - 40f, r.width, 80f), new Color(0f, 0f, 0f, 0.6f));
        GUI.Label(new Rect(r.x, r.center.y - 40f, r.width, 50f), Loc.F("Lead your pet slowly from one side to the other   {0}", Mathf.CeilToInt(calEnd - Time.unscaledTime)), Txt(22, Ink, TextAnchor.MiddleCenter, true));
        if (calCount > 0 && calMax > calMin)
            Fill(new Rect(r.x + calMin * r.width, r.center.y + 16f, (calMax - calMin) * r.width, 10f), Lime);
    }

    // drag the two white zone lines directly on the preview
    void DragZoneLines(Rect r)
    {
        var e = Event.current;
        float bx = r.x + backEdge * r.width, fx = r.x + forwardEdge * r.width;
        // handles
        Fill(new Rect(bx - 12f, r.yMax - 34f, 24f, 28f), dragLine == 1 ? Teal : Ink);
        Fill(new Rect(fx - 12f, r.yMax - 34f, 24f, 28f), dragLine == 2 ? Teal : Ink);

        if (calibrating) return;
        int id = GUIUtility.GetControlID(FocusType.Passive);
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (!r.Contains(e.mousePosition)) break;
                float db = Mathf.Abs(e.mousePosition.x - bx), df = Mathf.Abs(e.mousePosition.x - fx);
                if (Mathf.Min(db, df) > 36f) break;
                dragLine = db <= df ? 1 : 2;
                GUIUtility.hotControl = id; e.Use();
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl != id) break;
                float v = Mathf.Clamp01((e.mousePosition.x - r.x) / r.width);
                if (dragLine == 1) backEdge = Mathf.Clamp(v, 0.02f, forwardEdge - 0.04f);
                else forwardEdge = Mathf.Clamp(v, backEdge + 0.04f, 0.98f);
                e.Use();
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; dragLine = 0; e.Use(); }
                break;
        }
    }

    // ---------------------------------------------------------------- small touch-friendly widgets
    void Label(float x, float y, float w, string text) => GUI.Label(new Rect(x, y, w, 24f), text, Txt(15, Muted));

    float SliderRow(float x, float w, ref float y, string label, string value, float v, float min, float max)
    {
        GUI.Label(new Rect(x, y, w - 80f, 24f), label, Txt(14, Muted));
        GUI.Label(new Rect(x + w - 80f, y, 80f, 24f), value, Txt(15, Ink, TextAnchor.MiddleRight, true));
        v = Slider(new Rect(x, y + 24f, w, 36f), v, min, max);
        y += 66f;
        return v;
    }

    static float Slider(Rect r, float v, float min, float max)
    {
        int id = GUIUtility.GetControlID(FocusType.Passive);
        var e = Event.current;
        var track = new Rect(r.x + 14f, r.center.y - 3f, r.width - 28f, 6f);
        float t = Mathf.InverseLerp(min, max, v);
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (!r.Contains(e.mousePosition)) break;
                GUIUtility.hotControl = id;
                t = Mathf.Clamp01((e.mousePosition.x - track.x) / track.width);
                e.Use();
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl != id) break;
                t = Mathf.Clamp01((e.mousePosition.x - track.x) / track.width);
                e.Use();
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                break;
            case EventType.Repaint:
                Fill(track, new Color(1f, 1f, 1f, 0.15f));
                Fill(new Rect(track.x, track.y, track.width * t, track.height), Teal);
                var knob = new Rect(track.x + track.width * t - 13f, r.center.y - 13f, 26f, 26f);
                Fill(knob, GUIUtility.hotControl == id ? Teal : Ink);
                break;
        }
        return Mathf.Lerp(min, max, t);
    }

    bool Toggle(Rect r, bool on, string label)
    {
        Fill(r, new Color(0f, 0f, 0f, 0.3f));
        GUI.Label(new Rect(r.x + 12f, r.y, r.width - 100f, r.height), label, Txt(15, Ink));
        var sw = new Rect(r.xMax - 80f, r.y + 8f, 68f, r.height - 16f);
        Fill(sw, on ? Teal : new Color(1f, 1f, 1f, 0.15f));
        GUI.Label(sw, on ? Loc.T("ON") : Loc.T("OFF"), Txt(14, on ? new Color(0.05f, 0.08f, 0.14f) : Muted, TextAnchor.MiddleCenter, true));
        if (GUI.Button(r, GUIContent.none, GUIStyle.none)) on = !on;
        return on;
    }

    int Seg(Rect r, int idx, string a, string b)
    {
        float hw = (r.width - 6f) / 2f;
        for (int i = 0; i < 2; i++)
        {
            var br = new Rect(r.x + i * (hw + 6f), r.y, hw, r.height);
            string t = i == 0 ? a : b;
            if (i == idx) { Fill(br, new Color(0.17f, 0.23f, 0.40f)); Frame(br, 2f, Teal); GUI.Label(br, t, Txt(16, Teal, TextAnchor.MiddleCenter, true)); }
            else if (GUI.Button(br, t, Btn(16))) idx = i;
        }
        return idx;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (SetupOpen) { SetupOpen = false; Time.timeScale = 1f; }
        if (cam != null) cam.Stop();
        worker?.Dispose();
        input?.Dispose();
    }
}
