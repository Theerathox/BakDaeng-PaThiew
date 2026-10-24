using UnityEngine;

// Expressive poses for the dog, layered on top of the Animator's walk cycle (LateUpdate):
//   Bark()  - head snaps up, body rocks back, tail flicks up
//   Sniff() - nose down to the ground, twitching, little scent puffs, tail wagging
//   crawling (DogWalker) - head and tail low;  running - leaning forward, tail straight back
//   Happy (0..1, cutscenes) - tail wags, little hops
// Added automatically by DogBark / DogSniff / the cutscenes.
public class DogPose : MonoBehaviour
{
    [HideInInspector] public float Happy;

    Transform body, head, tail;
    Transform[] legs; bool[] front; float legLen = 0.9f;
    DogWalker walker;
    float barkT, sniffT, puffT, crawlK, runK, t;
    static Sprite disc;
    const float BarkTime = 0.45f, SniffTime = 1.1f;

    public static DogPose On(GameObject dog) { var p = dog.GetComponent<DogPose>(); return p != null ? p : dog.AddComponent<DogPose>(); }

    void Awake()
    {
        body = transform.Find("body");
        head = body != null ? body.Find("head") : null;
        tail = body != null ? body.Find("tail") : null;
        walker = GetComponent<DogWalker>();
        if (body == null) return;
        var ls = new System.Collections.Generic.List<Transform>();
        foreach (Transform c in body) if (c.name.StartsWith("leg_")) ls.Add(c);
        legs = ls.ToArray(); front = new bool[legs.Length]; legL = new Last[legs.Length];
        float sum = 0f; int n = 0;
        for (int i = 0; i < legs.Length; i++)
        {
            front[i] = legs[i].name.Contains("front");
            var r = legs[i].GetComponent<SpriteRenderer>();
            if (r != null) { sum += legs[i].position.y - r.bounds.min.y; n++; }
        }
        if (n > 0) legLen = sum / n;   // hip to paw
    }

    public void Bark() { barkT = BarkTime; }
    public void Sniff() { sniffT = SniffTime; puffT = 0f; }

    void LateUpdate()
    {
        if (body == null) return;
        float dt = Time.deltaTime; t += dt;
        float bodyZ = 0f, headZ = 0f, tailZ = 0f; Vector3 off = Vector3.zero;

        crawlK = Mathf.MoveTowards(crawlK, walker != null && walker.Crouching ? 1f : 0f, dt * 5f);
        runK = Mathf.MoveTowards(runK, walker != null && walker.Running ? 1f : 0f, dt * 5f);
        // crawl / stalk: legs splay out and bend (shorter), the body sinks until the paws touch the ground again,
        // nose stretched forward and low, tail flat
        float splay = 46f * crawlK, bend = 1f - 0.3f * crawlK;
        off.y -= legLen * (1f - bend * Mathf.Cos(splay * Mathf.Deg2Rad));
        headZ -= 14f * crawlK; tailZ -= 30f * crawlK; bodyZ -= 2f * crawlK;
        Vector3 headOff = new Vector3(0.16f, -0.16f, 0f) * crawlK;
        bodyZ -= 5f * runK; tailZ -= 15f * runK; headZ -= 4f * runK;

        if (barkT > 0f)
        {
            barkT -= dt;
            float k = Mathf.Sin(Mathf.PI * (1f - Mathf.Clamp01(barkT / BarkTime)));
            headZ += 30f * k; bodyZ += 8f * k; tailZ += 35f * k; off.x -= 0.06f * k; off.y += 0.04f * k;
        }
        if (sniffT > 0f)
        {
            sniffT -= dt;
            float k = Mathf.Min(1f, (SniffTime - sniffT) * 6f, sniffT * 6f);
            headZ -= (36f + 5f * Mathf.Sin(t * 40f)) * k;
            bodyZ -= 6f * k; off.y -= 0.05f * k;
            tailZ += 22f * Mathf.Sin(t * 22f) * k;
            puffT -= dt;
            if (puffT <= 0f && head != null && k > 0.5f) { puffT = 0.22f; Puff(); }
        }
        if (Happy > 0.001f)
        {
            tailZ += 30f * Mathf.Sin(t * 20f) * Happy;
            off.y += 0.06f * Mathf.Abs(Mathf.Sin(t * 7f)) * Happy;
            headZ += 8f * Happy;
        }

        Layer(ref bodyL, body, bodyZ, off);
        Layer(ref headL, head, headZ, headOff);
        if (legs != null)
            for (int i = 0; i < legs.Length; i++)
            {
                Layer(ref legL[i], legs[i], front[i] ? splay : -splay, Vector3.zero);
                var sc = legs[i].localScale; sc.y = bend; legs[i].localScale = sc;
            }
        Layer(ref tailL, tail, tailZ, Vector3.zero);
    }

    // put an offset on top of the bone's pose without it piling up frame after frame: if the Animator
    // didn't write the bone this frame, start again from the pose we started from last frame
    struct Last { public bool set; public Quaternion rot, baseRot; public Vector3 pos, basePos; }
    Last bodyL, headL, tailL; Last[] legL;

    static void Layer(ref Last l, Transform b, float z, Vector3 off)
    {
        if (b == null) return;
        Quaternion baseRot = l.set && Quaternion.Angle(b.localRotation, l.rot) < 0.01f ? l.baseRot : b.localRotation;
        Vector3 basePos = l.set && (b.localPosition - l.pos).sqrMagnitude < 1e-10f ? l.basePos : b.localPosition;
        b.localRotation = baseRot * Quaternion.Euler(0f, 0f, z);
        b.localPosition = basePos + off;
        l.set = true; l.rot = b.localRotation; l.baseRot = baseRot; l.pos = b.localPosition; l.basePos = basePos;
    }

    // a little grey puff by the nose that drifts up and fades
    void Puff()
    {
        if (disc == null)
        {
            const int N = 32;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float r = new Vector2(x + 0.5f - N / 2f, y + 0.5f - N / 2f).magnitude / (N / 2f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - r) * 1.5f));
                }
            tex.Apply();
            disc = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        }
        float face = Mathf.Sign(transform.localScale.x);
        var sr = new GameObject("sniffPuff").AddComponent<SpriteRenderer>();
        sr.sprite = disc; sr.sortingOrder = Darkness.DarkOrder + 8;
        sr.color = new Color(0.85f, 0.85f, 0.8f, 0.6f);
        sr.transform.position = head.position + new Vector3(face * (0.55f + Random.Range(0f, 0.15f)), -0.35f, 0f);
        sr.transform.localScale = Vector3.one * 0.18f;
        sr.gameObject.AddComponent<PuffFx>();
    }
}

public class PuffFx : MonoBehaviour
{
    float t; SpriteRenderer sr;
    void Start() { sr = GetComponent<SpriteRenderer>(); }
    void Update()
    {
        t += Time.deltaTime;
        transform.position += new Vector3(0.1f, 0.5f, 0f) * Time.deltaTime;
        transform.localScale = Vector3.one * (0.18f + 0.25f * t);
        var c = sr.color; c.a = Mathf.Max(0f, 0.6f - t); sr.color = c;
        if (t > 0.6f) Destroy(gameObject);
    }
}
