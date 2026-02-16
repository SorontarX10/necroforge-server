using UnityEngine;
using TMPro;

public class FloatingText : MonoBehaviour
{
    public TMP_Text text;

    [Header("Anim")]
    public float lifetime = 0.9f;
    public float floatSpeed = 1.4f;
    public float fadeStart = 0.4f;

    private float t;
    private Color baseColor;
    private Camera cam;
    private System.Action<FloatingText> onFinished;
    private bool completed;

    public void Init(
        Camera camera,
        string content,
        Color color,
        float fontSize,
        System.Action<FloatingText> onFinished = null
    )
    {
        cam = camera;
        this.onFinished = onFinished;
        completed = false;

        if (text == null)
            text = GetComponentInChildren<TMP_Text>(true);

        text.text = content;
        text.color = color;
        text.fontSize = fontSize;

        baseColor = color;
        t = 0f;
    }

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        t += dt;

        transform.position += Vector3.up * floatSpeed * dt;

        // ✅ BILLBOARD – ZAWSZE W STRONĘ KAMERY
        if (cam != null)
        {
            transform.LookAt(
                transform.position + cam.transform.rotation * Vector3.forward,
                cam.transform.rotation * Vector3.up
            );
        }

        // fade
        if (t >= fadeStart)
        {
            float a = Mathf.Lerp(1f, 0f, (t - fadeStart) / (lifetime - fadeStart));
            text.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
        }

        if (t >= lifetime)
            Complete();
    }

    private void OnDisable()
    {
        completed = false;
        onFinished = null;
    }

    private void Complete()
    {
        if (completed)
            return;

        completed = true;
        if (onFinished != null)
        {
            onFinished(this);
            return;
        }

        Destroy(gameObject);
    }
}
