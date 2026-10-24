using System.Collections.Generic;
using UnityEngine;

// Spirit candle (เทียนเจ้าที่). A circle of holy light: zombies will not step into it and cannot
// hurt the master while he stands inside. In night levels it is also a light in the dark.
// Use it as a safe spot: bark to call the master into the light, then deal with the zombie.
public class Lantern : MonoBehaviour
{
    public static readonly List<Lantern> All = new List<Lantern>();

    public float radius = 1.8f;
    public SpriteRenderer flame;
    public SpriteRenderer glow;

    Vector3 flameScale, glowScale;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Start()
    {
        if (flame != null) flameScale = flame.transform.localScale;
        if (glow != null) glowScale = glow.transform.localScale;
    }

    void Update()
    {
        float f = 1f + 0.12f * Mathf.Sin(Time.time * 11f) + 0.06f * Mathf.Sin(Time.time * 23f);
        if (flame != null) flame.transform.localScale = flameScale * f;
        if (glow != null) glow.transform.localScale = glowScale * (0.97f + 0.03f * f);
    }

    // distance from x to the edge of the nearest light it is OUTSIDE of (big = no light around)
    public static float EdgeDistance(float x)
    {
        float best = 999f;
        foreach (var l in All)
        {
            if (!l.isActiveAndEnabled) continue;
            float d = Mathf.Abs(x - l.transform.position.x) - l.radius;
            if (d > -0.3f) best = Mathf.Min(best, Mathf.Max(0f, d));
        }
        return best;
    }

    public static bool Safe(float x)
    {
        foreach (var l in All) if (Mathf.Abs(x - l.transform.position.x) <= l.radius) return true;
        return false;
    }

    // zombies walking from 'from' toward 'to' stop at the edge of any candle light
    public static float ClampOutside(float from, float to)
    {
        foreach (var l in All)
        {
            float lx = l.transform.position.x, r = l.radius;
            if (Mathf.Abs(from - lx) < r - 0.01f) continue;   // already inside (e.g. light appeared on it): let it walk out
                                                              // (a zombie parked exactly on the edge does NOT count as inside)
            if (from < lx) to = Mathf.Min(to, Mathf.Max(from, lx - r));
            else to = Mathf.Max(to, Mathf.Min(from, lx + r));
        }
        return to;
    }
}
