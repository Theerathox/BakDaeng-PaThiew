using UnityEngine;

// A gate that blocks dog, tourist and zombie while closed. Bark near it to open / close.
// isOn = open. Close the gate behind you to stop the zombie.
// - Never closes on someone standing under it: it waits until the doorway is clear.
// - Rusty gates (rusty = true, long openTime) creak open slowly and shake while lifting.
public class Gate : Switchable
{
    public SpriteRenderer panel;
    public SpriteRenderer lamp;
    public float liftHeight = 2.6f;
    [Tooltip("Seconds to lift fully. Normal ~0.35. Rusty gate ~7 (passable after ~85%)")]
    public float openTime = 0.35f;
    public float closeTime = 0.35f;
    public bool rusty;

    float open;   // 0 closed .. 1 open
    Transform[] bodies;
    static readonly Color OpenCol = new Color(0.5f, 0.82f, 0.76f);
    static readonly Color ClosedCol = new Color(0.91f, 0.64f, 0.23f);

    public bool Blocking => open < 0.85f;

    public override void Toggle()
    {
        base.Toggle();
        Sfx.Play(rusty && isOn ? "creak" : "clunk", Sfx.Near(transform.position));
    }
    public bool Opening => isOn && open < 1f;

    void Start()
    {
        open = isOn ? 1f : 0f;
        var list = new System.Collections.Generic.List<Transform>();
        foreach (var d in FindObjectsByType<DogWalker>(FindObjectsSortMode.None)) list.Add(d.transform);
        foreach (var b in FindObjectsByType<BlindFollower>(FindObjectsSortMode.None)) list.Add(b.transform);
        foreach (var z in FindObjectsByType<ZombieChaser>(FindObjectsSortMode.None)) list.Add(z.transform);
        bodies = list.ToArray();
        Apply(0f);
    }

    bool Occupied()
    {
        float gx = transform.position.x;
        foreach (var b in bodies) if (b != null && b.gameObject.activeInHierarchy && Mathf.Abs(b.position.x - gx) < 0.7f) return true;
        return false;
    }

    void Update()
    {
        float target = isOn ? 1f : 0f;
        if (!isOn && !Blocking && Occupied()) target = open;   // wait for the doorway to clear
        float time = target > open ? openTime : closeTime;
        open = Mathf.MoveTowards(open, target, Time.deltaTime / Mathf.Max(0.01f, time));
        float shake = rusty && Opening ? Mathf.Sin(Time.time * 45f) * 0.04f : 0f;
        Apply(shake);
    }

    void Apply(float shake)
    {
        if (panel != null)
        {
            panel.transform.localPosition = new Vector3(shake, open * liftHeight, 0f);
            var c = panel.color; c.a = 1f - 0.65f * open; panel.color = c;
        }
        if (lamp != null)
        {
            bool blink = rusty && Opening && Mathf.Repeat(Time.time, 0.5f) < 0.25f;
            lamp.color = isOn ? (blink ? ClosedCol : OpenCol) : ClosedCol;
        }
    }

    // Moving from 'from' to 'to' along x: stop 'margin' before any closed gate (or un-bridged canal).
    public static float ClampMove(float from, float to, float margin)
    {
        foreach (var s in All)
        {
            if (!(s is Gate g) || !g.Blocking) continue;
            float gx = g.transform.position.x;
            if (from < gx) to = Mathf.Min(to, Mathf.Max(from, gx - margin));
            else to = Mathf.Max(to, Mathf.Min(from, gx + margin));
        }
        return Canal.Clamp(from, to, margin);
    }
}
