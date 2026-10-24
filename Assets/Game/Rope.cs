using UnityEngine;

// Retractable leash between the dog's harness handle and the blind tourist's free hand
// (the hand without the cane). Its length changes all the time: it pays out as the dog walks
// away and reels back in as the tourist catches up, so the tourist is never yanked along.
// Only past 'hardMaxLength' (safety) does the dog pull the tourist.
[RequireComponent(typeof(LineRenderer))]
public class Rope : MonoBehaviour
{
    public Transform dog;
    public Transform tourist;
    [Tooltip("Harness handle, in the dog's 'body' bone space")]
    public Vector2 dogAnchor = new Vector2(0.025f, 0.823f);
    [Tooltip("Bone holding the leash (arm_near = free hand, arm_far = cane hand)")]
    public string handBone = "base/arm_near";
    [Tooltip("Hand position in that bone's space")]
    public Vector2 touristAnchor = new Vector2(-0.285f, -1.11f);

    [Header("Retractable length")]
    [Tooltip("Slack kept on top of the current distance")]
    public float slack = 0.4f;
    [Tooltip("How fast the leash pays out (should be faster than the dog)")]
    public float payOutSpeed = 3.5f;
    [Tooltip("How fast it reels back in")]
    public float reelInSpeed = 1.0f;
    [Tooltip("Gentle breathing of the length so it never looks static")]
    public float wobble = 0.15f;
    public float minLength = 0.8f;
    [Tooltip("Safety limit: only beyond this does the dog drag the tourist")]
    public float hardMaxLength = 6f;
    public float maxSag = 1.1f;
    public int segments = 20;

    public float CurrentLength { get; private set; }

    LineRenderer lr;
    Transform dogBody, hand;

    void Start()
    {
        lr = GetComponent<LineRenderer>();
        lr.positionCount = segments + 1;
        lr.useWorldSpace = true;
        if (dog != null) dogBody = dog.Find("body");
        if (tourist != null) hand = tourist.Find(handBone);
        if (dogBody != null && hand != null)
            CurrentLength = Vector2.Distance(dogBody.TransformPoint(dogAnchor), hand.TransformPoint(touristAnchor)) + slack;
    }

    void LateUpdate()
    {
        if (dogBody == null || hand == null) return;
        Vector3 a = dogBody.TransformPoint(dogAnchor);
        Vector3 b = hand.TransformPoint(touristAnchor);
        float d = Vector2.Distance(a, b);

        // length follows the distance: pays out quickly, reels in slowly, and breathes a little
        float target = Mathf.Max(minLength, d + slack + wobble * Mathf.Sin(Time.time * 1.3f));
        float rate = target > CurrentLength ? payOutSpeed : reelInSpeed;
        CurrentLength = Mathf.MoveTowards(CurrentLength, target, rate * Time.deltaTime);
        if (CurrentLength < d) CurrentLength = Mathf.Min(d, hardMaxLength);   // never shorter than the gap

        // safety only: beyond the hard limit the dog pulls the tourist (gates still block)
        if (d > hardMaxLength && !GameState.IsOver)
        {
            float dir = Mathf.Sign(a.x - b.x);
            var p = tourist.position;
            float nx = Gate.ClampMove(p.x, p.x + dir * (d - hardMaxLength), 0.5f);
            b.x += nx - p.x;
            p.x = nx;
            tourist.position = p;
            d = Vector2.Distance(a, b);
        }

        // sag of a hanging rope of length L over span d (parabola approximation)
        float extra = Mathf.Max(0f, CurrentLength - d);
        float s = Mathf.Min(maxSag, Mathf.Sqrt(3f * Mathf.Max(d, 0.1f) * extra / 8f));
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            Vector3 pos = Vector3.Lerp(a, b, t);
            pos.y -= s * 4f * t * (1f - t);
            pos.z = 0f;
            lr.SetPosition(i, pos);
        }
    }
}
