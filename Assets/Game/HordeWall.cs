using UnityEngine;

// Escape sequence (ฉากวิ่งหนี). When the dog passes 'triggerX', a horde of zombies starts pouring in
// from the left at a steady pace. If it reaches the master, the game is lost - keep moving!
// Closed gates, canals and candle light stop the horde, so a well-timed bark can save you.
// The members are just animated zombie bodies (no AI of their own).
public class HordeWall : MonoBehaviour
{
    public float triggerX = 0f;
    public float speed = 1.3f;
    public CutoutRig[] members;

    Transform dog, master;
    bool running;
    float x;
    AudioSource rumble;
    SpriteRenderer[] parts; Color[] partCols; float appear;

    void Start()
    {
        x = transform.position.x;
        var d = FindFirstObjectByType<DogWalker>(); if (d != null) dog = d.transform;
        var m = FindFirstObjectByType<BlindFollower>(); if (m != null) master = m.transform;
        foreach (var r in members) if (r != null) r.Face(1f);
        rumble = Sfx.Loop(gameObject, "rumble");
        parts = GetComponentsInChildren<SpriteRenderer>();
        partCols = new Color[parts.Length];
        for (int i = 0; i < parts.Length; i++) partCols[i] = parts[i].color;
        SetAlpha(0f);
    }

    void SetAlpha(float a) { for (int i = 0; i < parts.Length; i++) { var c = partCols[i]; c.a *= a; parts[i].color = c; } }

    void Update()
    {
        if (GameState.Started && appear < 1f) { appear = Mathf.MoveTowards(appear, 1f, Time.deltaTime * 1.6f); SetAlpha(appear); }
        if (rumble != null) rumble.volume = running && !GameState.IsOver ? 0.3f * Sfx.Near(transform.position, 12f) * Sfx.Volume : 0f;   // soft: loops the whole chase
        if (GameState.IsOver || GameState.Cutscene || dog == null || master == null) { SetVel(0f); return; }
        if (!running)
        {
            if (dog.position.x < triggerX) { SetVel(0f); return; }
            running = true;
            if (rumble != null) rumble.Play();
            GameState.Toast(Loc.T("RUN! A horde is coming!"));
        }
        float nx = x + speed * Time.deltaTime;
        nx = Gate.ClampMove(x, nx, 0.6f);
        nx = Lantern.ClampOutside(x, nx);
        SetVel(Time.deltaTime > 0f ? (nx - x) / Time.deltaTime : 0f);
        x = nx;
        var p = transform.position; p.x = x; transform.position = p;
        if (master.position.x - x < 0.7f && !Lantern.Safe(master.position.x))
            GameState.Lose(Loc.T("The horde caught your master. Keep moving!"));
    }

    void SetVel(float v) { foreach (var r in members) if (r != null) r.Velocity = v; }

    bool wasNear;
    void LateUpdate()
    {
        if (members == null) return;
        foreach (var r in members)   // scared of holy light: the front ones shake at the edge
        {
            if (r == null) continue;
            bool scared = Lantern.EdgeDistance(r.transform.position.x) < 0.9f;
            r.Afraid = Mathf.MoveTowards(r.Afraid, scared ? 1f : 0f, Time.deltaTime * 4f);
        }
        if (!running || master == null || GameState.IsOver) return;
        float d = master.position.x - x;
        if (!wasNear && d < 4f) { wasNear = true; Sfx.Play("zombienear", 0.9f); }
        else if (wasNear && d > 6f) wasNear = false;
    }
}
