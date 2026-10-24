using System.Collections.Generic;
using UnityEngine;

// Anything the dog's bark can switch on/off (gates, radio, shrine...).
// The bark always hits the ONE nearest switchable within its radius; DogBark shows a marker on it.
public class Switchable : MonoBehaviour
{
    public static readonly List<Switchable> All = new List<Switchable>();

    [Tooltip("The dog must be within this distance to switch it")]
    public float radius = 2.5f;
    public bool isOn;
    [Tooltip("Height of the 'bark will hit this' marker above the object's base")]
    public float markerHeight = 3.6f;

    // the bark never reaches further than this, whatever 'radius' says (zombies still HEAR it from far away -
    // this is only how close Bak Daeng must be to work a gate / lever / radio...)
    public static float BarkReach = 1.6f;
    public virtual float Reach => Mathf.Min(radius, BarkReach);

    public Vector3 MarkerPoint => transform.position + Vector3.up * markerHeight;

    protected virtual void OnEnable() { All.Add(this); }
    protected virtual void OnDisable() { All.Remove(this); }

    public virtual void Toggle() { isOn = !isOn; }

    public static Switchable Nearest(Vector3 from)
    {
        Switchable best = null;
        float bestD = float.MaxValue;
        foreach (var s in All)
        {
            float d = Mathf.Abs(s.transform.position.x - from.x);
            if (d <= s.Reach && d < bestD) { best = s; bestD = d; }
        }
        return best;
    }
}
