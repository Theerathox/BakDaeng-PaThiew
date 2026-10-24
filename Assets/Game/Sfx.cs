using System.Collections.Generic;
using UnityEngine;

// Game sounds, made in code (no audio files needed). Sfx.Play("bark") from anywhere.
// IMPORTANT: the player's microphone listens for a HELD, voiced "woof" (VoiceTrigger), so no sound
// here is a long steady tone: everything is noisy, inharmonic or short plucks, so the game can't
// trigger its own bark through the phone speaker.
// PET-FRIENDLY: the pet is right next to the phone, so sounds are soft, short and low (no shrill tones,
// no long loud loops, the dog's bark is one muffled 'wuf' - a real-sounding bark makes pets look for the dog).
public static class Sfx
{
    const int Rate = 22050;
    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    static AudioSource[] pool;
    static int next;
    static float volume = -1f;

    public static float Volume
    {
        get { if (volume < 0f) volume = PlayerPrefs.GetFloat("sfx.volume", 0.5f); return volume; }
        set { volume = Mathf.Clamp01(value); PlayerPrefs.SetFloat("sfx.volume", volume); }
    }

    static readonly Dictionary<string, float> busyUntil = new Dictionary<string, float>();

    public static void Play(string name, float vol = 1f, float pitch = 1f)
    {
        var clip = Get(name);
        if (clip == null || Volume <= 0f) return;
        var rec = SoundBank.Clip(name);
        if (rec != null)   // a recorded sound: the bank's loudness, and long ones don't pile up on each other
        {
            vol *= SoundBank.I.sfxVolume; pitch = Mathf.Lerp(1f, pitch, 0.3f);
            if (busyUntil.TryGetValue(name, out float until) && Time.unscaledTime < until) return;
            if (rec.length > 1.5f) busyUntil[name] = Time.unscaledTime + rec.length * 0.6f;
        }
        Pool();
        var src = pool[next]; next = (next + 1) % pool.Length;
        src.pitch = pitch;
        src.PlayOneShot(clip, vol * Volume);
    }

    // a looping sound on a scene object (radio music, horde rumble). Volume is set by the caller each frame.
    public static AudioSource Loop(GameObject owner, string name)
    {
        var src = owner.AddComponent<AudioSource>();
        src.clip = Get(name); src.loop = true; src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 0f;
        return src;
    }

    // louder the closer it is to the middle of the screen
    public static float Near(Vector3 pos, float range = 14f)
    {
        var cam = Camera.main;
        if (cam == null) return 1f;
        return Mathf.Clamp01(1f - Mathf.Abs(pos.x - cam.transform.position.x) / range);
    }

    static void Pool()
    {
        if (pool != null && pool[0] != null) return;
        var go = new GameObject("Sfx");
        go.transform.position = new Vector3(0f, -500f, 0f);   // 2D sound: position does not matter; keeps the speaker gizmo off screen
        Object.DontDestroyOnLoad(go);
        pool = new AudioSource[8];
        for (int i = 0; i < pool.Length; i++) { pool[i] = go.AddComponent<AudioSource>(); pool[i].playOnAwake = false; }
    }

    static AudioClip Get(string name)
    {
        if (clips.TryGetValue(name, out var c)) return c;
        var recorded = SoundBank.Clip(name);   // the team's recorded sound wins over the one made in code
        if (recorded != null) { clips[name] = recorded; return recorded; }
        float[] d = name switch
        {
            "bark" => Bark(),
            "clunk" => Thump(0.18f, 90f, 0.9f),
            "creak" => Scrape(1.1f),
            "clang" => Clang(),
            "splash" => Splash(),
            "crash" => Crash(),
            "groan" => Groan(),
            "lose" => Thump(0.6f, 55f, 1f),
            "win" => Jingle(new[] { 0, 4, 7, 12 }),
            "clear" => Jingle(new[] { 7, 12 }),
            "ping" => Ping(),
            "click" => Thump(0.06f, 400f, 0.5f),
            "morlam" => MorLam(),
            "rumble" => Rumble(),
            "sniff" => Sniff(),
            "zombienear" => Groan(),
            "step" => Step(0.09f, 0.10f),
            "paw" => Step(0.05f, 0.22f),
            "shield" => Shield(),
            "dig" => Scrape(0.5f),
            _ => null
        };
        if (d == null) return null;
        c = AudioClip.Create(name, d.Length, 1, Rate, false);
        c.SetData(d, 0);
        clips[name] = c;
        return c;
    }

    // ---------------------------------------------------------------- synthesis helpers
    static System.Random rng = new System.Random(7);
    static float N() => (float)(rng.NextDouble() * 2.0 - 1.0);
    static float[] Buf(float sec) => new float[Mathf.CeilToInt(sec * Rate)];

    static void LowPass(float[] d, float k) { float y = 0f; for (int i = 0; i < d.Length; i++) { y += k * (d[i] - y); d[i] = y; } }

    static float[] Bark()   // ONE soft, low, muffled "wuf" (short: doesn't wind up the pet)
    {
        var d = Buf(0.2f);
        for (int b = 0; b < 1; b++)
        {
            int s = 0, n = (int)(0.13f * Rate);
            float ph = 0f;
            for (int i = 0; i < n && s + i < d.Length; i++)
            {
                float t = (float)i / n;
                float f = Mathf.Lerp(300f, 140f, t);
                ph += f / Rate;
                float env = Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 3f)) * (1f - t);
                d[s + i] += env * (0.55f * Mathf.Sign(Mathf.Sin(ph * 6.283f)) * 0.5f + 0.7f * N());
            }
        }
        LowPass(d, 0.18f); LowPass(d, 0.3f);
        return Norm(d, 0.55f);
    }

    static float[] Sniff()   // three quick snuffles: soft filtered noise puffs
    {
        var d = Buf(0.5f);
        for (int b = 0; b < 3; b++)
        {
            int s = (int)(b * 0.15f * Rate), n = (int)(0.08f * Rate);
            for (int i = 0; i < n && s + i < d.Length; i++) { float t = (float)i / n; d[s + i] += Mathf.Sin(Mathf.PI * t) * N(); }
        }
        LowPass(d, 0.15f);
        return Norm(d, 0.5f);
    }

    static float[] Step(float sec, float bright)   // soft footstep on dirt: a short muffled noise tap
    {
        var d = Buf(sec);
        for (int i = 0; i < d.Length; i++) { float t = (float)i / d.Length; d[i] = N() * Mathf.Exp(-t * 7f) * Mathf.Min(1f, t * 60f); }
        LowPass(d, bright); LowPass(d, bright * 1.5f);
        return Norm(d, 0.35f);
    }

    static float[] Shield()   // holy light: a soft airy swell + three quick bell taps going up (no held tone)
    {
        var d = Buf(0.9f);
        for (int i = 0; i < d.Length; i++) { float t = (float)i / d.Length; d[i] = 0.35f * N() * Mathf.Sin(Mathf.PI * t) * (1f - t); }
        LowPass(d, 0.12f);
        float[] notes = { 784f, 988f, 1175f };
        for (int n = 0; n < notes.Length; n++)
        {
            int s = (int)(n * 0.09f * Rate);
            for (int i = 0; s + i < d.Length; i++) { float t = (float)i / Rate; d[s + i] += 0.5f * Mathf.Sin(t * 6.283f * notes[n]) * Mathf.Exp(-t * 16f); }
        }
        return Norm(d, 0.4f);
    }

    static float[] Thump(float sec, float f, float noise)
    {
        var d = Buf(sec); float ph = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / d.Length;
            ph += f * (1f - 0.5f * t) / Rate;
            d[i] = Mathf.Exp(-t * 6f) * (Mathf.Sin(ph * 6.283f) + noise * N() * Mathf.Exp(-t * 20f));
        }
        LowPass(d, 0.5f);
        return Norm(d, 0.9f);
    }

    static float[] Scrape(float sec)   // rusty metal grinding: noise chopped at an uneven rate
    {
        var d = Buf(sec);
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float am = 0.5f + 0.5f * Mathf.Sin(t * 6.283f * (11f + 4f * Mathf.Sin(t * 2.3f)));
            d[i] = N() * am * am * (0.6f + 0.4f * Mathf.Sin(t * 1.7f));
        }
        LowPass(d, 0.15f);
        return Norm(d, 0.35f);
    }

    static float[] Clang()   // tin cans: inharmonic partials, quick decay, a few hits
    {
        var d = Buf(0.4f);
        float[] partials = { 330f, 742f, 1027f, 1444f };
        for (int h = 0; h < 2; h++)
        {
            int s = (int)(h * 0.11f * Rate);
            for (int i = 0; s + i < d.Length; i++)
            {
                float t = (float)i / Rate, v = 0f;
                for (int p = 0; p < partials.Length; p++) v += Mathf.Sin(t * 6.283f * partials[p] * (1f + h * 0.07f)) / (p + 1);
                d[s + i] += v * Mathf.Exp(-t * 18f) * 0.6f + N() * Mathf.Exp(-t * 60f) * 0.2f;
            }
        }
        LowPass(d, 0.4f);
        return Norm(d, 0.45f);
    }

    static float[] Splash()
    {
        var d = Buf(0.8f);
        for (int i = 0; i < d.Length; i++) { float t = (float)i / d.Length; d[i] = N() * Mathf.Exp(-t * 4f) * Mathf.Min(1f, t * 40f); }
        LowPass(d, 0.18f);
        return Norm(d, 0.8f);
    }

    static float[] Crash()
    {
        var d = Buf(1.0f);
        for (int i = 0; i < d.Length; i++) { float t = (float)i / d.Length; d[i] = N() * Mathf.Exp(-t * 3.5f) + 0.6f * Mathf.Sin(t * 6.283f * 45f) * Mathf.Exp(-t * 6f); }
        LowPass(d, 0.06f);
        return Norm(d, 0.6f);
    }

    static float[] Groan()   // zombie wind-up: breathy, wobbling noise (not a clean tone)
    {
        var d = Buf(0.7f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / d.Length;
            d[i] = N() * Mathf.Sin(Mathf.PI * t) * (0.6f + 0.4f * Mathf.Sin(t * 6.283f * 9f));
        }
        LowPass(d, 0.06f);
        return Norm(d, 0.5f);
    }

    static float[] Ping()   // night sound wave
    {
        var d = Buf(0.22f);
        for (int i = 0; i < d.Length; i++) { float t = (float)i / Rate; d[i] = Mathf.Sin(t * 6.283f * 660f) * Mathf.Exp(-t * 18f) + 0.3f * Mathf.Sin(t * 6.283f * 990f) * Mathf.Exp(-t * 26f); }
        return Norm(d, 0.3f);
    }

    static float Pluck(float t, float f) => (Mathf.Sin(t * 6.283f * f) + 0.5f * Mathf.Sin(t * 6.283f * f * 2f) + 0.25f * Mathf.Sin(t * 6.283f * f * 3.01f)) * Mathf.Exp(-t * 22f);

    static float[] Jingle(int[] semis)
    {
        var d = Buf(0.18f * semis.Length + 0.3f);
        for (int n = 0; n < semis.Length; n++)
        {
            int s = (int)(n * 0.16f * Rate);
            float f = 523f * Mathf.Pow(2f, semis[n] / 12f);
            for (int i = 0; s + i < d.Length; i++) d[s + i] += Pluck((float)i / Rate, f);
        }
        LowPass(d, 0.5f);
        return Norm(d, 0.4f);
    }

    // mor lam loop: plucked phin (short notes, pentatonic) over a drum pattern. Short plucks with gaps,
    // so the microphone never hears a "held voice" from it.
    static float[] MorLam()
    {
        float step = 0.17f; int steps = 32;
        var d = Buf(step * steps);
        int[] scale = { 0, 2, 4, 7, 9, 12, 14 };
        int[] melody = { 0, 2, 4, 2, 5, 4, 2, 0, 1, 2, 4, 5, 4, 2, 1, 0, 3, 4, 5, 4, 3, 2, 1, 2, 0, 2, 4, 5, 6, 5, 4, 2 };
        for (int k = 0; k < steps; k++)
        {
            int s = (int)(k * step * Rate);
            float f = 392f * Mathf.Pow(2f, scale[melody[k]] / 12f);
            for (int i = 0; i < (int)(0.15f * Rate) && s + i < d.Length; i++) d[s + i] += 0.5f * Pluck((float)i / Rate, f);
            bool kick = k % 4 == 0, snare = k % 4 == 2;
            for (int i = 0; i < (int)(0.12f * Rate) && s + i < d.Length; i++)
            {
                float t = (float)i / Rate;
                if (kick) d[s + i] += 0.8f * Mathf.Sin(t * 6.283f * (90f - 200f * t)) * Mathf.Exp(-t * 25f);
                if (snare) d[s + i] += 0.35f * N() * Mathf.Exp(-t * 35f);
                else d[s + i] += 0.04f * N() * Mathf.Exp(-t * 90f);   // ching
            }
        }
        LowPass(d, 0.45f);
        return Norm(d, 0.5f);
    }

    static float[] Rumble()   // many feet + groans
    {
        var d = Buf(2f);
        for (int i = 0; i < d.Length; i++) { float t = (float)i / Rate; d[i] = N() * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(t * 6.283f * 1.7f))); }
        LowPass(d, 0.03f);
        return Norm(d, 0.5f);
    }

    static float[] Norm(float[] d, float peak)
    {
        float m = 1e-6f; foreach (var v in d) m = Mathf.Max(m, Mathf.Abs(v));
        float k = peak / m; for (int i = 0; i < d.Length; i++) d[i] *= k;
        return d;
    }
}
