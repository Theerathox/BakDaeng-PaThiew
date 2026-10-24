using UnityEngine;

// "Next street" sign at the right end of a level. When the MASTER gets there (gates and canals still
// stop him on the way), the level is done: the pair walks off the right
// edge and the screen scrolls on to the next level (no button, no invisible wall).
public class LevelExit : MonoBehaviour
{
    Transform dog, tourist;
    bool warned;

    void Start()
    {
        var d = FindFirstObjectByType<DogWalker>(); if (d != null) dog = d.transform;
        var b = FindFirstObjectByType<BlindFollower>(); if (b != null) tourist = b.transform;
    }

    void Update()
    {
        if (GameState.IsOver || GameState.Cutscene || !GameState.Started) return;
        // the level ends when the MASTER gets to the sign (no bark needed). He walks ~2.5 behind the dog,
        // so "near the sign" counts. Gates and canals still stop him, so the puzzles still matter.
        var who = tourist != null ? tourist : dog;
        if (who != null && who.position.x >= transform.position.x - 2f) GameState.LevelComplete(transform.position.x);
    }
}
