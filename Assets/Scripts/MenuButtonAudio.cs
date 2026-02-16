using UnityEngine;
using UnityEngine.EventSystems;

public class MenuButtonAudio : MonoBehaviour,
    IPointerEnterHandler,
    IPointerClickHandler
{
    public AudioSource audioSource;
    public AudioClip hoverClip;
    public AudioClip clickClip;

    [Header("Random Pitch")]
    public Vector2 hoverPitchRange = new Vector2(0.98f, 1.02f);
    public Vector2 clickPitchRange = new Vector2(0.95f, 1.00f);

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!audioSource || !hoverClip) return;

        audioSource.pitch = Random.Range(
            hoverPitchRange.x,
            hoverPitchRange.y
        );

        audioSource.PlayOneShot(hoverClip, 0.45f);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!audioSource || !clickClip) return;

        audioSource.pitch = Random.Range(
            clickPitchRange.x,
            clickPitchRange.y
        );

        audioSource.PlayOneShot(clickClip, 0.75f);
    }
}
