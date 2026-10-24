using UnityEngine;

// Drawbridge over a canal. Bark to lower / raise it. isOn = down (walkable).
// Raising it while a zombie is on it drops the zombie into the water - for good.
// It will not lift while the dog or the master is on it (waits until they step off).
public class Drawbridge : Switchable
{
    public Canal canal;
    [Tooltip("Pivot at the left bank; the deck rotates up around it")]
    public Transform deck;
    public float moveTime = 0.6f;

    float down;   // 0 raised .. 1 down
    Transform dog, master;

    void Start()
    {
        down = isOn ? 1f : 0f;
        var d = FindFirstObjectByType<DogWalker>(); if (d != null) dog = d.transform;
        var m = FindFirstObjectByType<BlindFollower>(); if (m != null) master = m.transform;
        Apply();
    }

    public override void Toggle()
    {
        base.Toggle();
        Sfx.Play("clunk", Sfx.Near(transform.position), 0.7f);
        Sfx.Play("clang", 0.4f * Sfx.Near(transform.position), 0.5f);   // chains
    }

    bool FriendsOnDeck() =>
        canal != null && ((dog != null && canal.Inside(dog.position.x)) || (master != null && canal.Inside(master.position.x)));

    void Update()
    {
        float target = isOn ? 1f : 0f;
        if (!isOn && down >= 1f && FriendsOnDeck()) target = 1f;   // wait for them to get off
        down = Mathf.MoveTowards(down, target, Time.deltaTime / Mathf.Max(0.05f, moveTime));
        if (canal != null)
        {
            bool was = canal.bridged;
            canal.bridged = down > 0.95f;
            if (was && !canal.bridged) canal.DropZombies();
        }
        Apply();
    }

    void Apply()
    {
        if (deck != null) deck.localRotation = Quaternion.Euler(0f, 0f, 80f * (1f - down));
    }
}
