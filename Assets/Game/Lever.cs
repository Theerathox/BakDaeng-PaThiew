using UnityEngine;

// Lever (คันโยก). Bark at it to flip every linked target at once - a gate far away, a drawbridge,
// a radio... Lets a level put the switch somewhere other than the thing it controls.
public class Lever : Switchable
{
    public Switchable[] targets;
    public Transform handle;

    float angle;

    public override void Toggle()
    {
        base.Toggle();
        Sfx.Play("click", Sfx.Near(transform.position));
        if (targets != null)
            foreach (var t in targets) if (t != null) t.Toggle();
    }

    void Start() { angle = isOn ? -35f : 35f; }

    void Update()
    {
        angle = Mathf.MoveTowards(angle, isOn ? -35f : 35f, Time.deltaTime * 300f);
        if (handle != null) handle.localRotation = Quaternion.Euler(0f, 0f, angle);
    }
}
