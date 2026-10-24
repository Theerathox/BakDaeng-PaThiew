using UnityEngine;

// Zombie that threatens the blind tourist (it never hurts the dog).
//   Chaser: shambles toward the tourist.
//   Guard : stands at its post, does not chase - but strikes fast if the tourist walks past.
// When in reach it raises its arms (wind-up). If the tourist is still in reach when the wind-up
// ends, it strikes and the game is lost. Escaping out of reach cancels the attack.
// Radio: while a radio it can hear is playing, it runs to it and dances. On the way it only hits the
// tourist if he stands in its way (between it and the radio). Song over -> back to chasing / to its post.
// Bark noise: a zombie within 'noiseHearing' walks to where the dog barked (guards leave their post),
// looks around, then goes back to chasing / its post. The radio beats a bark.
// Asleep: lies on the ground and ignores everything until a noise (bark, cans) within 'wakeHearing'
// wakes it up - then it behaves like its kind. Zombies never hit through a closed gate or across open water. Fall(): drops into a canal (drawbridge / falling palm) and is gone.
[RequireComponent(typeof(CutoutRig))]
public class ZombieChaser : MonoBehaviour
{
    public enum Kind { Chaser, Guard }

    public Kind kind = Kind.Chaser;
    public Transform target;
    public float speed = 0.55f;
    public float reach = 1.0f;
    [Tooltip("Extra distance needed to escape once the wind-up has started")]
    public float escapeMargin = 0.35f;
    public float windupTime = 1.4f;
    public float strikeTime = 0.18f;
    public float recoverTime = 0.6f;

    [Header("Guard")]
    public float guardReach = 2.0f;
    public float guardWindup = 0.5f;
    public float returnSpeed = 1.2f;

    [Header("Hears the bark")]
    public float noiseHearing = 10f;
    public float investigateSpeed = 1.1f;
    public float investigateTime = 6f;
    [Tooltip("Once it reaches the noise spot it stands and looks around this long before going back")]
    public float lookTime = 3f;

    [Header("Asleep")]
    public bool asleep;
    public float wakeHearing = 6f;

    [Header("Radio")]
    public float hearing = 30f;
    public float luredSpeed = 3f;
    public float luredWindup = 0.35f;

    enum State { Chase, Windup, Strike, Recover, Lured, Dance }
    State state = State.Chase;
    float timer, homeX, curWindup = 1.4f, curReach = 1f;
    bool investigating; float noiseX, noiseUntil, lookUntil = -1f;
    float wake = 1f;      // 0 lying down .. 1 standing
    bool falling; float fallT;
    float standY;
    CutoutRig rig;
    Camera cam;
    GameObject zzz;
    SpriteRenderer[] parts; Color[] partCols; float appear = -1f;   // fade in once the level has started

    public bool Gone => falling;

    [Header("Close by")]
    [Tooltip("Plays the 'zombie close' sound when it gets this near to your master")]
    public float nearSound = 3.5f;
    bool wasNear;

    void OnEnable() { Noise.OnSound += Heard; }
    void OnDisable() { Noise.OnSound -= Heard; }

    void Heard(Vector3 at, float loudness)
    {
        if (falling) return;
        float d = Mathf.Abs(at.x - transform.position.x);
        if (asleep)
        {
            if (d > wakeHearing * loudness) return;
            asleep = false;
            GameState.Toast(Loc.T("A sleeping zombie woke up!"));
            Sfx.Play("groan", Sfx.Near(transform.position), 0.8f);
        }
        if (d > noiseHearing * loudness) return;
        investigating = true; noiseX = at.x; noiseUntil = Time.time + investigateTime; lookUntil = -1f;
    }

    // drop into the water and disappear
    public void Fall()
    {
        if (falling) return;
        falling = true; fallT = 0f; rig.Velocity = 0f;
        Sfx.Play("splash", Sfx.Near(transform.position));
        GameState.Toast(Loc.T("Splash! One zombie less."));
    }

    void Start()
    {
        rig = GetComponent<CutoutRig>();
        cam = Camera.main;
        homeX = transform.position.x;
        standY = transform.position.y;
        if (target == null) { var b = FindFirstObjectByType<BlindFollower>(); if (b != null) target = b.transform; }
        if (asleep) { wake = 0f; zzz = MakeZzz(); }
        Pose();
        parts = GetComponentsInChildren<SpriteRenderer>();
        partCols = new Color[parts.Length];
        for (int i = 0; i < parts.Length; i++) partCols[i] = parts[i].color;
        SetAlpha(0f);
    }

    void SetAlpha(float a)
    {
        for (int i = 0; i < parts.Length; i++) { var c = partCols[i]; c.a *= a; parts[i].color = c; }
        if (zzz != null) zzz.SetActive(asleep && a > 0.5f);
    }

    // lying down <-> standing up: rotate around the hips and lower to the ground
    void Pose()
    {
        float lie = 1f - wake;
        float side = Mathf.Sign(transform.localScale.x);
        transform.rotation = Quaternion.Euler(0f, 0f, 82f * lie * side);
        var p = transform.position; p.y = standY - 1.55f * lie; transform.position = p;
        if (zzz != null) zzz.SetActive(asleep && appear >= 0.5f);
    }

    GameObject MakeZzz()
    {
        var t = new GameObject("zzz").AddComponent<TextMesh>();
        t.text = "z z Z"; t.characterSize = 0.08f; t.fontSize = 40; t.anchor = TextAnchor.MiddleCenter;
        t.color = new Color(0.62f, 0.9f, 1f);
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.font = font; t.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        t.GetComponent<MeshRenderer>().sortingOrder = 20;
        t.transform.position = transform.position + new Vector3(0f, 0.6f, 0f);
        return t.gameObject;
    }

    void Update()
    {
        if (!GameState.Started) { rig.Velocity = 0f; return; }   // hidden until the walk-in and title are done
        if (appear < 1f)
        {
            if (appear < 0f) { appear = 0f; if (Sfx.Near(transform.position) > 0.2f) Sfx.Play("groan", 0.6f * Sfx.Near(transform.position), 0.9f); }
            appear = Mathf.MoveTowards(appear, 1f, Time.deltaTime * 1.6f);
            SetAlpha(appear);
        }
        if (falling)
        {
            fallT += Time.deltaTime;
            transform.position += Vector3.down * (4f * fallT) * Time.deltaTime * 3f;
            transform.Rotate(0f, 0f, 120f * Time.deltaTime);
            if (fallT > 1.2f) gameObject.SetActive(false);
            return;
        }
        if (asleep || wake < 1f)
        {
            if (!asleep) wake = Mathf.MoveTowards(wake, 1f, Time.deltaTime * 1.5f);   // getting up
            if (zzz != null) zzz.transform.position = new Vector3(transform.position.x, standY - 1.0f + 0.15f * Mathf.Sin(Time.time * 2f), 0f);
            Pose();
            rig.Velocity = 0f;
            return;
        }
        if (target == null || GameState.IsOver || GameState.Cutscene) { rig.Velocity = 0f; return; }
        rig.Dance = Mathf.MoveTowards(rig.Dance, state == State.Dance ? 1f : 0f, Time.deltaTime * 3f);   // eases in / out of the mor lam dance
        // scared of holy light: shakes at the edge of a candle / the spirit shield
        bool scared = state != State.Dance && Lantern.EdgeDistance(transform.position.x) < 0.7f;
        rig.Afraid = Mathf.MoveTowards(rig.Afraid, scared ? 1f : 0f, Time.deltaTime * 4f);
        // "zombie close" sound when it comes near your master (not every frame: once per approach)
        float md = Mathf.Abs(target.position.x - transform.position.x);
        if (!wasNear && md < nearSound && state != State.Dance) { wasNear = true; Sfx.Play("zombienear", Mathf.Max(0.4f, Sfx.Near(transform.position))); }
        else if (wasNear && md > nearSound + 2f) wasNear = false;
        float dx = target.position.x - transform.position.x;
        float dist = Mathf.Abs(dx);
        timer += Time.deltaTime;

        bool attacking = state == State.Windup || state == State.Strike;
        var radio = attacking ? null : Radio.Heard(transform.position.x, hearing);
        if (radio != null) { FollowMusic(radio, dx, dist); return; }
        if (state == State.Lured || state == State.Dance) Go(State.Chase);

        switch (state)
        {
            case State.Chase:
                rig.ArmRaise = Mathf.MoveTowards(rig.ArmRaise, 0f, Time.deltaTime * 2f);
                rig.ArmStrike = 0f;
                float r = kind == Kind.Guard ? guardReach : reach;
                bool safe = Lantern.Safe(target.position.x);   // candle light protects the master
                if (dist <= r && !safe && CanReach()) { StartWindup(kind == Kind.Guard ? guardWindup : windupTime, r); break; }
                if (investigating)
                {
                    float toNoise = noiseX - transform.position.x;
                    bool there = Mathf.Abs(toNoise) < 0.4f;
                    if (there && lookUntil < 0f) lookUntil = Time.time + lookTime;   // arrived: look around a while
                    // blocked on the way (gate, canal, candle light): keeps trying until investigateTime runs out
                    if ((lookUntil >= 0f && Time.time > lookUntil) || (lookUntil < 0f && Time.time > noiseUntil)) { investigating = false; lookUntil = -1f; }
                    else if (!there) { Walk(Mathf.Sign(toNoise) * investigateSpeed); rig.Face(toNoise); break; }
                    else { rig.Velocity = 0f; rig.Face(Mathf.Sin(Time.time * 1.6f) >= 0f ? 1f : -1f); break; }
                }
                if (kind == Kind.Chaser) { Walk(Mathf.Sign(dx) * speed); rig.Face(dx); }
                else
                {
                    float toHome = homeX - transform.position.x;   // guard: walk back to its post
                    if (Mathf.Abs(toHome) > 0.05f) { Walk(Mathf.Sign(toHome) * returnSpeed); rig.Face(toHome); }
                    else { rig.Velocity = 0f; rig.Face(dx); }
                }
                break;

            case State.Windup:
                rig.Velocity = 0f; rig.Face(dx);
                rig.ArmRaise = Mathf.Clamp01(timer / curWindup);
                if (dist > curReach + escapeMargin || !CanReach()) { Go(State.Recover); break; }
                if (timer >= curWindup) Go(State.Strike);
                break;

            case State.Strike:
                rig.ArmStrike = Mathf.Clamp01(timer / strikeTime);
                if (timer >= strikeTime)
                {
                    if (dist <= curReach + escapeMargin && !Lantern.Safe(target.position.x) && CanReach())
                        GameState.Lose(kind == Kind.Guard
                            ? "The guard zombie got him. Lure it away before walking past!"
                            : "The zombie got the tourist. Move the dog away when the arms go up!");
                    Go(State.Recover);
                }
                break;

            case State.Recover:
                rig.Velocity = 0f;
                rig.ArmStrike = Mathf.MoveTowards(rig.ArmStrike, 0f, Time.deltaTime * 3f);
                rig.ArmRaise = Mathf.MoveTowards(rig.ArmRaise, 0f, Time.deltaTime * 2f);
                if (timer >= recoverTime) Go(State.Chase);
                break;
        }
    }

    void FollowMusic(Radio radio, float dx, float dist)
    {
        rig.ArmStrike = 0f;
        float toR = radio.transform.position.x - transform.position.x;
        if (Mathf.Abs(toR) > 0.85f)
        {
            // tourist standing between me and the radio = in my way
            bool inWay = Mathf.Sign(dx) == Mathf.Sign(toR) && dist < Mathf.Abs(toR) && dist <= reach && !Lantern.Safe(target.position.x) && CanReach();
            if (inWay) { StartWindup(luredWindup, reach); return; }
            if (state != State.Lured) Go(State.Lured);
            Walk(Mathf.Sign(toR) * luredSpeed);
            rig.Face(toR);
            rig.ArmRaise = Mathf.MoveTowards(rig.ArmRaise, 0.3f, Time.deltaTime * 2f);
        }
        else
        {
            if (state != State.Dance) Go(State.Dance);
            rig.Velocity = 0f;
            rig.ArmRaise = Mathf.MoveTowards(rig.ArmRaise, 0f, Time.deltaTime * 3f);   // the Dance pose moves the arms
            rig.Face(Mathf.Sin(timer * 1.1f) >= 0f ? 1f : -1f);                        // turns this way and that
        }
    }

    void Walk(float v)
    {
        var p = transform.position;
        float nx = Gate.ClampMove(p.x, p.x + v * Time.deltaTime, 0.6f);   // closed gates stop zombies
        nx = Lantern.ClampOutside(p.x, nx);                                // and so does candle light
        rig.Velocity = Time.deltaTime > 0f ? (nx - p.x) / Time.deltaTime : 0f;
        p.x = nx; transform.position = p;
    }

    // a closed gate or an open canal between us = can't hit him
    bool CanReach() => target != null && Mathf.Abs(Gate.ClampMove(transform.position.x, target.position.x, 0f) - target.position.x) < 0.01f;

    void StartWindup(float time, float r)
    {
        curWindup = time; curReach = r; rig.Velocity = 0f; Go(State.Windup);
        Sfx.Play("groan", Sfx.Near(transform.position), Random.Range(0.85f, 1.1f));
    }

    void Go(State s) { state = s; timer = 0f; }

    // Wind-up warning bar above the zombie
    void OnGUI()
    {
        if (state != State.Windup || cam == null || GameState.IsOver || PetZoneDetector.SetupOpen) return;
        Vector3 top = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.6f);
        float k = Mathf.Clamp01(timer / curWindup);
        var r = new Rect(top.x - 40f, Screen.height - top.y, 80f, 10f);
        GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.Lerp(new Color(0.91f, 0.64f, 0.23f), new Color(0.85f, 0.28f, 0.23f), k);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * k, r.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
