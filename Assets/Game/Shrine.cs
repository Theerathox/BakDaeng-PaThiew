using UnityEngine;

// Goal reached when your master gets next to it: the spirit house (ศาลเจ้าที่) or the shelter (ศาลา) -> chapter clear.
public class Shrine : Switchable
{
    // a shrine / shelter is a big place: you stand just past it so your master is next to it - a bit more reach
    public override float Reach => Mathf.Min(radius, 2.4f);

    [Tooltip("How close the tourist must stand to the shrine")]
    public float masterRange = 1.2f;
    [Tooltip("Shown in the hint when the master is not close yet (shrine, shelter...)")]
    public string placeName = "shrine";
    [TextArea] public string winMessage = "The guardian spirit hears you. Its light drives the zombies away!";
    public SpriteRenderer glow;

    Transform tourist;
    float glowK;

    void Start()
    {
        var b = FindFirstObjectByType<BlindFollower>();
        if (b != null) tourist = b.transform;
    }

    public override void Toggle()
    {
        if (GameState.IsOver) return;
        if (MasterNear()) Finish();
        else GameState.Toast(Loc.F("Bring your master to the {0} first, then bark", Loc.T(placeName)));
    }

    bool MasterNear() => tourist != null && Mathf.Abs(tourist.position.x - transform.position.x) <= masterRange;

    void Finish()
    {
        if (isOn || GameState.IsOver) return;
        isOn = true;
        GameState.ReachGoal(winMessage);   // -> next scene (story cutscene)
    }

    void Update()
    {
        // the chapter ends as soon as the MASTER reaches the shrine / shelter - no bark needed
        // (a bark here could wake the sleeping zombies)
        if (!isOn && GameState.Started && !GameState.Cutscene && !GameState.IsOver && MasterNear()) Finish();

        if (glow == null) return;
        // unscaled: keeps glowing while the win screen pauses the game
        glowK = Mathf.MoveTowards(glowK, isOn ? 1f : 0f, Time.unscaledDeltaTime * 0.8f);
        var c = glow.color;
        c.a = 0.08f + 0.04f * Mathf.Sin(Time.unscaledTime * 2f) + 0.55f * glowK;
        glow.color = c;
        glow.transform.localScale = Vector3.one * (2.2f + 1.2f * glowK);
    }
}
