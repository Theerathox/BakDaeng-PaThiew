using UnityEngine;

// Code-driven animation for a cutout (bone) character built from:
//   base (root) > head, arm_near, arm_far, leg_front, leg_back
// Controllers set Velocity (units/sec, signed) and ArmRaise / ArmStrike (0..1).
// Walk cycle phase follows the real distance travelled, so feet don't skate.
public class CutoutRig : MonoBehaviour
{
    [Header("Legs (degrees, relative to the drawn pose)")]
    public float legFrontStand = -17f;
    public float legBackStand = 17f;
    public float legSwing = 17f;
    [Tooltip("World units travelled per full step cycle")]
    public float strideLength = 1.4f;

    [Header("Upper body")]
    public float armNearSwing = 8f;
    public float armFarSwing = 4f;
    public float headBob = 1.5f;
    public float bodyBob = 0.03f;
    public float idleSway = 1.5f;
    public float idleSpeed = 1.2f;

    [Header("Attack pose (zombie)")]
    public float armNearRaise = 0f;
    public float armFarRaise = 0f;
    public float armNearStrike = 0f;
    public float armFarStrike = 0f;

    [HideInInspector] public float Velocity;
    [HideInInspector] public float ArmRaise;    // 0 = down, 1 = fully raised
    [HideInInspector] public float ArmStrike;   // 0..1 blends raised pose into the strike pose
    // extra poses, 0..1 (blend smoothly from the outside)
    [HideInInspector] public float Dance;       // mor lam dance: hips sway, knees bounce, hands circle (zombies at the radio)
    [HideInInspector] public float Sneak;       // bent forward, lower, small steps (master tiptoeing while the dog crawls)
    [HideInInspector] public float Fall;        // knocked down backwards (master caught)
    [HideInInspector] public float Cheer;       // both arms up, waving (cutscenes)
    [HideInInspector] public float Bow;         // bowing / praying (cutscenes)
    [HideInInspector] public float Afraid;      // zombie at the edge of holy light: leans away, shakes, arm up against the glare
    [Tooltip("Animate with unscaled time (keeps moving while the game is paused, e.g. the 'caught' fall)")]
    [HideInInspector] public bool unscaled;

    Transform body, head, armNear, armFar, legFront, legBack;
    float rBody, rHead, rArmNear, rArmFar, rLegFront, rLegBack;
    Vector3 bodyPos;
    float hipH = -1f;   // height of the hips above the feet (for falling down)
    float phase, walkW, t;

    void Awake()
    {
        body = transform.Find("base");
        head = Find("head"); armNear = Find("arm_near"); armFar = Find("arm_far");
        legFront = Find("leg_front"); legBack = Find("leg_back");
        bodyPos = body.localPosition;
        rHead = Z(head); rArmNear = Z(armNear); rArmFar = Z(armFar); rLegFront = Z(legFront); rLegBack = Z(legBack);
    }

    Transform Find(string n) => body != null ? body.Find(n) : null;
    static float Z(Transform t) => t != null ? t.localEulerAngles.z : 0f;
    static void SetZ(Transform t, float z) { if (t != null) t.localRotation = Quaternion.Euler(0f, 0f, z); }

    void Update()
    {
        float dt = unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
        t += dt;
        if (hipH < 0f)   // measure once: how far the feet are below the hips
        {
            float minY = body.position.y;
            foreach (var r in GetComponentsInChildren<SpriteRenderer>()) minY = Mathf.Min(minY, r.bounds.min.y);
            hipH = Mathf.Max(0.1f, (body.position.y - minY) / Mathf.Max(0.01f, Mathf.Abs(transform.lossyScale.y)));
        }
        float speed = Mathf.Abs(Velocity);
        walkW = Mathf.MoveTowards(walkW, speed > 0.02f ? 1f : 0f, dt * 5f);
        phase += speed / Mathf.Max(0.1f, strideLength) * 2f * Mathf.PI * dt;
        float s = Mathf.Sin(phase);
        float idle = Mathf.Sin(t * idleSpeed * 2f * Mathf.PI / 3f);

        // legs: standing = legs together, walking = swing around that
        SetZ(legFront, rLegFront + legFrontStand + legSwing * s * walkW);
        SetZ(legBack, rLegBack + legBackStand - legSwing * s * walkW);

        // arms: opposite to the legs, plus raise / strike pose
        float nearPose = Mathf.Lerp(armNearRaise, armNearStrike, ArmStrike) * Mathf.Max(ArmRaise, ArmStrike);
        float farPose = Mathf.Lerp(armFarRaise, armFarStrike, ArmStrike) * Mathf.Max(ArmRaise, ArmStrike);
        float swingK = 1f - Mathf.Max(ArmRaise, ArmStrike);
        SetZ(armNear, rArmNear + nearPose + (-armNearSwing * s * walkW + idleSway * idle) * swingK);
        SetZ(armFar, rArmFar + farPose + (armFarSwing * s * walkW - idleSway * 0.7f * idle) * swingK);

        SetZ(head, rHead + headBob * Mathf.Sin(phase * 2f) * walkW + idleSway * 0.6f * idle);
        body.localPosition = bodyPos + new Vector3(0f, bodyBob * Mathf.Abs(s) * walkW + 0.006f * idle, 0f);
        body.localRotation = Quaternion.identity;
        Extras();
    }

    // dance / sneak / fall / cheer / bow, layered on top of the walk pose
    void Extras()
    {
        float bodyZ = 0f; Vector3 off = Vector3.zero;
        if (Dance > 0.001f)   // mor lam: sway hips, bounce, hands circling out of step ("fon")
        {
            float b = t * 2f * Mathf.PI * 1.1f;
            bodyZ += 9f * Mathf.Sin(b) * Dance;
            off.y += 0.05f * Mathf.Abs(Mathf.Sin(b)) * Dance;
            off.x += 0.04f * Mathf.Sin(b) * Dance;
            Add(armNear, Dance * (95f + 35f * Mathf.Sin(b * 2f)));
            Add(armFar, Dance * (70f + 35f * Mathf.Sin(b * 2f + 2.4f)));
            Add(head, Dance * -12f * Mathf.Sin(b));
            Add(legFront, Dance * 8f * Mathf.Sin(b));
            Add(legBack, Dance * -8f * Mathf.Sin(b));
        }
        if (Sneak > 0.001f)   // tiptoe: lean forward, knees bent, arms in
        {
            bodyZ -= 14f * Sneak;
            off.y -= 0.06f * Sneak;
            Add(legFront, 10f * Sneak); Add(legBack, -6f * Sneak);
            Add(armNear, 20f * Sneak);
        }
        if (Bow > 0.001f) { bodyZ -= 35f * Bow; off.y -= 0.12f * Bow; Add(armNear, 60f * Bow); Add(armFar, 60f * Bow); Add(head, -15f * Bow); }
        if (Cheer > 0.001f)
        {
            float w = Mathf.Sin(t * 12f);
            Add(armNear, Cheer * (150f + 20f * w)); Add(armFar, Cheer * (140f - 20f * w));
            off.y += 0.04f * Mathf.Abs(Mathf.Sin(t * 6f)) * Cheer;
        }
        if (Afraid > 0.001f)
        {
            float sh = Mathf.Sin(t * 57f) + 0.6f * Mathf.Sin(t * 83f);   // fast, uneven shiver
            bodyZ += (9f + 3f * sh) * Afraid;                          // leans back, away from the light
            off.x += 0.03f * Mathf.Sin(t * 71f) * Afraid;
            off.y -= 0.03f * Afraid;
            Add(armNear, (110f + 8f * sh) * Afraid);                   // shields its eyes
            Add(armFar, (40f - 6f * sh) * Afraid);
            Add(head, (-14f + 5f * sh) * Afraid);
            Add(legFront, 4f * sh * Afraid);
        }
        if (Fall > 0.001f)   // backwards onto the ground, arms thrown up, legs straight
        {
            float k = Fall * Fall * (3f - 2f * Fall);
            bodyZ += 84f * k;
            off.y -= hipH * 0.8f * k;
            off.x -= hipH * 0.25f * k;
            Add(armNear, 120f * k); Add(armFar, 100f * k);
            Add(legFront, -10f * k); Add(legBack, 12f * k);
            Add(head, 15f * k);
        }
        body.localRotation = Quaternion.Euler(0f, 0f, bodyZ);
        body.localPosition += off;
    }

    static void Add(Transform tr, float deg) { if (tr != null) tr.localRotation *= Quaternion.Euler(0f, 0f, deg); }

    public void Face(float dir)
    {
        if (Mathf.Abs(dir) < 0.01f) return;
        var sc = transform.localScale;
        sc.x = Mathf.Abs(sc.x) * Mathf.Sign(dir);
        transform.localScale = sc;
    }
}
