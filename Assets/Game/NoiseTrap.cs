using System.Collections.Generic;
using UnityEngine;

// Tin cans on a string (ปี๊บกระป๋อง). Anyone who walks through - dog, master or zombie - rattles
// them: a noise as loud as a bark (wakes sleepers, pulls zombies in, sends a wave at night).
// Not a bark target; you can't switch it off. Lure zombies away or lock them in before passing.
public class NoiseTrap : MonoBehaviour
{
    public float loudness = 1f;
    public float halfWidth = 0.6f;
    public Transform[] cans;

    readonly Dictionary<Transform, bool> inside = new Dictionary<Transform, bool>();
    Transform[] bodies;
    float cool, swing, hintAt;
    DogWalker walker;

    void Start()
    {
        var list = new List<Transform>();
        foreach (var d in FindObjectsByType<DogWalker>(FindObjectsSortMode.None)) list.Add(d.transform);
        foreach (var b in FindObjectsByType<BlindFollower>(FindObjectsSortMode.None)) list.Add(b.transform);
        foreach (var z in FindObjectsByType<ZombieChaser>(FindObjectsSortMode.None)) list.Add(z.transform);
        bodies = list.ToArray();
        walker = FindFirstObjectByType<DogWalker>();
        foreach (var b in bodies) inside[b] = Mathf.Abs(b.position.x - transform.position.x) < halfWidth;
    }

    void Update()
    {
        cool -= Time.deltaTime;
        float x = transform.position.x;
        bool sneaking = walker != null && walker.Crouching;   // dog crawling = dog AND master pass silently
        foreach (var b in bodies)
        {
            if (b == null || !b.gameObject.activeInHierarchy) continue;
            if (sneaking && (b == walker.transform || b.GetComponent<BlindFollower>() != null))
            {
                bool inNow = Mathf.Abs(b.position.x - transform.position.x) < halfWidth;
                if (inNow && !inside[b] && Time.time > hintAt) { hintAt = Time.time + 3f; GameState.Toast(Loc.T("Shh... crawling past the cans.")); }
                inside[b] = inNow; continue;
            }
            bool now = Mathf.Abs(b.position.x - x) < halfWidth;
            if (now && !inside[b] && cool <= 0f && !GameState.Cutscene)
            {
                cool = 1.2f; swing = 1f;
                Noise.Emit(transform.position, loudness);
                Sfx.Play("clang", Sfx.Near(transform.position));
                GameState.Toast(Loc.T("CLANG! The cans rattled..."));
            }
            inside[b] = now;
        }
        swing = Mathf.MoveTowards(swing, 0f, Time.deltaTime * 0.8f);
        if (cans != null)
            for (int i = 0; i < cans.Length; i++)
                if (cans[i] != null) cans[i].localRotation = Quaternion.Euler(0f, 0f, 35f * swing * Mathf.Sin(Time.time * 18f + i * 1.7f));
    }
}
