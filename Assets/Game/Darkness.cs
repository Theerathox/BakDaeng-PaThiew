using System.Collections.Generic;
using UnityEngine;

// Night levels. The screen is dark except a circle of light around the dog, candles (Lantern)
// and the small lamps on gates. Every bark sends out a sound wave (the blind master "sees" with
// his ears): things the wave reaches - zombies, gates, radios, shrines - light up for a moment.
// But zombies hear the bark too (see DogBark.OnNoise), so seeing has a price.
// Put one on the "Game" object of a night level; everything else is found automatically.
public class Darkness : MonoBehaviour
{
    public Color darkColor = new Color(0.02f, 0.03f, 0.07f, 0.94f);
    [Tooltip("Radius of the light around the dog")]
    public float dogLight = 3.4f;
    [Tooltip("How far the bark's sound wave reaches")]
    public float revealRadius = 11f;
    public float waveSpeed = 14f;
    [Tooltip("How long a revealed thing stays visible")]
    public float revealTime = 2.5f;
    public float echoRadius = 1.7f;

    public static bool Active { get; private set; }
    public const int DarkOrder = 100;   // anything drawn above this shows through the dark

    static Sprite disc, soft, square;
    SpriteRenderer dark;
    Transform cam, dog;
    Material lineMat;

    void Awake() { Active = true; }
    void OnDestroy() { Active = false; Noise.OnSound -= Wave; }

    void Start()
    {
        MakeSprites();
        cam = Camera.main != null ? Camera.main.transform : null;
        var walker = FindFirstObjectByType<DogWalker>();
        if (walker != null) dog = walker.transform;
        var bark = FindFirstObjectByType<DogBark>();
        if (bark != null) lineMat = bark.ringMaterial;

        dark = new GameObject("Darkness").AddComponent<SpriteRenderer>();
        dark.sprite = square;
        dark.color = darkColor;
        dark.sortingOrder = DarkOrder;
        dark.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
        dark.transform.localScale = new Vector3(400f, 200f, 1f);

        if (dog != null) AddLight(dog, new Vector3(0f, 0.7f, 0f), dogLight, -1f, 0f, true);
        foreach (var l in FindObjectsByType<Lantern>(FindObjectsSortMode.None)) AddLight(l.transform, new Vector3(0f, 0.6f, 0f), l.radius, -1f, 0f, true);
        foreach (var g in FindObjectsByType<Gate>(FindObjectsSortMode.None))
            if (g.lamp != null) AddLight(g.lamp.transform, Vector3.zero, 0.45f, -1f);   // gate lamps glow a little

        Noise.OnSound += Wave;
    }

    void LateUpdate()
    {
        if (cam != null) dark.transform.position = new Vector3(cam.position.x, cam.position.y, 0f);
    }

    // a hole in the dark that follows 'target'. life < 0 = permanent; else it opens, then closes over 'life' seconds
    public LightHole AddLight(Transform target, Vector3 offset, float radius, float life, float delay = 0f, bool softRim = false)
    {
        var go = new GameObject("LightHole");
        var mask = go.AddComponent<SpriteMask>();
        mask.sprite = disc; mask.alphaCutoff = 0.5f;
        if (softRim)   // soft rim so the big lights aren't a hard cut
        {
            var edge = new GameObject("edge").AddComponent<SpriteRenderer>();
            edge.transform.SetParent(go.transform, false);
            edge.sprite = soft; edge.color = darkColor; edge.sortingOrder = DarkOrder + 1;
        }
        var h = go.AddComponent<LightHole>();
        h.Init(target, offset, radius, life, delay);
        return h;
    }

    void Wave(Vector3 at, float loudness)
    {
        if (loudness < 0.6f) return;   // footsteps are too quiet to 'see' with
        SpawnWaveRing(at + Vector3.up * 0.8f, loudness);
        Sfx.Play("ping", 0.5f);
        var seen = new List<Transform>();
        foreach (var z in FindObjectsByType<ZombieChaser>(FindObjectsSortMode.None)) seen.Add(z.transform);
        foreach (var s in Switchable.All) seen.Add(s.transform);
        foreach (var t in seen)
        {
            float d = Mathf.Abs(t.position.x - at.x);
            if (d > revealRadius * loudness || (dog != null && d < dogLight * 0.6f)) continue;
            AddLight(t, new Vector3(0f, 1.3f, 0f), echoRadius, revealTime, d / waveSpeed);
        }
    }

    // the dog sniffs: things close by show up for a moment, with no sound at all (DogSniff)
    public void Sniff(Vector3 at, float radius)
    {
        if (dog != null) AddLight(dog, new Vector3(0f, 0.7f, 0f), radius, revealTime, 0f, true);
        var seen = new List<Transform>();
        foreach (var z in FindObjectsByType<ZombieChaser>(FindObjectsSortMode.None)) seen.Add(z.transform);
        foreach (var s in Switchable.All) seen.Add(s.transform);
        foreach (var s in ScentSpot.All) seen.Add(s.transform);
        foreach (var t in seen)
        {
            float d = Mathf.Abs(t.position.x - at.x);
            if (d > radius) continue;
            AddLight(t, new Vector3(0f, 1.0f, 0f), echoRadius, revealTime * 1.3f, d / 8f);
        }
    }

    void SpawnWaveRing(Vector3 at, float loudness)
    {
        var go = new GameObject("SoundWave");
        go.transform.position = at;
        var lr = go.AddComponent<LineRenderer>();
        lr.material = lineMat;
        lr.loop = true; lr.useWorldSpace = false; lr.positionCount = 64;
        lr.widthMultiplier = 0.08f;
        lr.sortingOrder = DarkOrder + 60;
        go.AddComponent<WaveRing>().Init(lr, revealRadius * loudness, revealRadius * loudness / waveSpeed);
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
                float r = new Vector2(x + 0.5f - N / 2f, y + 0.5f - N / 2f).magnitude / (N / 2f);   // 0 centre .. 1 edge
                a.SetPixel(x, y, new Color(1f, 1f, 1f, r <= 1f ? 1f : 0f));
                float rim = r <= 1f ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, r)) : 0f;
                b.SetPixel(x, y, new Color(1f, 1f, 1f, rim));
            }
        a.Apply(); b.Apply();
        disc = Sprite.Create(a, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);   // 1 unit across
        soft = Sprite.Create(b, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        var w = new Texture2D(1, 1); w.SetPixel(0, 0, Color.white); w.Apply();
        square = Sprite.Create(w, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
    }
}

// one light circle in the dark; follows its target (not parented, so flips/scales don't affect it)
public class LightHole : MonoBehaviour
{
    Transform target; Vector3 offset; float radius, life, delay, t;

    public void Init(Transform tgt, Vector3 off, float r, float lifeTime, float wait)
    {
        target = tgt; offset = off; radius = r; life = lifeTime; delay = wait;
        Place(life < 0f ? 1f : 0f);
    }

    void LateUpdate()
    {
        if (target == null) { Destroy(gameObject); return; }
        if (life < 0f) { Place(1f); return; }
        t += Time.deltaTime;
        float k = t < delay ? 0f : Mathf.Clamp01((t - delay) / life);
        // pop open fast, then slowly close
        float size = k <= 0f ? 0f : k < 0.12f ? k / 0.12f : 1f - Mathf.SmoothStep(0f, 1f, (k - 0.12f) / 0.88f);
        Place(size);
        if (k >= 1f) Destroy(gameObject);
    }

    void Place(float size)
    {
        transform.position = target.position + offset;
        transform.localScale = Vector3.one * Mathf.Max(0.0001f, radius * 2f * size);
    }
}

public class WaveRing : MonoBehaviour
{
    LineRenderer lr; float maxR, time, t;
    static readonly Color Col = new Color(0.62f, 0.9f, 1f);

    public void Init(LineRenderer l, float r, float dur) { lr = l; maxR = r; time = Mathf.Max(0.1f, dur); Draw(0.2f, 1f); }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / time);
        Draw(Mathf.Lerp(0.2f, maxR, k), 1f - k * k);
        if (k >= 1f) Destroy(gameObject);
    }

    void Draw(float r, float a)
    {
        for (int i = 0; i < lr.positionCount; i++)
        {
            float ang = i * Mathf.PI * 2f / lr.positionCount;
            lr.SetPosition(i, new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r * 0.55f, 0f));   // flattened: reads as a wave along the street
        }
        var c = Col; c.a = a * 0.8f;
        lr.startColor = lr.endColor = c;
    }
}
