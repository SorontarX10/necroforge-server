using UnityEngine;

public class MenuMusicFade : MonoBehaviour
{
    public AudioSource musicSource;
    public float targetVolume = 0.3f;
    public float fadeTime = 2f;

    void Start()
    {
        musicSource.volume = 0f;
        musicSource.Play();
        StartCoroutine(FadeIn());
    }

    System.Collections.IEnumerator FadeIn()
    {
        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.deltaTime;
            musicSource.volume = Mathf.Lerp(0f, targetVolume, t / fadeTime);
            yield return null;
        }
        musicSource.volume = targetVolume;
    }
}
