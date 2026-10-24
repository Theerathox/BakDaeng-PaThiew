using UnityEngine;

// Background layer that scrolls slower than the world: 0 = moves with the world, 1 = stuck to the camera.
// Far layers (hills, moon) near 1, middle layers (palms, huts) about 0.5.
public class Parallax : MonoBehaviour
{
    [Range(0f, 1f)] public float factor = 0.5f;
    Transform cam;
    float startX, camStartX;

    void Start()
    {
        cam = Camera.main != null ? Camera.main.transform : null;
        startX = transform.position.x;
        camStartX = cam != null ? cam.position.x : 0f;
    }

    void LateUpdate()
    {
        if (cam == null) return;
        var p = transform.position;
        p.x = startX + (cam.position.x - camStartX) * factor;
        transform.position = p;
    }
}
