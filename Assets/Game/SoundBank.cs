using UnityEngine;

// The team's recorded sounds (Assets/SoundGame), collected in Resources/SoundBank.asset so the game can
// find them by name. Any slot left empty falls back to the sound made in code (Sfx).
//   Sfx.Play("bark") -> bark,  "sniff" -> sniff,  "lose" -> lose,  "zombienear" -> zombieNear, "step", "shield",
//   "zombiekill" -> zombieKill,  radio loop "morlam" -> radioMusic.  BackgroundMusic uses day / night.
[CreateAssetMenu(menuName = "Pet Game/Sound Bank")]
public class SoundBank : ScriptableObject
{
    [Header("Sound effects")]
    public AudioClip bark;
    public AudioClip sniff;
    public AudioClip lose;
    [Tooltip("A zombie (or the horde) comes close to your master")]
    public AudioClip zombieNear;
    [Tooltip("The zombie (or the horde) catches your master")]
    public AudioClip zombieKill;
    [Tooltip("Footsteps of your master (empty = made in code)")]
    public AudioClip footstep;
    [Tooltip("Spirit shield switching on (empty = made in code)")]
    public AudioClip shield;
    [Header("Music")]
    [Tooltip("What the mor lam radio plays (loops)")]
    public AudioClip radioMusic;
    [Tooltip("Background music of the morning levels, title and day cutscenes (loops)")]
    public AudioClip day;
    [Tooltip("Background music of the night levels and night cutscenes (loops)")]
    public AudioClip night;
    [Header("Loudness (0..1) - kept soft, a pet is next to the phone")]
    [Range(0f, 1f)] public float sfxVolume = 0.7f;
    [Range(0f, 1f)] public float musicVolume = 0.3f;

    static SoundBank inst; static bool looked;
    public static SoundBank I { get { if (!looked) { looked = true; inst = Resources.Load<SoundBank>("SoundBank"); } return inst; } }

    public static AudioClip Clip(string name)
    {
        var b = I;
        if (b == null) return null;
        switch (name)
        {
            case "bark": return b.bark;
            case "sniff": return b.sniff;
            case "lose": return b.lose;
            case "zombienear": return b.zombieNear;
            case "step": return b.footstep;
            case "shield": return b.shield;
            case "zombiekill": return b.zombieKill;
            case "morlam": return b.radioMusic;
            default: return null;
        }
    }
}
