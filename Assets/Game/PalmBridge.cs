using UnityEngine;

// A leaning palm on the canal bank (ต้นตาลเอียง). Bark once: it falls across the canal and stays
// there as a bridge - for everyone, zombies too. One shot, can't be undone.
// Any zombie under it when it lands is knocked into the canal.
public class PalmBridge : Switchable
{
    public Canal canal;
    [Tooltip("Pivot at the foot of the trunk")]
    public Transform tree;
    public float length = 4.5f;
    public float fallTime = 0.9f;
    [Tooltip("+1 falls to the right, -1 to the left")]
    public float direction = 1f;

    bool fallen, landed;
    float t, lean;

    void Start() { if (tree != null) lean = tree.localEulerAngles.z > 180f ? tree.localEulerAngles.z - 360f : tree.localEulerAngles.z; }

    public override void Toggle()
    {
        if (fallen) return;
        fallen = true;
        radius = 0f;   // no longer a bark target
        GameState.Toast(Loc.T("Timber! The palm is falling..."));
        Sfx.Play("creak", 0.6f * Sfx.Near(transform.position), 1.4f);
    }

    void Update()
    {
        if (!fallen || landed || tree == null) return;
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / fallTime);
        tree.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(lean, -direction * 90f, k * k));   // speeds up as it falls
        if (k < 1f) return;
        landed = true;
        if (canal != null) canal.bridged = true;
        float a = tree.position.x, b = a + direction * length;
        foreach (var z in FindObjectsByType<ZombieChaser>(FindObjectsSortMode.None))
        {
            float x = z.transform.position.x;
            if (x > Mathf.Min(a, b) && x < Mathf.Max(a, b)) z.Fall();
        }
        Noise.Emit(tree.position, 0.8f);   // a palm crashing down is loud
        Sfx.Play("crash", Sfx.Near(tree.position));
    }
}
