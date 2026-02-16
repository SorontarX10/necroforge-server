using UnityEngine;
using UnityEngine.EventSystems;

public class MenuButtonHover : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    public float hoverScale = 1.05f;
    public float speed = 10f;

    private Vector3 baseScale;

    private MenuButtonPulse pulse;

    void Awake()
    {
        baseScale = transform.localScale;
        pulse = GetComponent<MenuButtonPulse>();
    }

    void Update()
    {
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            baseScale,
            Time.deltaTime * speed
        );
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (pulse) pulse.SetHover(true);
        baseScale = Vector3.one * hoverScale;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (pulse) pulse.SetHover(false);
        baseScale = Vector3.one;
    }
}
