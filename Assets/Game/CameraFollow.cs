using UnityEngine;

// Side-scroll camera that follows the dog smoothly, looks a little ahead in the direction
// the dog faces, leans toward the blind tourist so both stay on screen, and stops at the level edges.
public class CameraFollow : MonoBehaviour
{
    public Transform dog;
    public Transform tourist;
    [Range(0f, 1f)] public float touristWeight = 0.3f;
    public float lookAhead = 1.5f;
    public float smoothTime = 0.35f;
    [Tooltip("Level edges in world X. The camera never shows beyond them.")]
    public float levelMinX = -12f, levelMaxX = 36f;

    Camera cam;
    float vel, ahead, aheadVel;

    void Start()
    {
        cam = GetComponent<Camera>();
        if (dog == null) { var d = FindFirstObjectByType<DogWalker>(); if (d != null) dog = d.transform; }
        if (tourist == null) { var t = FindFirstObjectByType<BlindFollower>(); if (t != null) tourist = t.transform; }
        var p = transform.position; p.x = Clamp(TargetX()); transform.position = p;
    }

    void LateUpdate()
    {
        if (dog == null) return;
        float facing = Mathf.Sign(dog.localScale.x);
        ahead = Mathf.SmoothDamp(ahead, facing * lookAhead, ref aheadVel, 0.6f);
        var p = transform.position;
        p.x = Mathf.SmoothDamp(p.x, Clamp(TargetX() + ahead), ref vel, smoothTime);
        transform.position = p;
    }

    float TargetX()
    {
        float x = dog.position.x;
        if (tourist != null) x = Mathf.Lerp(x, tourist.position.x, touristWeight);
        return x;
    }

    float Clamp(float x)
    {
        float half = cam != null ? cam.orthographicSize * cam.aspect : 8f;
        if (levelMaxX - levelMinX <= half * 2f) return (levelMinX + levelMaxX) / 2f;
        return Mathf.Clamp(x, levelMinX + half, levelMaxX - half);
    }
}
