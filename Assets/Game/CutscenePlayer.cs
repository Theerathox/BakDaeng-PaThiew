using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// In-engine story cutscenes, played with the real game characters and scenery.
//   Opening : arrival in town, the zombies, how to play           -> Level 1
//   Shelter : end of the morning, hiding in the sala till dark     -> Level 6
//   Spirit  : the great shrine, the spirit gives the SHIELD power  -> Level 11
//   Ending  : the taxi, sunrise, credits                           -> title
// Each scene is built by GameSceneBuilder (actors are found by name). Tap / Space / Enter = next line,
// SKIP (or Esc) = skip the whole cutscene. All lines are in the Lines region below - edit freely.
public class CutscenePlayer : MonoBehaviour
{
    public enum Story { Opening, Shelter, Spirit, Ending }
    public Story story;
    [Tooltip("Shown on the credits card of the ending")]
    [TextArea] public string credits = "Made by: (your team names here)";

    Transform cam, dog, master;
    Animator dogAnim;
    CutoutRig masterRig;
    SpriteRenderer overlay;
    Color skyDay, skyNight = new Color(0.05f, 0.075f, 0.14f);
    float night, fade = 1f;
    string speaker = "", line = "", cardTitle = "", cardBody = "";
    int extra;   // 1 = how-to-play drawing, 2 = shield meter drawing
    bool tapped, finished;
    static Sprite disc, ring, square;

    static readonly int IsWalking = Animator.StringToHash("IsWalking");

    // ------------------------------------------------------------------ the stories
    IEnumerator Opening()
    {
        var bus = Find("Songthaew");
        var rope = Find("Rope");
        Show(dog, false); Show(master, false); Show(rope, false);
        var zs = Zombies();
        StartCoroutine(Fade(0f, 1.2f));
        StartCoroutine(Drive(bus, -2f, 3.2f, 0f));
        yield return Say("", "A small town in Isan. A blind traveller and his guide dog, Bak Daeng, arrive for a holiday.", 4f);
        Show(dog, true); Show(master, true); Show(rope, true);
        StartCoroutine(Drive(bus, 40f, 5f, 1f));
        StartCoroutine(Walk(dog, 3.5f, 1.4f));
        yield return Walk(master, 1f, 1.4f);
        StartCoroutine(Pose(masterRig, "Cheer", 1f, 0.4f));
        if (dog != null) DogPose.On(dog.gameObject).Happy = 1f;
        yield return Say("MASTER", "Smell that, Bak Daeng? Sticky rice... grilled chicken... this will be a good trip!", 3.5f);
        StartCoroutine(Pose(masterRig, "Cheer", 0f, 0.4f));
        if (dog != null) DogPose.On(dog.gameObject).Happy = 0f;
        Sfx.Play("groan", 0.8f, 0.9f);
        foreach (var z in zs) StartCoroutine(Walk(z, z.position.x - 3f, 0.35f));
        StartCoroutine(Pan(9f, 1.6f));
        yield return Say("MASTER", "Wait... so many footsteps. Dragging... shuffling... Those are not people.", 4f);
        StartCoroutine(Pan(1.5f, 1.2f));
        Face(dog, 1f);
        Bark(); yield return Wait(0.5f); Bark();
        yield return Say("MASTER", "I can't see them - but you can. Lead the way, Bak Daeng. Bark, and I'll run to you!", 4.5f);
        extra = 1;
        yield return Card("HOW TO PLAY", "Your PET moves Bak Daeng: step close / stand still / step back in front of the camera.\nYOU make him bark: hold a 'hooong~' into the microphone (or press BARK).", 7f);
        extra = 0;
        yield return Card("FIND A SHELTER", "Get your master off the street before it fills up with zombies!", 3f);
        yield return Fade(1f, 0.8f);
    }

    IEnumerator Shelter()
    {
        var radio = Find("Radio_MorLam") ? Find("Radio_MorLam").GetComponent<Radio>() : null;
        var zs = Zombies();
        StartCoroutine(Fade(0f, 1f));
        yield return Say("", "Safe... for now. Outside, the dead fill the streets.", 3.5f);
        if (radio != null) radio.isOn = true;
        foreach (var z in zs) StartCoroutine(Pose(z.GetComponent<CutoutRig>(), "Dance", 1f, 0.5f));   // the song gets them dancing
        yield return Wait(2.6f);
        if (radio != null) radio.isOn = false;
        foreach (var z in zs) StartCoroutine(Pose(z.GetComponent<CutoutRig>(), "Dance", 0f, 0.5f));
        Sfx.Play("click");
        yield return Say("RADIO", "...kzzt... people say the light of the GREAT SPIRIT SHRINE at the edge of town keeps the dead away... kzzt...", 5f);
        yield return Say("MASTER", "The great shrine... We'll go there. After dark.", 3f);
        StartCoroutine(Night(1f, 3.5f));
        StartCoroutine(Sun(-3f, 3.5f));
        yield return Wait(1.5f);
        for (int i = 0; i < zs.Count; i++) { StartCoroutine(Sleep(zs[i])); yield return Wait(0.5f); }
        yield return Say("", "Night falls. Many of them lie down... and sleep.", 3.5f);
        Bark();
        StartCoroutine(Wave(dog.position, 11f));
        foreach (var z in zs) StartCoroutine(Flash(z));
        yield return Say("MASTER", "In the dark, everyone is blind - just like me. Your bark will be our eyes now, Bak Daeng.", 4.5f);
        yield return Say("MASTER", "But they hear it too. So... quietly.", 3f);
        yield return Card("TO THE GREAT SHRINE", "Bark to see in the dark. Don't wake the sleepers.", 3f);
        yield return Fade(1f, 0.8f);
    }

    IEnumerator Spirit()
    {
        var shrine = Find("Shrine_SpiritHouse");
        var glow = shrine != null ? shrine.Find("glow")?.GetComponent<SpriteRenderer>() : null;
        var zs = Zombies();
        StartCoroutine(Fade(0f, 1f));
        StartCoroutine(Pose(masterRig, "Bow", 1f, 0.8f));
        yield return Say("MASTER", "Great spirit... please. Help us.", 3f);
        Bark();
        yield return Wait(0.6f);
        Sfx.Play("win", 0.7f, 0.6f);
        float sx = shrine != null ? shrine.position.x : 0f;
        var pillar = Glow(new Vector3(sx, -3.4f + 5f, 0f), new Vector2(2.2f, 16f), new Color(1f, 0.85f, 0.45f, 0f), 92, disc);   // soft beam
        var orb = Glow(new Vector3(sx, -0.6f, 0f), Vector2.one * 0.9f, new Color(1f, 0.95f, 0.75f, 0f), 94, disc);
        for (float t = 0f; t < 2.5f; t += Time.deltaTime)
        {
            float k = t / 2.5f;
            if (glow != null) { glow.transform.localScale = Vector3.one * Mathf.Lerp(2.2f, 7f, k); var c = glow.color; c.a = Mathf.Lerp(0.1f, 0.45f, k); glow.color = c; }
            pillar.color = new Color(1f, 0.85f, 0.45f, 0.4f * k);
            orb.color = new Color(1f, 0.95f, 0.75f, k);
            orb.transform.position = new Vector3(sx, Mathf.Lerp(-0.6f, 1.6f, k), 0f);
            if (tapped) break;
            yield return null;
        }
        StartCoroutine(Float(orb.transform));
        StartCoroutine(Pose(masterRig, "Bow", 0f, 0.8f));
        yield return Say("SPIRIT", "I hear you, little guide. I cannot drive all the dead from this town...", 4.5f);
        // demo: the shield around the master, zombies stop at its edge
        float mid = (master.position.x + dog.position.x) / 2f;
        var bubble = Glow(new Vector3(mid, -3.4f + 1.6f, 0f), Vector2.one * 6f, new Color(1f, 0.85f, 0.4f, 0.35f), 93, disc);
        var edge = Glow(bubble.transform.position, Vector2.one * 6f, new Color(1f, 0.9f, 0.55f, 0.8f), 93, ring);
        Sfx.Play("win", 0.5f, 1.35f);
        foreach (var z in zs) StartCoroutine(Walk(z, mid + 3.2f + 0.9f * zs.IndexOf(z), 0.8f));   // they stop at the edge of the light
        yield return Say("SPIRIT", "...but I will guard you on your way. Call me LOUDER than a bark, and my light will shield your master.", 5.5f);
        extra = 2;
        yield return Card("NEW POWER: SPIRIT SHIELD", "Normal 'hooong~' = bark.\nLOUD 'HOOONG!' past the 2nd mark on the mic meter = SHIELD for 5 s - zombies can't touch your master.\nThen it must recharge.   (No mic: S key or the SHIELD button)", 8f);
        extra = 0;
        Destroy(bubble.gameObject); Destroy(edge.gameObject);
        yield return Say("MASTER", "There's a main road past the market. If we find a taxi... we're out of here!", 4f);
        yield return Card("RUN FOR THE TAXI", "Use the shield to get through the crowd!", 3f);
        yield return Fade(1f, 0.8f);
    }

    IEnumerator Ending()
    {
        var taxi = Find("Taxi");
        var zs = Zombies();
        SetNight(0.85f);
        var sunT = Find("sun");
        if (sunT != null) { var sp = sunT.localPosition; sp.y = -2.5f; sunT.localPosition = sp; }   // still below the horizon
        StartCoroutine(Fade(0f, 1f));
        StartCoroutine(Pose(masterRig, "Cheer", 1f, 0.3f));
        if (dog != null) DogPose.On(dog.gameObject).Happy = 1f;
        yield return Say("MASTER", "An engine! Bak Daeng - a car! TAXI! TAXI!", 3f);
        StartCoroutine(Pose(masterRig, "Cheer", 0f, 0.3f));
        Bark();
        yield return Say("DRIVER", "Get in, get in! Is that... ZOMBIES?!", 3f);
        foreach (var z in zs) StartCoroutine(Walk(z, z.position.x + 4f, 0.45f));
        float tx = taxi != null ? taxi.position.x : 2f;
        StartCoroutine(Walk(dog, tx, 2.2f));
        yield return Walk(master, tx - 0.5f, 2.2f);
        Show(dog, false); Show(master, false); Show(Find("Rope"), false);
        Sfx.Play("clunk", 1f, 0.8f);
        yield return Wait(0.6f);
        StartCoroutine(Drive(taxi, 60f, 6f, 1f));
        StartCoroutine(Night(0f, 5f));
        StartCoroutine(Sun(2.6f, 5f));
        yield return Say("", "The sun comes up over the rice fields. Bak Daeng got his master out of town.", 4.5f);
        yield return Say("MASTER", "Airport, please. And Bak Daeng... thank you, my friend.", 4f);
        yield return Card("BAK DAENG PA THIEW", "THE END\n\nThanks for playing - and give your pet a treat!\n", 5f);
        yield return Card("CREDITS", CreditsText(), 10f);
        yield return Fade(1f, 1f);
    }

    // ------------------------------------------------------------------ runner
    IEnumerator Start()
    {
        cam = Camera.main.transform;
        skyDay = Camera.main.backgroundColor;
        dog = Find("Dog"); master = Find("Tourist");
        if (dog != null) dogAnim = dog.GetComponent<Animator>();
        if (master != null) masterRig = master.GetComponent<CutoutRig>();
        MakeSprites();
        overlay = new GameObject("NightOverlay").AddComponent<SpriteRenderer>();
        overlay.sprite = square; overlay.sortingOrder = 90;
        overlay.transform.localScale = new Vector3(400f, 200f, 1f);
        SetNight(story == Story.Spirit ? 0f : night);
        IEnumerator run = story == Story.Opening ? Opening() : story == Story.Shelter ? Shelter() : story == Story.Spirit ? Spirit() : Ending();
        yield return StartCoroutine(run);
        while (!finished) { Finish(); yield return null; }
    }

    void Finish()
    {
        if (finished || LevelTransition.Busy) return;   // (still sliding in from the last scene)
        finished = true;
        Time.timeScale = 1f;
        int next = SceneManager.GetActiveScene().buildIndex + 1;
        if (story == Story.Ending || next >= SceneManager.sceneCountInBuildSettings) next = 0;   // back to the title
        LevelTransition.Go(next);
    }

    void Update()
    {
        tapped = false;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame)) tapped = true;
        if (k != null && k.escapeKey.wasPressedThisFrame && !LevelSelect.Open) Finish();
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) tapped = true;
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) tapped = true;
#else
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0)) tapped = true;
        if (Input.GetKeyDown(KeyCode.Escape)) Finish();
#endif
    }

    void LateUpdate()
    {
        if (overlay != null && cam != null) overlay.transform.position = new Vector3(cam.position.x, cam.position.y, 0f);
    }

    // ------------------------------------------------------------------ helpers
    Transform Find(string name) { var go = GameObject.Find(name); return go != null ? go.transform : null; }

    List<Transform> Zombies()
    {
        var list = new List<Transform>();
        for (int i = 0; i < 20; i++) { var z = Find("Zombie" + i); if (z != null) list.Add(z); }
        return list;
    }

    static void Show(Transform t, bool on) { if (t != null) t.gameObject.SetActive(on); }

    IEnumerator Wait(float secs) { for (float t = 0f; t < secs; t += Time.deltaTime) yield return null; }

    // wait, or move on early when the viewer taps (but not in the first 0.4 s, so a tap doesn't skip two lines)
    IEnumerator WaitOrTap(float secs)
    {
        for (float t = 0f; t < secs; t += Time.deltaTime) { if (tapped && t > 0.4f) break; yield return null; }
        yield return null;
    }

    IEnumerator Say(string who, string text, float secs) { speaker = Loc.T(who); line = Loc.T(text); yield return WaitOrTap(secs); line = ""; }

    // the credits card at the end of the game (each line is translated in LocThai)
    static readonly string[] CreditLines = {
        "Made by Team CatCanFly",
        "Game engine: Unity",
        "Pet detection AI: Ultralytics YOLO (AGPL-3.0)",
        "Coding help: Claude (Anthropic)",
        "Some art, music and sounds were made with the help of AI tools",
        "Thai font: Kanit by Cadson Demak (SIL Open Font License)",
        "Source code: AGPL-3.0",
    };
    static string CreditsText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var l in CreditLines) { if (sb.Length > 0) sb.Append('\n'); sb.Append(Loc.T(l)); }
        return sb.ToString();
    }

    IEnumerator Card(string title, string body, float secs)
    {
        cardTitle = Loc.T(title); cardBody = Loc.T(body);
        yield return WaitOrTap(secs);
        cardTitle = ""; cardBody = "";
    }

    void Face(Transform t, float dir)
    {
        if (t == null) return;
        var rig = t.GetComponent<CutoutRig>();
        if (rig != null) { rig.Face(dir); return; }
        var s = t.localScale; s.x = Mathf.Abs(s.x) * (dir < 0f ? -1f : 1f); t.localScale = s;
    }

    IEnumerator Walk(Transform t, float toX, float speed)
    {
        if (t == null) yield break;
        var rig = t.GetComponent<CutoutRig>();
        var anim = t == dog ? dogAnim : null;
        while (t != null && Mathf.Abs(toX - t.position.x) > 0.05f)
        {
            float dir = Mathf.Sign(toX - t.position.x);
            var p = t.position; p.x += dir * Mathf.Min(speed * Time.deltaTime, Mathf.Abs(toX - p.x)); t.position = p;
            if (rig != null) rig.Velocity = dir * speed;
            if (anim != null) anim.SetBool(IsWalking, true);
            Face(t, dir);
            yield return null;
        }
        if (rig != null) rig.Velocity = 0f;
        if (anim != null) anim.SetBool(IsWalking, false);
    }

    // vehicles: ease in / out (accel 1 = start slow and speed up)
    IEnumerator Drive(Transform t, float toX, float secs, float accel)
    {
        if (t == null) yield break;
        float from = t.position.x;
        var wheels = new List<Transform>();
        foreach (Transform c in t) if (c.name.StartsWith("wheel")) wheels.Add(c);
        for (float k = 0f; k < 1f; k += Time.deltaTime / secs)
        {
            float e = accel > 0f ? k * k : 1f - (1f - k) * (1f - k);
            float before = t.position.x;
            var p = t.position; p.x = Mathf.Lerp(from, toX, e); t.position = p;
            foreach (var w in wheels) w.Rotate(0f, 0f, -(p.x - before) * 120f);
            yield return null;
        }
    }

    // blend a CutoutRig pose (Cheer / Bow / Dance / Sneak / Fall) to a value
    IEnumerator Pose(CutoutRig rig, string pose, float to, float secs)
    {
        if (rig == null) yield break;
        float Get() => pose == "Cheer" ? rig.Cheer : pose == "Bow" ? rig.Bow : pose == "Dance" ? rig.Dance : pose == "Sneak" ? rig.Sneak : rig.Fall;
        void Set(float v) { if (pose == "Cheer") rig.Cheer = v; else if (pose == "Bow") rig.Bow = v; else if (pose == "Dance") rig.Dance = v; else if (pose == "Sneak") rig.Sneak = v; else rig.Fall = v; }
        float from = Get();
        for (float k = 0f; k < 1f; k += Time.deltaTime / Mathf.Max(0.01f, secs)) { Set(Mathf.Lerp(from, to, k)); yield return null; }
        Set(to);
    }

    IEnumerator Pan(float toX, float secs)
    {
        float from = cam.position.x;
        for (float k = 0f; k < 1f; k += Time.deltaTime / secs)
        {
            var p = cam.position; p.x = Mathf.Lerp(from, toX, k * k * (3f - 2f * k)); cam.position = p;
            yield return null;
        }
    }

    void SetNight(float k)
    {
        night = k;
        var c = skyNight; c.a = 0.72f * k; overlay.color = c;
        Camera.main.backgroundColor = Color.Lerp(skyDay, skyNight, k);
    }

    IEnumerator Night(float to, float secs)
    {
        float from = night;
        for (float k = 0f; k < 1f; k += Time.deltaTime / secs) { SetNight(Mathf.Lerp(from, to, k)); yield return null; }
        SetNight(to);
    }

    IEnumerator Sun(float toY, float secs)
    {
        var sun = Find("sun");
        if (sun == null) yield break;
        float from = sun.localPosition.y;
        for (float k = 0f; k < 1f; k += Time.deltaTime / secs)
        {
            var p = sun.localPosition; p.y = Mathf.Lerp(from, toY, k); sun.localPosition = p;
            yield return null;
        }
    }

    IEnumerator Fade(float to, float secs)
    {
        float from = fade;
        for (float k = 0f; k < 1f; k += Time.deltaTime / secs) { fade = Mathf.Lerp(from, to, k); yield return null; }
        fade = to;
    }

    // zombie lies down and snores (same pose as a sleeping ZombieChaser)
    IEnumerator Sleep(Transform z)
    {
        float y0 = z.position.y, side = Mathf.Sign(z.localScale.x);
        for (float k = 0f; k < 1f; k += Time.deltaTime / 0.8f)
        {
            z.rotation = Quaternion.Euler(0f, 0f, 82f * k * side);
            var p = z.position; p.y = y0 - 1.55f * k; z.position = p;
            yield return null;
        }
        var t = new GameObject("zzz").AddComponent<TextMesh>();
        t.text = "z z Z"; t.characterSize = 0.08f; t.fontSize = 40; t.anchor = TextAnchor.MiddleCenter;
        t.color = new Color(0.62f, 0.9f, 1f);
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.font = font; var mr = t.GetComponent<MeshRenderer>(); mr.sharedMaterial = font.material; mr.sortingOrder = 95;
        t.transform.position = new Vector3(z.position.x, y0 - 1.0f, 0f);
        StartCoroutine(Bob(t.transform));
    }

    IEnumerator Bob(Transform t)
    {
        float y0 = t.position.y;
        while (t != null) { var p = t.position; p.y = y0 + 0.15f * Mathf.Sin(Time.time * 2f); t.position = p; yield return null; }
    }

    IEnumerator Float(Transform t)
    {
        float y0 = t.position.y;
        while (t != null) { var p = t.position; p.y = y0 + 0.2f * Mathf.Sin(Time.time * 1.5f); t.position = p; yield return null; }
    }

    void Bark()
    {
        Sfx.Play("bark", 0.7f, Random.Range(0.95f, 1.08f));
        if (dog != null) DogPose.On(dog.gameObject).Bark();
        if (dog != null) StartCoroutine(Wave(dog.position + new Vector3(Mathf.Sign(dog.localScale.x) * 1.4f, 0.9f, 0f), 2.6f));
    }

    // expanding ring (bark ring, or the night sound wave)
    IEnumerator Wave(Vector3 at, float radius)
    {
        var sr = Glow(at, Vector2.one * 0.2f, new Color(0.91f, 0.64f, 0.23f, 1f), 96, ring);
        if (radius > 5f) { sr.color = new Color(0.62f, 0.9f, 1f, 1f); Sfx.Play("ping", 0.5f); }
        float secs = radius > 5f ? 1.2f : 0.6f;
        for (float k = 0f; k < 1f; k += Time.deltaTime / secs)
        {
            sr.transform.localScale = new Vector3(radius * 2f * k, radius * 2f * k * (radius > 5f ? 0.55f : 1f), 1f);
            var c = sr.color; c.a = 1f - k; sr.color = c;
            yield return null;
        }
        Destroy(sr.gameObject);
    }

    // a zombie lights up for a moment (seen by the sound wave)
    IEnumerator Flash(Transform z)
    {
        yield return Wait(Mathf.Abs(z.position.x - dog.position.x) / 9f);
        var sr = Glow(z.position + Vector3.up * 0.3f, Vector2.one * 2.4f, new Color(0.62f, 0.9f, 1f, 0.5f), 93, disc);
        for (float k = 0f; k < 1f; k += Time.deltaTime / 2f) { var c = sr.color; c.a = 0.5f * (1f - k); sr.color = c; yield return null; }
        Destroy(sr.gameObject);
    }

    SpriteRenderer Glow(Vector3 at, Vector2 size, Color c, int order, Sprite sprite)
    {
        var sr = new GameObject("fx").AddComponent<SpriteRenderer>();
        sr.sprite = sprite; sr.color = c; sr.sortingOrder = order;
        sr.transform.position = at; sr.transform.localScale = new Vector3(size.x, size.y, 1f);
        return sr;
    }

    static void MakeSprites()
    {
        if (disc != null) return;
        const int N = 128;
        var a = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var b = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float r = new Vector2(x + 0.5f - N / 2f, y + 0.5f - N / 2f).magnitude / (N / 2f);
                a.SetPixel(x, y, new Color(1f, 1f, 1f, r > 1f ? 0f : Mathf.Clamp01((1f - r) * 3f)));     // soft disc
                b.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - Mathf.Abs(r - 0.94f) * 25f)));  // thin ring
            }
        a.Apply(); b.Apply();
        disc = Sprite.Create(a, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        ring = Sprite.Create(b, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        var w = new Texture2D(1, 1); w.SetPixel(0, 0, Color.white); w.Apply();
        square = Sprite.Create(w, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
    }

    // ------------------------------------------------------------------ screen: letterbox, subtitles, cards
    void OnGUI()
    {
        if (LevelSelect.Open) return;
        float s = Mathf.Max(1f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = Screen.height / s;
        var ink = new Color(0.95f, 0.92f, 0.87f);

        GUI.color = Color.black;   // letterbox
        GUI.DrawTexture(new Rect(0, 0, W, H * 0.09f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0, H * 0.91f, W, H * 0.09f), Texture2D.whiteTexture);
        GUI.color = Color.white;

        if (line.Length > 0)
        {
            var r = new Rect(W * 0.12f, H * 0.105f, W * 0.76f, H * 0.15f);   // top: the characters walk along the bottom
            GUI.color = new Color(0f, 0f, 0f, 0.7f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            var body = new GUIStyle(GUI.skin.label) { fontSize = 22, wordWrap = true, alignment = TextAnchor.MiddleCenter, fontStyle = speaker.Length == 0 ? FontStyle.Italic : FontStyle.Normal };
            body.normal.textColor = ink;
            if (speaker.Length > 0)
            {
                var who = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = Loc.Bold };
                who.normal.textColor = speaker == "SPIRIT" ? new Color(1f, 0.85f, 0.4f) : speaker == "RADIO" ? new Color(0.62f, 0.9f, 1f) : new Color(0.78f, 1f, 0.37f);
                GUI.Label(new Rect(r.x + 14f, r.y + 4f, 300f, 24f), speaker, who);
                GUI.Label(new Rect(r.x + 14f, r.y + 22f, r.width - 28f, r.height - 26f), line, body);
            }
            else GUI.Label(new Rect(r.x + 14f, r.y + 4f, r.width - 28f, r.height - 8f), line, body);
        }

        if (cardTitle.Length > 0)
        {
            var r = new Rect(W / 2f - 380f, H * 0.2f, 760f, extra > 0 ? 330f : Mathf.Max(190f, 92f + cardBody.Split('\n').Length * 28f));
            GUI.color = new Color(0.03f, 0.04f, 0.08f, 0.9f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            var t = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = Loc.Bold, alignment = TextAnchor.MiddleCenter };
            t.normal.textColor = extra == 2 ? new Color(1f, 0.85f, 0.4f) : new Color(0.78f, 1f, 0.37f);
            GUI.Label(new Rect(r.x, r.y + 12f, r.width, 48f), cardTitle, t);
            var b = new GUIStyle(GUI.skin.label) { fontSize = 19, wordWrap = true, alignment = TextAnchor.UpperCenter };
            b.normal.textColor = ink;
            GUI.Label(new Rect(r.x + 30f, r.y + 68f, r.width - 60f, r.height - 80f), cardBody, b);
            if (extra == 2) DrawMeter(new Rect(r.center.x - 160f, r.yMax - 70f, 320f, 22f));
            if (extra == 1) DrawZones(new Rect(r.center.x - 210f, r.yMax - 110f, 420f, 80f));
        }

        if (fade > 0.001f) { GUI.color = new Color(0f, 0f, 0f, fade); GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture); GUI.color = Color.white; }

        var skip = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = Loc.Bold };
        if (GUI.Button(new Rect(W - 120f, 12f, 100f, 38f), Loc.T("SKIP >>"), skip)) Finish();
        var hint = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleRight };
        hint.normal.textColor = new Color(1f, 1f, 1f, 0.5f);
        GUI.Label(new Rect(W - 420f, H - 40f, 400f, 24f), Loc.T("tap / space = next line"), hint);
    }

    // the mic meter with its two marks: bark and shield
    void DrawMeter(Rect r)
    {
        GUI.color = new Color(0.2f, 0.2f, 0.25f); GUI.DrawTexture(r, Texture2D.whiteTexture);
        float k = 0.5f + 0.45f * Mathf.Sin(Time.time * 1.6f);
        GUI.color = k > 0.75f ? new Color(1f, 0.85f, 0.4f) : new Color(0.5f, 0.82f, 0.76f);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * k, r.height), Texture2D.whiteTexture);
        GUI.color = new Color(0.91f, 0.64f, 0.23f); GUI.DrawTexture(new Rect(r.x + r.width * 0.25f, r.y - 4f, 3f, r.height + 8f), Texture2D.whiteTexture);
        GUI.color = new Color(1f, 0.85f, 0.4f); GUI.DrawTexture(new Rect(r.x + r.width * 0.75f, r.y - 6f, 4f, r.height + 12f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        var st = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = Loc.Bold, alignment = TextAnchor.MiddleCenter };
        st.normal.textColor = new Color(0.91f, 0.64f, 0.23f);
        GUI.Label(new Rect(r.x + r.width * 0.25f - 40f, r.yMax + 4f, 80f, 18f), Loc.T("BARK"), st);
        st.normal.textColor = new Color(1f, 0.85f, 0.4f);
        GUI.Label(new Rect(r.x + r.width * 0.75f - 40f, r.yMax + 4f, 80f, 18f), Loc.T("SHIELD"), st);
    }

    // the three camera zones: back / stop / forward
    void DrawZones(Rect r)
    {
        string[] names = { "<< BACK", "STOP", "FORWARD >>" };
        Color[] cols = { new Color(0.85f, 0.4f, 0.35f), new Color(0.5f, 0.5f, 0.55f), new Color(0.5f, 0.82f, 0.76f) };
        float w = r.width / 3f;
        var st = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = Loc.Bold, alignment = TextAnchor.MiddleCenter };
        st.normal.textColor = Color.black;
        for (int i = 0; i < 3; i++)
        {
            var z = new Rect(r.x + i * w + 3f, r.y, w - 6f, r.height);
            GUI.color = cols[i]; GUI.DrawTexture(z, Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.Label(z, names[i], st);
        }
    }
}
