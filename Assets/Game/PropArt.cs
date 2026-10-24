using UnityEngine;
using UnityEngine.SceneManagement;

// Painted art for the props (Resources/PropArt: radio, key, taxi). Every time a scene loads,
// the simple box drawings that the scene builder made are hidden and the picture is shown instead,
// so no scene has to be edited by hand. Delete a picture from Resources/PropArt to get the boxes back.
//   radio - the stereo bounces while the song plays (the music notes still float up)
//   key   - the buried key that pops out of the ground (ScentSpot item)
//   taxi  - the level 11 / ending taxi
public static class PropArt
{
    const int Order = -5;   // same layer as the old box drawings (under the night darkness)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded += (s, m) => ApplyAll();
        ApplyAll();
    }

    static Sprite Load(string name) => Resources.Load<Sprite>("PropArt/" + name);

    public static void ApplyAll()
    {
        var radio = Load("radio");
        if (radio != null)
            foreach (var r in Object.FindObjectsByType<Radio>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.GetComponent<PropArtAnim>() != null) continue;
                foreach (var sr in r.GetComponentsInChildren<SpriteRenderer>(true))
                    if (r.notes == null || System.Array.IndexOf(r.notes, sr) < 0) sr.enabled = false;
                r.speaker = null; r.lamp = null;
                var art = Add(r.transform, "art", radio, 1.35f, Order);
                r.markerHeight = 2.1f;
                r.gameObject.AddComponent<PropArtAnim>().Init(art, r);
            }

        // (the lever keeps its original drawing - the team liked it better)

        var key = Load("key");
        if (key != null)
            foreach (var s in Object.FindObjectsByType<ScentSpot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (s.item == null || s.item.sprite == key) continue;
                foreach (Transform c in s.item.transform) { var sr = c.GetComponent<SpriteRenderer>(); if (sr != null) sr.enabled = false; }   // old key stem
                s.item.sprite = key;
                s.item.color = Color.white;
                float w = key.bounds.size.x;
                s.item.transform.localScale = Vector3.one * (0.85f / Mathf.Max(0.01f, w));
                s.item.transform.localRotation = Quaternion.Euler(0f, 0f, 12f);
                // dark outline behind it, so the orange key reads in front of the orange dog
                var ol = new GameObject("outline").AddComponent<SpriteRenderer>();
                ol.transform.SetParent(s.item.transform, false);
                ol.transform.localScale = Vector3.one * 1.18f;
                ol.sprite = key; ol.color = new Color(0.05f, 0.07f, 0.15f, 0.9f);
                ol.sortingOrder = s.item.sortingOrder - 1;
                s.gameObject.AddComponent<KeyOutlineSync>().Init(s.item, ol);
            }

        var taxi = Load("taxi");
        if (taxi != null)
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name != "Taxi" || t.parent != null || t.Find("art") != null) continue;
                foreach (var sr in t.GetComponentsInChildren<SpriteRenderer>(true)) sr.enabled = false;
                foreach (var mr in t.GetComponentsInChildren<MeshRenderer>(true)) mr.enabled = false;   // the old TAXI sign text
                Add(t, "art", taxi, 1.55f, Order);
            }
    }

    // picture standing on the object's base, 'height' world units tall
    static SpriteRenderer Add(Transform parent, string name, Sprite sprite, float height, int order)
    {
        var sr = new GameObject(name).AddComponent<SpriteRenderer>();
        sr.transform.SetParent(parent, false);
        sr.sprite = sprite; sr.sortingOrder = order;
        float h = sprite.bounds.size.y;
        float k = height / Mathf.Max(0.01f, h);
        // parent may be scaled (flipped zombie-style objects are not, but be safe)
        var ps = parent.lossyScale;
        sr.transform.localScale = new Vector3(k / Mathf.Max(0.01f, Mathf.Abs(ps.x)), k / Mathf.Max(0.01f, Mathf.Abs(ps.y)), 1f);
        return sr;
    }
}

// little animations for the painted props
public class PropArtAnim : MonoBehaviour
{
    SpriteRenderer art; Radio radio; Lever lever;
    Vector3 baseScale; float flip = 1f;

    public void Init(SpriteRenderer a, Switchable owner)
    {
        art = a; baseScale = a.transform.localScale;
        radio = owner as Radio; lever = owner as Lever;
        if (lever != null) flip = lever.isOn ? 1f : -1f;
    }

    void Update()
    {
        if (art == null) return;
        if (radio != null)   // bounce to the beat while playing
        {
            float b = radio.isOn ? Mathf.Abs(Mathf.Sin(Time.time * 9f)) : 0f;
            art.transform.localScale = new Vector3(baseScale.x * (1f + 0.04f * b), baseScale.y * (1f + 0.08f * b), 1f);
        }
        else if (lever != null)   // the handle leans right when ON, left when OFF: flip the whole picture
        {
            flip = Mathf.MoveTowards(flip, lever.isOn ? 1f : -1f, Time.deltaTime * 9f);
            float sx = Mathf.Abs(flip) < 0.15f ? 0.15f * Mathf.Sign(flip == 0f ? 1f : flip) : flip;
            art.transform.localScale = new Vector3(baseScale.x * sx, baseScale.y, 1f);
        }
    }
}

// the key's outline shows / hides with the key
public class KeyOutlineSync : MonoBehaviour
{
    SpriteRenderer key, outline;
    public void Init(SpriteRenderer k, SpriteRenderer o) { key = k; outline = o; }
    void LateUpdate() { if (key != null && outline != null) outline.enabled = key.enabled; }
}
