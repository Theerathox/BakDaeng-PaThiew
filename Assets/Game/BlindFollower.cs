using System.Collections.Generic;
using UnityEngine;

// The blind tourist follows the guide dog's path with a delay (holding the harness),
// staying a little behind the dog. Characters never collide - they can pass through each other.
// Sharp ears: when the dog barks he hears it and hurries straight to the dog (no delay, faster than
// any zombie), stopping just short of it. He waits there until the dog moves on. Closed gates still stop him.
[RequireComponent(typeof(CutoutRig))]
public class BlindFollower : MonoBehaviour
{
    public Transform dog;
    [Tooltip("How many seconds behind the dog the tourist reacts")]
    public float delay = 0.8f;
    [Tooltip("Distance kept behind the dog (in the dog's facing direction)")]
    public float gap = 2.5f;
    [Tooltip("A bit faster than the dog so the tourist catches up on his own (no dragging)")]
    public float maxSpeed = 1.75f;
    [Tooltip("When the dog runs, he runs with it (holding the harness)")]
    public float runSpeed = 3.2f;
    public float minX = -7.5f, maxX = 7.5f;
    [Header("Hears the bark")]
    public float callSpeed = 3.5f;
    [Tooltip("Stops this far from the dog when called")]
    public float callStop = 1.2f;

    bool called, parked;
    float callX, parkedDogX;
    float curGap = -1f;   // after a call he is closer than 'gap'; eases back so he never steps away from the dog

    CutoutRig rig;
    DogWalker walker;
    readonly Queue<(float time, float x, float facing)> trail = new Queue<(float, float, float)>();
    (float time, float x, float facing) delayed;

    void Start()
    {
        rig = GetComponent<CutoutRig>();
        if (dog == null) { var d = FindFirstObjectByType<DogWalker>(); if (d != null) dog = d.transform; }
        if (dog != null) walker = dog.GetComponent<DogWalker>();
        delayed = (Time.time, dog != null ? dog.position.x : transform.position.x, 1f);
    }

    void OnEnable() { DogBark.OnNoise += Heard; }
    void OnDisable() { DogBark.OnNoise -= Heard; }

    void Heard(Vector3 at)
    {
        if (GameState.IsOver) return;
        float side = transform.position.x >= at.x ? 1f : -1f;   // come from the side he is on
        callX = Mathf.Clamp(at.x + side * callStop, minX, maxX);
        called = true; parked = false;
    }

    // forget the old path so the normal delayed follow restarts from "now"
    void ResetTrail(float facing) { trail.Clear(); delayed = (Time.time, dog.position.x, facing); }

    void Update()
    {
        if (dog == null) return;
        if (GameState.IsOver)
        {
            rig.Velocity = 0f; rig.Sneak = 0f;
            if (!GameState.Won) { rig.unscaled = true; rig.Fall = Mathf.MoveTowards(rig.Fall, 1f, Time.unscaledDeltaTime * 1.8f); }   // caught: falls down
            return;
        }
        rig.Sneak = Mathf.MoveTowards(rig.Sneak, walker != null && walker.Crouching ? 1f : 0f, Time.deltaTime * 4f);   // tiptoes while the dog crawls
        float facing = Mathf.Sign(dog.localScale.x);

        if (called)
        {
            float cdx = callX - transform.position.x;
            float cv = Mathf.Abs(cdx) < 0.05f ? 0f : Mathf.Sign(cdx) * Mathf.Min(callSpeed, Mathf.Abs(cdx) * 4f);
            float moved = Step(cv);
            if (Mathf.Abs(cdx) < 0.05f || (Mathf.Abs(cv) > 0.01f && Mathf.Abs(moved) < 1e-4f))   // arrived, or a closed gate
            {
                called = false; parked = true; parkedDogX = dog.position.x; ResetTrail(facing);
            }
            return;
        }
        if (parked)
        {
            curGap = Mathf.Clamp(Mathf.Abs(transform.position.x - dog.position.x), 0.8f, gap);
            if (Mathf.Abs(dog.position.x - parkedDogX) < 0.4f) { rig.Velocity = 0f; ResetTrail(facing); return; }
            parked = false;
        }

        trail.Enqueue((Time.time, dog.position.x, facing));
        while (trail.Count > 0 && trail.Peek().time <= Time.time - delay) delayed = trail.Dequeue();

        if (curGap < 0f) curGap = gap;
        curGap = Mathf.MoveTowards(curGap, gap, Time.deltaTime * 0.6f);
        float target = delayed.x - delayed.facing * curGap;
        if (!GameState.Cutscene) target = Mathf.Clamp(target, minX, maxX);   // walk-in / walk-out may leave the level
        float dx = target - transform.position.x;
        float top = walker != null && walker.Running ? runSpeed : maxSpeed;
        float v = Mathf.Abs(dx) < 0.05f ? 0f : Mathf.Sign(dx) * Mathf.Min(top, Mathf.Abs(dx) * 3f);
        Step(v);
    }

    // footsteps (tiptoes silently while the dog crawls)
    float stepT;
    void LateUpdate()
    {
        if (rig == null || GameState.IsOver) return;
        float v = Mathf.Abs(rig.Velocity);
        if (v < 0.3f || (walker != null && walker.Crouching)) { stepT = Mathf.Min(stepT, 0.12f); return; }
        stepT -= Time.deltaTime;
        if (stepT <= 0f)
        {
            bool fast = v > 2.4f;
            stepT = fast ? 0.27f : 0.44f;
            Sfx.Play("step", fast ? 0.5f : 0.35f, Random.Range(0.85f, 1.15f));
        }
    }

    // move with velocity v (closed gates block); returns the distance actually moved
    float Step(float v)
    {
        var p = transform.position;
        float nx = Gate.ClampMove(p.x, p.x + v * Time.deltaTime, 0.5f);
        float moved = nx - p.x;
        float rv = Time.deltaTime > 0f ? moved / Time.deltaTime : 0f;   // blocked -> stand still
        p.x = nx;
        transform.position = p;
        rig.Velocity = rv;
        if (Mathf.Abs(rv) > 0.05f) rig.Face(rv);
        return moved;
    }
}
