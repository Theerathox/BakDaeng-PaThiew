using UnityEngine;

// Shows what the NEXT bark will do, right on the thing it will hit (drawn above the night darkness):
//   a word over the target ("OPEN", "CLOSE", "PLAY", "FALL", "RAISE", ...)
//   palm   - an orange stripe where the trunk will land: zombies standing there get knocked into the water
//   lever  - lines to everything it is linked to, each saying what will happen to it
//   bridge - the deck is marked: raise it while a zombie is on it and the zombie falls
// Added to the dog by DogBark.
public class BarkHints : MonoBehaviour
{
    static readonly Color Amber = new Color(0.91f, 0.64f, 0.23f);
    static readonly Color Warn = new Color(1f, 0.45f, 0.25f);
    const int MaxLinks = 4;

    TextMesh word;
    LineRenderer zone;
    TextMesh zoneLabel;
    readonly LineRenderer[] links = new LineRenderer[MaxLinks];
    readonly TextMesh[] linkLabels = new TextMesh[MaxLinks];
    Material mat;

    public void Init(Material m)
    {
        mat = m;
        word = MakeText("BarkHint_Word", 0.055f, Amber);
        zone = MakeLine("BarkHint_Zone", 0.24f);
        zoneLabel = MakeText("BarkHint_ZoneLabel", 0.045f, Warn);
        for (int i = 0; i < MaxLinks; i++) { links[i] = MakeLine("BarkHint_Link" + i, 0.05f); linkLabels[i] = MakeText("BarkHint_LinkLabel" + i, 0.042f, Amber); }
        Hide();
    }

    TextMesh MakeText(string name, float size, Color c)
    {
        var t = new GameObject(name).AddComponent<TextMesh>();
        t.anchor = TextAnchor.MiddleCenter; t.alignment = TextAlignment.Center;
        t.characterSize = size; t.fontSize = 48; t.fontStyle = Loc.Bold; t.color = c;
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.font = font; var mr = t.GetComponent<MeshRenderer>(); mr.sharedMaterial = font.material; mr.sortingOrder = Darkness.DarkOrder + 72;
        // dark copy just behind it, so the words read on the light morning sky too
        var sh = new GameObject("shadow").AddComponent<TextMesh>();
        sh.transform.SetParent(t.transform, false); sh.transform.localPosition = new Vector3(0.04f, -0.04f, 0f);
        sh.anchor = t.anchor; sh.alignment = t.alignment; sh.characterSize = size; sh.fontSize = 48; sh.fontStyle = Loc.Bold;
        sh.color = new Color(0.05f, 0.05f, 0.1f, 0.85f); sh.font = font;
        var smr = sh.GetComponent<MeshRenderer>(); smr.sharedMaterial = font.material; smr.sortingOrder = Darkness.DarkOrder + 71;
        return t;
    }

    LineRenderer MakeLine(string name, float width)
    {
        var lr = new GameObject(name).AddComponent<LineRenderer>();
        lr.material = mat; lr.widthMultiplier = width; lr.positionCount = 2; lr.useWorldSpace = true;
        lr.numCapVertices = 2; lr.sortingOrder = Darkness.DarkOrder + 65;
        return lr;
    }

    void Hide()
    {
        if (word == null) return;
        word.gameObject.SetActive(false); zone.gameObject.SetActive(false); zoneLabel.gameObject.SetActive(false);
        for (int i = 0; i < MaxLinks; i++) { links[i].gameObject.SetActive(false); linkLabels[i].gameObject.SetActive(false); }
    }

    // what barking at s does right now
    static string Action(Switchable s)
    {
        switch (s)
        {
            case PalmBridge _: return "FALL";
            case Drawbridge d: return d.isOn ? "RAISE" : "LOWER";
            case Gate g: return g.isOn ? "CLOSE" : "OPEN";
            case Radio r: return r.isOn ? "STOP" : "PLAY";
            case Lever _: return "FLIP";
            case Shrine sh: return sh.placeName == "shrine" ? "ASK FOR HELP" : "GO IN";
            default: return "";
        }
    }

    public void Show(Switchable s)
    {
        if (word == null) return;
        Hide();
        if (s == null || GameState.IsOver) return;
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 6f);

        word.gameObject.SetActive(true);
        SetText(word, Action(s));
        word.transform.position = s.MarkerPoint + Vector3.up * 0.75f;
        var wc = Amber; wc.a = pulse; word.color = wc;

        if (s is PalmBridge pb && pb.tree != null && pb.radius > 0f)
        {
            float a = pb.tree.position.x, b = a + pb.direction * pb.length;
            Zone(a, b, pb.transform.position.y, "falls here - knocks zombies into the water", pulse);
        }
        else if (s is Drawbridge db && db.canal != null)
            Zone(db.canal.Left, db.canal.Right, db.canal.transform.position.y, db.isOn ? "zombies on the bridge fall in" : "bridge comes down here", pulse);
        else if (s is Lever lv && lv.targets != null)
        {
            Vector3 from = lv.transform.position + Vector3.up * 1.5f;
            for (int i = 0; i < lv.targets.Length && i < MaxLinks; i++)
            {
                var t = lv.targets[i];
                if (t == null) continue;
                Vector3 to = t.transform.position + Vector3.up * Mathf.Max(1.2f, t.markerHeight * 0.75f);
                var lr = links[i]; lr.gameObject.SetActive(true);
                lr.SetPosition(0, from); lr.SetPosition(1, to);
                var c = Amber; c.a = 0.55f * pulse; lr.startColor = c; c.a = 0.85f * pulse; lr.endColor = c;
                var lab = linkLabels[i]; lab.gameObject.SetActive(true);
                SetText(lab, "will " + Action(t));
                lab.transform.position = to + Vector3.up * 0.45f;
            }
        }
    }

    void Zone(float a, float b, float groundY, string text, float pulse)
    {
        zone.gameObject.SetActive(true);
        zone.SetPosition(0, new Vector3(a, groundY + 0.12f, 0f));
        zone.SetPosition(1, new Vector3(b, groundY + 0.12f, 0f));
        var c = Warn; c.a = 0.5f * pulse; zone.startColor = zone.endColor = c;
        zoneLabel.gameObject.SetActive(true);
        SetText(zoneLabel, text);
        zoneLabel.transform.position = new Vector3((a + b) / 2f, groundY + 0.55f, 0f);
    }

    static void SetText(TextMesh t, string text)
    {
        text = Loc.T(text);
        if (t.text == text && t.font == Loc.WorldFont) return;
        t.text = text; Font(t);
        var sh = t.transform.Find("shadow");
        if (sh != null) { var s = sh.GetComponent<TextMesh>(); s.text = text; Font(s); }
    }

    static void Font(TextMesh t)
    {
        if (t.font == Loc.WorldFont) return;
        t.font = Loc.WorldFont; t.GetComponent<MeshRenderer>().sharedMaterial = t.font.material;
    }

    void OnDestroy()
    {
        foreach (var o in new Object[] { word, zone, zoneLabel }) if (o != null) Destroy(((Component)o).gameObject);
        for (int i = 0; i < MaxLinks; i++) { if (links[i] != null) Destroy(links[i].gameObject); if (linkLabels[i] != null) Destroy(linkLabels[i].gameObject); }
    }
}
