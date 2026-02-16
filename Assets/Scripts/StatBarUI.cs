using UnityEngine;
using UnityEngine.UI;

public class StatBarUI : MonoBehaviour
{
    [Header("UI")]
    public Image fillImage;

    [Header("Smoothing")]
    public bool smooth = true;
    public float smoothSpeed = 8f;

    private float targetValue = 1f;

    private void Awake()
    {
        if (fillImage == null)
            Debug.LogError("[StatBarUI] Fill Image not assigned", this);
    }

    private void Update()
    {
        if (!smooth)
        {
            fillImage.fillAmount = targetValue;
            return;
        }

        fillImage.fillAmount = Mathf.Lerp(
            fillImage.fillAmount,
            targetValue,
            Time.deltaTime * smoothSpeed
        );
    }

    /// <summary>
    /// valueNormalized = 0..1
    /// </summary>
    public void SetValue(float valueNormalized)
    {
        targetValue = Mathf.Clamp01(valueNormalized);
    }
}
