using UnityEngine;
using System.Collections;
using GrassSim.Core;

public class MusicPhaseController : MonoBehaviour
{
    [Header("Audio Sources")]
    public AudioSource musicA;
    public AudioSource musicB;
    public AudioSource musicC;

    [Header("Fade Settings")]
    public float fadeDuration = 2.5f;

    [Header("Low Health Music Pitch")]
    [SerializeField] private bool enableLowHealthPitch = true;
    [SerializeField, Range(0.05f, 1f)] private float lowHealthPitchThreshold = 0.5f;
    [SerializeField, Range(0.5f, 1f)] private float lowHealthMinPitchMultiplier = 0.94f;
    [SerializeField, Min(0.1f)] private float lowHealthPitchBlendSpeed = 4.5f;
    [SerializeField, Min(0.05f)] private float playerResolveInterval = 0.25f;

    private Coroutine fadeRoutine;
    private AudioSource current;
    private float basePitchA = 1f;
    private float basePitchB = 1f;
    private float basePitchC = 1f;
    private float currentPitchMultiplier = 1f;
    private PlayerProgressionController player;
    private float nextPlayerResolveAt;

    void Start()
    {
        // Preload all tracks so crossfades are instant.
        basePitchA = ResolveBasePitch(musicA);
        basePitchB = ResolveBasePitch(musicB);
        basePitchC = ResolveBasePitch(musicC);

        InitSource(musicA, 1f);
        InitSource(musicB, 0f);
        InitSource(musicC, 0f);

        current = musicA;

        if (GameTimerController.Instance != null)
            GameTimerController.Instance.OnPhaseChanged += OnPhaseChanged;
    }

    void OnDestroy()
    {
        if (GameTimerController.Instance != null)
            GameTimerController.Instance.OnPhaseChanged -= OnPhaseChanged;
    }

    void Update()
    {
        UpdateLowHealthPitch();
    }

    void InitSource(AudioSource src, float phaseWeight)
    {
        if (src == null)
            return;

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
        float toStart = GetPhaseVolume(to);

        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = t / fadeDuration;

            SetPhaseVolume(from, Mathf.Lerp(fromStart, 0f, k));
            SetPhaseVolume(to, Mathf.Lerp(toStart, 1f, k));

            yield return null;
        }

        SetPhaseVolume(from, 0f);
        SetPhaseVolume(to, 1f);
    }

    void SetPhaseVolume(AudioSource src, float phase)
    {
        if (src == null)
            return;

        src.volume =
            phase *
            GameSettings.MusicVolume *
            GameSettings.MasterVolume;
    }

    float GetPhaseVolume(AudioSource src)
    {
        if (src == null)
            return 0f;

        float global = GameSettings.MusicVolume * GameSettings.MasterVolume;
        return global > 0f ? src.volume / global : 0f;
    }

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

        currentPitchMultiplier = 1f;
        ApplyPitchToSource(musicA, basePitchA, currentPitchMultiplier);
        ApplyPitchToSource(musicB, basePitchB, currentPitchMultiplier);
        ApplyPitchToSource(musicC, basePitchC, currentPitchMultiplier);
    }

    private void UpdateLowHealthPitch()
    {
        if (!enableLowHealthPitch)
            return;

        ResolvePlayer();

        float targetPitchMultiplier = GetLowHealthPitchMultiplier();
        currentPitchMultiplier = Mathf.MoveTowards(
            currentPitchMultiplier,
            targetPitchMultiplier,
            Time.unscaledDeltaTime * Mathf.Max(0.1f, lowHealthPitchBlendSpeed)
        );

        ApplyPitchToSource(musicA, basePitchA, currentPitchMultiplier);
        ApplyPitchToSource(musicB, basePitchB, currentPitchMultiplier);
        ApplyPitchToSource(musicC, basePitchC, currentPitchMultiplier);
    }

    private float GetLowHealthPitchMultiplier()
    {
        if (player == null)
            return 1f;

        float maxHealth = Mathf.Max(1f, player.MaxHealth);
        float health01 = Mathf.Clamp01(player.CurrentHealth / maxHealth);
        float threshold = Mathf.Clamp(lowHealthPitchThreshold, 0.05f, 1f);

        if (health01 >= threshold)
            return 1f;

        float t = Mathf.InverseLerp(threshold, 0f, health01);
        return Mathf.Lerp(1f, Mathf.Clamp(lowHealthMinPitchMultiplier, 0.5f, 1f), t);
    }

    private void ResolvePlayer()
    {
        if (player != null && player.gameObject.activeInHierarchy)
            return;

        if (Time.unscaledTime < nextPlayerResolveAt)
            return;

        nextPlayerResolveAt = Time.unscaledTime + Mathf.Max(0.05f, playerResolveInterval);
        player = PlayerLocator.GetProgression();
    }

    private static float ResolveBasePitch(AudioSource src)
    {
        if (src == null)
            return 1f;

        return Mathf.Max(0.01f, src.pitch);
    }

    private static void ApplyPitchToSource(AudioSource src, float basePitch, float multiplier)
    {
        if (src == null)
            return;

        src.pitch = Mathf.Max(0.01f, basePitch * multiplier);
    }
}
