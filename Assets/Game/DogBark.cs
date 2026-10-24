using UnityEngine;

// When the player barks (voice / button / B key): the dog lifts its head, a sound ring
// spreads from its mouth, and the nearest switchable object in range toggles.
// A pulsing marker always shows WHICH object the next bark will hit (or nothing if none is in range).
// Every bark is also a NOISE: the blind tourist (sharp ears) runs to the dog at once,
// and zombies that hear it come to look too (OnNoise).
public class DogBark : MonoBehaviour
{
    public static event System.Action<Vector3> OnNoise;

    public Material ringMaterial;
    public Vector2 mouthLocal = new Vector2(1.6f, 0.75f);   // in the dog's 'body' bone space
    public float ringRadius = 2.6f;
    public float ringTime = 0.6f;

    Transform body;
    LineRenderer marker;
    BarkHints hints;
    static readonly Color Amber = new Color(0.91f, 0.64f, 0.23f);

    void OnEnable() { VoiceTrigger.OnVoice += Bark; }
    void OnDisable() { VoiceTrigger.OnVoice -= Bark; }

    void Start()
    {
        body = transform.Find("body");
        marker = MakeMarker();
        hints = gameObject.AddComponent<BarkHints>();
        hints.Init(ringMaterial);
    }

    public void Bark()
    {
        if (GameState.IsOver) return;
        if (LevelSelect.Open || PetPause.Paused || Guide.Open) return;
        DogPose.On(gameObject).Bark();   // head up, body rocks back, tail flick
        Vector3 mouth = body != null ? body.TransformPoint(mouthLocal) : transform.position;
        SpawnRing(mouth);
        Sfx.Play("bark", 0.7f, Random.Range(0.95f, 1.08f));
        var s = Switchable.Nearest(transform.position);
        if (s != null) s.Toggle();
        OnNoise?.Invoke(transform.position);   // the master answers his dog's bark
        Noise.Emit(transform.position, 1f);    // zombies (and the night sound wave) hear any noise
    }

    void LateUpdate()
    {
        UpdateMarker();
    }

    // ring + arrow pointing down at the current bark target
    LineRenderer MakeMarker()
    {
        var go = new GameObject("BarkTargetMarker");
        var lr = go.AddComponent<LineRenderer>();
        lr.material = ringMaterial;
        lr.useWorldSpace = false;
        lr.loop = false;
        lr.widthMultiplier = 0.07f;
        lr.sortingOrder = Darkness.DarkOrder + 70;   // above the night darkness
        const int n = 28;
        lr.positionCount = n + 1 + 3;
        for (int i = 0; i <= n; i++)
        {
            float a = -Mathf.PI / 2f + i * Mathf.PI * 2f / n;   // start at the bottom so the arrow hangs below
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * 0.32f, Mathf.Sin(a) * 0.32f + 0.55f, 0f));
        }
        // arrow under the ring
        lr.SetPosition(n + 1, new Vector3(-0.2f, 0.1f, 0f));
        lr.SetPosition(n + 2, new Vector3(0f, -0.15f, 0f));
        lr.SetPosition(n + 3, new Vector3(0.2f, 0.1f, 0f));
        go.SetActive(false);
        return lr;
    }

    void UpdateMarker()
    {
        if (marker == null) return;
        var s = GameState.IsOver || GameState.Cutscene ? null : Switchable.Nearest(transform.position);
        if (hints != null) hints.Show(s);   // what this bark will do (word, palm fall zone, lever links...)
        marker.gameObject.SetActive(s != null);
        if (s == null) return;
        float t = Time.time;
        marker.transform.position = s.MarkerPoint + Vector3.up * (0.12f * Mathf.Sin(t * 5f));
        marker.transform.localScale = Vector3.one * (1f + 0.1f * Mathf.Sin(t * 10f));
        var c = Amber; c.a = 0.75f + 0.25f * Mathf.Sin(t * 10f);
        marker.startColor = marker.endColor = c;
    }

    void SpawnRing(Vector3 at)
    {
        var go = new GameObject("BarkRing");
        go.transform.position = at;
        var lr = go.AddComponent<LineRenderer>();
        lr.material = ringMaterial;
        lr.loop = true;
        lr.useWorldSpace = false;
        lr.positionCount = 40;
        lr.widthMultiplier = 0.06f;
        lr.sortingOrder = Darkness.DarkOrder + 60;
        go.AddComponent<RingFx>().Init(lr, ringRadius, ringTime);
    }
}

public class RingFx : MonoBehaviour
{
    LineRenderer lr; float maxR, time, t;
    static readonly Color Amber = new Color(0.91f, 0.64f, 0.23f);

    public void Init(LineRenderer l, float r, float dur) { lr = l; maxR = r; time = dur; Draw(0.1f, 1f); }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / time);
        Draw(Mathf.Lerp(0.1f, maxR, 1f - (1f - k) * (1f - k)), 1f - k);
        if (k >= 1f) Destroy(gameObject);
    }

    void Draw(float r, float a)
    {
        for (int i = 0; i < lr.positionCount; i++)
        {
            float ang = i * Mathf.PI * 2f / lr.positionCount;
            lr.SetPosition(i, new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, 0f));
        }
        var c = Amber; c.a = a;
        lr.startColor = lr.endColor = c;
    }
}
