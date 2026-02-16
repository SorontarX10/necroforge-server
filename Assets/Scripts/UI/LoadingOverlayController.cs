using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class LoadingOverlayController : MonoBehaviour
{
    [Header("Fade")]
    [SerializeField] private float fadeDuration = 1.5f;

    private Image overlayImage;
    private bool fadedOut;

    private void Awake()
    {
        overlayImage = GetComponent<Image>();
        overlayImage.color = new Color(0, 0, 0, 1);
    }

    private void Update()
    {
        if (fadedOut)
            return;

        if (IsPlayerReady())
        {
            fadedOut = true;
            StartCoroutine(FadeOut());
        }
    }

    private bool IsPlayerReady()
    {
        // NAJPROSTSZY I NAJSTABILNIEJSZY WARUNEK
        return GameObject.FindGameObjectWithTag("Player") != null;
    }

    private IEnumerator FadeOut()
    {
        float t = 0f;
        Color c = overlayImage.color;

        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(1f, 0f, t / fadeDuration);
            overlayImage.color = new Color(c.r, c.g, c.b, a);
            yield return null;
        }

        overlayImage.color = new Color(c.r, c.g, c.b, 0f);
        gameObject.SetActive(false);
    }
}
