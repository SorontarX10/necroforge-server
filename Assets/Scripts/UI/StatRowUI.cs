using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StatRowUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private TextMeshProUGUI valueText;

    [Header("Boost Colors")]
    [SerializeField] private Color normalColor = new Color(0.85f, 0.85f, 0.85f, 1f);
    [SerializeField] private Color boostedColor = new Color(0.25f, 1f, 0.35f, 1f);

    private bool boosted;
    private bool colorsInitialized;

    private void EnsureVisible()
    {
        if (labelText != null)
        {
            if (!labelText.gameObject.activeSelf)
                labelText.gameObject.SetActive(true);
            labelText.enabled = true;
        }

        if (valueText != null)
        {
            if (!valueText.gameObject.activeSelf)
                valueText.gameObject.SetActive(true);
            valueText.enabled = true;
        }

        if (!colorsInitialized)
        {
            var c = boosted ? boostedColor : normalColor;
            if (labelText != null) labelText.color = c;
            if (valueText != null) valueText.color = c;
            colorsInitialized = true;
        }
    }


    private bool TryAutoBind()
    {
        if (labelText != null && valueText != null)
            return true;

        var tmps = GetComponentsInChildren<TextMeshProUGUI>(true);
        if (tmps == null || tmps.Length == 0)
            return false;

        if (labelText == null)
        {
            foreach (var t in tmps)
            {
                if (t != null && t.gameObject.name.Contains("Label"))
                {
                    labelText = t;
                    break;
                }
            }
        }

        if (valueText == null)
        {
            foreach (var t in tmps)
            {
                if (t != null && t.gameObject.name.Contains("Value"))
                {
                    valueText = t;
                    break;
                }
            }
        }

        if (labelText == null)
            labelText = tmps.Length > 0 ? tmps[0] : null;

        if (valueText == null)
            valueText = tmps.Length > 1 ? tmps[1] : null;

        return valueText != null;
    }


    private void Awake()
    {
        TryAutoBind();
        EnsureVisible();
        // Runtime fallback in case serialized refs got lost.
        if (labelText == null || valueText == null)
        {
            var tmps = GetComponentsInChildren<TextMeshProUGUI>(true);
            if (tmps != null && tmps.Length >= 2)
            {
                labelText = tmps[0];
                valueText = tmps[1];
            }
        }
    }


    private void Reset()
    {
        // Spróbuj auto-podpiąć, jeśli to prefab row z 2 TMP
        var tmps = GetComponentsInChildren<TextMeshProUGUI>(true);
        if (tmps != null && tmps.Length >= 2)
        {
            labelText = tmps[0];
            valueText = tmps[1];
        }
    }

    public void SetBoosted(bool isBoosted)
    {
        TryAutoBind();
        EnsureVisible();
        if (boosted == isBoosted && colorsInitialized) return;
        boosted = isBoosted;
        colorsInitialized = true;

        var c = boosted ? boostedColor : normalColor;

        if (labelText != null) labelText.color = c;
        if (valueText != null) valueText.color = c;
    }

    public void SetInt(int v)
    {
        if (!TryAutoBind()) return;
        EnsureVisible();
        if (valueText != null)
            valueText.text = v.ToString();
    }

    public void SetFloat(float v, int decimals = 1)
    {
        if (!TryAutoBind()) return;
        EnsureVisible();
        if (valueText != null)
            valueText.text = v.ToString($"F{decimals}");
    }

    public void SetPercent(float v01)
    {
        if (!TryAutoBind()) return;
        EnsureVisible();
        v01 = Mathf.Clamp01(v01);
        if (valueText != null)
            valueText.text = (v01 * 100f).ToString("F1") + "%";
    }

    public void SetMultiplier(float mul)
    {
        if (!TryAutoBind()) return;
        EnsureVisible();
        if (valueText != null)
            valueText.text = mul.ToString("F2") + "x";
    }

    public void SetPerSecond(float v)
    {
        if (!TryAutoBind()) return;
        EnsureVisible();
        if (valueText != null)
            valueText.text = v.ToString("F1") + " /s";
    }

    public void SetCurrentMax(float current, float max)
    {
        if (!TryAutoBind()) return;
        EnsureVisible();
        if (valueText != null)
            valueText.text = $"{current:F0} / {max:F0}";
    }

    public string DebugValueText => valueText != null ? valueText.text : null;
    public string DebugLabelText => labelText != null ? labelText.text : null;
    public bool HasValueText => valueText != null;
}
