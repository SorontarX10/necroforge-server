using UnityEngine;

public class MenuContainerFade : MonoBehaviour
{
    public CanvasGroup group;
    public float delay = 0.5f;
    public float fadeTime = 0.8f;

    void Awake()
    {
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    void Start()
    {
        StartCoroutine(Fade());
    }

    System.Collections.IEnumerator Fade()
    {
        yield return new WaitForSeconds(delay);

        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.deltaTime;
            group.alpha = Mathf.Lerp(0f, 1f, t / fadeTime);
            yield return null;
        }

        // 🔴 TO JEST KLUCZ
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;
    }
}
