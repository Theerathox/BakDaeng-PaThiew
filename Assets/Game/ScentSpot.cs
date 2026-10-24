using System.Collections.Generic;
using UnityEngine;

// Something buried (กลิ่นของที่ฝังไว้): only the dog's NOSE finds it - sniff close by (DogSniff)
// and Bak Daeng digs it up, which toggles its targets (open a lever gate, lower a bridge, start a radio...).
// Barking at it does nothing. At night it is hidden in the dark until you sniff nearby.
public class ScentSpot : MonoBehaviour
{
    public static readonly List<ScentSpot> All = new List<ScentSpot>();

    public Switchable[] targets;
    [TextArea] public string foundMessage = "Bak Daeng dug up a key - a gate opened!";
    public SpriteRenderer mound;
    public SpriteRenderer item;

    bool found; float pop, glint;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Start() { if (item != null) item.enabled = false; }

    public static ScentSpot Nearest(float x, float range)
    {
        ScentSpot best = null; float bd = range;
        foreach (var s in All)
        {
            if (s.found) continue;
            float d = Mathf.Abs(s.transform.position.x - x);
            if (d <= bd) { best = s; bd = d; }
        }
        return best;
    }

    public void Glint() { glint = 1.5f; }

    public void Dig()
    {
        if (found) return;
        found = true; pop = 0f;
        Sfx.Play("dig", 0.8f);
        Sfx.Play("clear", 0.6f);
        GameState.Toast(foundMessage);
        if (item != null) item.enabled = true;
        if (targets != null) foreach (var t in targets) if (t != null) t.Toggle();
    }

    void Update()
    {
        if (found && item != null && pop < 1f)   // the item pops out of the ground
        {
            pop = Mathf.MoveTowards(pop, 1f, Time.deltaTime * 2f);
            item.transform.localPosition = new Vector3(0f, 0.2f + 2.1f * Mathf.Sin(pop * Mathf.PI * 0.5f), 0f);   // up above the dog's head
        }
        if (mound != null)
        {
            if (found) mound.transform.localScale = Vector3.Lerp(mound.transform.localScale, new Vector3(mound.transform.localScale.x, 0.05f, 1f), Time.deltaTime * 4f);
            glint = Mathf.Max(0f, glint - Time.deltaTime);
            var c = mound.color; float k = glint > 0f ? 0.5f + 0.5f * Mathf.Sin(Time.time * 12f) : 0f;
            mound.color = Color.Lerp(new Color(0.42f, 0.32f, 0.22f), new Color(0.78f, 1f, 0.37f), k * 0.6f);
        }
    }
}
