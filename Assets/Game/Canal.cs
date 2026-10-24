using System.Collections.Generic;
using UnityEngine;

// A canal (คลอง) across the road. Nobody can cross it - dog, master or zombie - unless something
// bridges it: a Drawbridge that is down, or a PalmBridge that has fallen across.
public class Canal : MonoBehaviour
{
    public static readonly List<Canal> All = new List<Canal>();

    public float width = 3f;
    [HideInInspector] public bool bridged;

    public float Left => transform.position.x - width / 2f;
    public float Right => transform.position.x + width / 2f;
    public bool Inside(float x) => x > Left && x < Right;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // stop 'margin' before the bank of any canal that is not bridged (used by Gate.ClampMove)
    public static float Clamp(float from, float to, float margin)
    {
        foreach (var c in All)
        {
            if (c.bridged) continue;
            if (from <= c.Left) to = Mathf.Min(to, Mathf.Max(from, c.Left - margin));
            else if (from >= c.Right) to = Mathf.Max(to, Mathf.Min(from, c.Right + margin));
        }
        return to;
    }

    // zombies standing over open water drop in
    public void DropZombies()
    {
        foreach (var z in FindObjectsByType<ZombieChaser>(FindObjectsSortMode.None))
            if (Inside(z.transform.position.x)) z.Fall();
    }
}
