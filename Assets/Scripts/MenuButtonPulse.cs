using UnityEngine;

public class MenuButtonPulse : MonoBehaviour
{
    [Header("Pulse Settings")]
    public float minScale = 1.0f;
    public float maxScale = 1.04f;
    public float speed = 1.2f;

    private Vector3 baseScale;
    private bool isHovered = false;

    void Awake()
    {
        baseScale = transform.localScale;
    }

    void Update()
    {
        if (isHovered) return;

        float t = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f;
        float scale = Mathf.Lerp(minScale, maxScale, t);

        transform.localScale = baseScale * scale;
    }

    // Te metody będą wołane przez hover script
    public void SetHover(bool hover)
    {
        isHovered = hover;
        if (!hover)
            transform.localScale = baseScale;
    }
}
