using UnityEngine;
using UnityEngine.UI;

public class MenuFadeIn : MonoBehaviour
{
    public float fadeDuration = 1.5f;
    private Image image;

    void Awake()
    {
        image = GetComponent<Image>();
    }

    void Start()
    {
        StartCoroutine(FadeIn());
    }

    System.Collections.IEnumerator FadeIn()
    {
        float t = 0f;
        Color c = image.color;

        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            c.a = Mathf.Lerp(1f, 0f, t / fadeDuration);
            image.color = c;
            yield return null;
        }

        c.a = 0f;
        image.color = c;
        gameObject.SetActive(false);
    }
}
