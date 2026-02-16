using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class UIElementIntroLayoutSafe : MonoBehaviour
{
    public CanvasGroup group;
    public float delay = 0f;
    public float fadeTime = 0.6f;
    public Vector2 startOffset = new Vector2(0, -50);

    RectTransform rect;
    Vector2 targetAnchoredPos;

    IEnumerator Start()
    {
        rect = GetComponent<RectTransform>();
        if (!group) group = GetComponent<CanvasGroup>();

        // 🔴 KLUCZ: czekamy aż Layout Group ustali pozycję
        yield return new WaitForEndOfFrame();
        LayoutRebuilder.ForceRebuildLayoutImmediate(
            rect.parent as RectTransform
        );

        // zapamiętujemy POZYCJĘ DOCELOWĄ (taką jak na Twoich screenach)
        targetAnchoredPos = rect.anchoredPosition;

        // ustawiamy stan startowy
        rect.anchoredPosition = targetAnchoredPos + startOffset;
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / fadeTime);

            group.alpha = k;
            rect.anchoredPosition = Vector2.Lerp(
                targetAnchoredPos + startOffset,
                targetAnchoredPos,
                k
            );

            yield return null;
        }

        rect.anchoredPosition = targetAnchoredPos;
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;
    }
}
