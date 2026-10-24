using UnityEngine;

// The taxi at the end of the escape (level 11). When the master reaches it the game is won
// (no closed gate / canal in between), they jump in -> ending cutscene.
public class Taxi : MonoBehaviour
{
    [TextArea] public string message = "TAXI! You jump in and slam the door. Go, go, go!";
    Transform dog, master;
    bool warned;

    void Start()
    {
        var d = FindFirstObjectByType<DogWalker>(); if (d != null) dog = d.transform;
        var m = FindFirstObjectByType<BlindFollower>(); if (m != null) master = m.transform;
    }

    void Update()
    {
        if (GameState.IsOver || GameState.Cutscene || !GameState.Started) return;
        // the MASTER reaching the taxi ends the game
        var who = master != null ? master : dog;
        if (who != null && who.position.x >= transform.position.x - 1.5f) { Sfx.Play("clunk", 1f, 0.8f); GameState.ReachGoal(message); }
    }
}
