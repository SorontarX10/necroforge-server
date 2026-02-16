using UnityEngine;
using UnityEngine.UI;

public class WorldHealthBar : MonoBehaviour
{
    [Header("UI References")]
    public Canvas healthCanvas;
    public Image backgroundImage;
    public Image fillImage;

    [Header("Colors")]
    public Color enemyColor = Color.red;
    public Color playerColor = Color.green;

    public Transform target; // teraz public
    private Camera cam;
    private float offsetY = 2f;

    public void Initialize(Transform targetTransform, bool isPlayer, Camera worldCamera)
    {
        target = targetTransform;
        cam = worldCamera;

        // Canvas
        if (healthCanvas != null)
        {
            healthCanvas.renderMode = RenderMode.WorldSpace;
            healthCanvas.worldCamera = cam;

            RectTransform cr = healthCanvas.GetComponent<RectTransform>();
            cr.sizeDelta = new Vector2(100f, 12f);
            cr.localScale = Vector3.one * 0.01f;
        }

        // kolor
        fillImage.color = isPlayer ? playerColor : enemyColor;

        // wysokość nad głową
        offsetY = CalculateOffsetY(targetTransform);

        // reset pozycji
        transform.position = cam.WorldToScreenPoint(target.position + Vector3.up * offsetY);
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        if (cam == null)
            cam = Camera.main;

        if (cam == null)
            return;

        Vector3 worldPos = target.position + Vector3.up * offsetY;
        Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

        // jeśli za kamerą — odwróć to
        if (screenPos.z < 0f)
        {
            screenPos *= -1f;
        }

        transform.position = screenPos;
    }

    public void SetHealthFraction(float fraction)
    {
        if (fillImage != null)
            fillImage.fillAmount = Mathf.Clamp01(fraction);
    }

    private float CalculateOffsetY(Transform t)
    {
        var cap = t.GetComponentInParent<CapsuleCollider>();
        if (cap != null) return cap.height + 0.2f;

        var cc = t.GetComponentInParent<CharacterController>();
        if (cc != null) return cc.height + 0.2f;

        var rend = t.GetComponentInChildren<Renderer>();
        if (rend != null) return rend.bounds.size.y + 0.2f;

        return 2f;
    }
}
