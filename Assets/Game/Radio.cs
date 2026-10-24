using UnityEngine;

// Mor lam radio. Bark to switch it on: every zombie that can hear it runs to it and dances.
// A running zombie only hits the tourist if he stands in its way (between the zombie and the radio).
// playTime > 0: the song ends after that many seconds and the zombies go back to normal.
public class Radio : Switchable
{
    public float playTime = 0f;          // 0 = plays forever
    public SpriteRenderer speaker;
    public SpriteRenderer lamp;
    public SpriteRenderer[] notes;

    float left;
    Vector3 speakerScale;
    Camera cam;
    AudioSource music;

    public float TimeLeft => left;

    // nearest playing radio within 'range' of x (null = none)
    public static Radio Heard(float x, float range)
    {
        Radio best = null; float bestD = range;
        foreach (var s in All)
        {
            if (!(s is Radio r) || !r.isOn) continue;
            float d = Mathf.Abs(r.transform.position.x - x);
            if (d <= bestD) { best = r; bestD = d; }
        }
        return best;
    }

    public override void Toggle()
    {
        base.Toggle();
        left = playTime;
        Sfx.Play("click", Sfx.Near(transform.position));
    }

    void Start()
    {
        cam = Camera.main;
        if (speaker != null) speakerScale = speaker.transform.localScale;
        music = Sfx.Loop(gameObject, "morlam");
        if (isOn) left = playTime;
    }

    void Update()
    {
        if (isOn && playTime > 0f)
        {
            left -= Time.deltaTime;
            if (left <= 0f) { isOn = false; GameState.Toast(Loc.T("The song ended - the zombie is coming back!")); }
        }

        if (music != null)   // mor lam plays while on, louder when the radio is on screen
        {
            if (isOn && !music.isPlaying) music.Play();
            if (!isOn && music.isPlaying) music.Stop();
            music.volume = isOn ? (SoundBank.Clip("morlam") != null ? 0.45f : 0.25f) * Sfx.Near(transform.position, 14f) * Sfx.Volume : 0f;   // soft: plays for a long time
        }

        float t = Time.time;
        if (speaker != null)
            speaker.transform.localScale = speakerScale * (isOn ? 1f + 0.18f * Mathf.Abs(Mathf.Sin(t * 9f)) : 1f);
        if (lamp != null) lamp.color = isOn ? new Color(0.5f, 0.82f, 0.76f) : new Color(0.35f, 0.3f, 0.28f);
        if (notes != null)
            for (int i = 0; i < notes.Length; i++)
            {
                var n = notes[i];
                if (n == null) continue;
                n.enabled = isOn;
                if (!isOn) continue;
                float ph = Mathf.Repeat(t * 0.55f + i / (float)notes.Length, 1f);
                n.transform.localPosition = new Vector3(-0.2f + 0.35f * Mathf.Sin(ph * 7f + i), 1.3f + ph * 1.8f, 0f);
                var c = n.color; c.a = 1f - ph; n.color = c;
            }
    }

    // time-left bar above the radio
    void OnGUI()
    {
        if (!isOn || playTime <= 0f || cam == null || GameState.IsOver || PetZoneDetector.SetupOpen) return;
        Vector3 top = cam.WorldToScreenPoint(transform.position + Vector3.up * 1.6f);
        float k = Mathf.Clamp01(left / playTime);
        var r = new Rect(top.x - 45f, Screen.height - top.y, 90f, 9f);
        GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = k > 0.3f ? new Color(0.5f, 0.82f, 0.76f) : new Color(0.91f, 0.64f, 0.23f);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * k, r.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
