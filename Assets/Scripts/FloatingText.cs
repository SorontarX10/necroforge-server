using TMPro;
using UnityEngine;

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

        if (text == null)
        {
            Debug.LogWarning("[FloatingText] TMP_Text reference missing. Popup will be skipped.", this);
            Complete();
            return;
        }

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

        // Billboard always towards camera.
        if (cam != null)
        {
            transform.LookAt(
                transform.position + cam.transform.rotation * Vector3.forward,
                cam.transform.rotation * Vector3.up
            );
        }

        if (text == null)
        {
            Complete();
            return;
        }

        if (t >= fadeStart)
        {
            float fadeDuration = Mathf.Max(0.0001f, lifetime - fadeStart);
            float a = Mathf.Lerp(1f, 0f, (t - fadeStart) / fadeDuration);
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
