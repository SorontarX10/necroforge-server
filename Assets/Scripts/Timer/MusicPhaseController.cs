using UnityEngine;
using System.Collections;

public class MusicPhaseController : MonoBehaviour
{
    [Header("Audio Sources")]
    public AudioSource musicA;
    public AudioSource musicB;
    public AudioSource musicC;

    [Header("Fade Settings")]
    public float fadeDuration = 2.5f;

    private Coroutine fadeRoutine;
    private AudioSource current;

    void Start()
    {
        // preload – wszystkie grają
        InitSource(musicA, 1f);
        InitSource(musicB, 0f);
        InitSource(musicC, 0f);

        current = musicA;

        GameTimerController.Instance.OnPhaseChanged += OnPhaseChanged;
    }

    void InitSource(AudioSource src, float phaseWeight)
    {
        src.Play();
        SetPhaseVolume(src, phaseWeight);
    }

    void OnPhaseChanged(GameTimerController.TimerPhase phase)
    {
        switch (phase)
        {
            case GameTimerController.TimerPhase.PhaseB:
                StartCrossfade(musicB);
                break;

            case GameTimerController.TimerPhase.PhaseC:
                StartCrossfade(musicC);
                break;
        }
    }

    void StartCrossfade(AudioSource next)
    {
        if (current == next)
            return;

        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        fadeRoutine = StartCoroutine(Crossfade(current, next));
        current = next;
    }

    IEnumerator Crossfade(AudioSource from, AudioSource to)
    {
        float t = 0f;

        float fromStart = GetPhaseVolume(from);
        float toStart   = GetPhaseVolume(to);

        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = t / fadeDuration;

            SetPhaseVolume(from, Mathf.Lerp(fromStart, 0f, k));
            SetPhaseVolume(to,   Mathf.Lerp(toStart,   1f, k));

            yield return null;
        }

        SetPhaseVolume(from, 0f);
        SetPhaseVolume(to,   1f);
    }

    // 🔽 KLUCZOWE METODY
    void SetPhaseVolume(AudioSource src, float phase)
    {
        src.volume =
            phase *
            GameSettings.MusicVolume *
            GameSettings.MasterVolume;
    }

    float GetPhaseVolume(AudioSource src)
    {
        float global = GameSettings.MusicVolume * GameSettings.MasterVolume;
        return global > 0f ? src.volume / global : 0f;
    }

    // 🔔 wołane przy zmianie opcji
    public void RefreshVolumes()
    {
        SetPhaseVolume(musicA, musicA == current ? 1f : 0f);
        SetPhaseVolume(musicB, musicB == current ? 1f : 0f);
        SetPhaseVolume(musicC, musicC == current ? 1f : 0f);
    }

    public void StopAllMusic()
    {
        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        if (musicA) musicA.Stop();
        if (musicB) musicB.Stop();
        if (musicC) musicC.Stop();
    }
}
