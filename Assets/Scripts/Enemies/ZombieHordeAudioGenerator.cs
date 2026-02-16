using UnityEngine;

public class ZombieHordeAudioGenerator : MonoBehaviour
{
    public AudioSource audioSource;

    public AudioClip ZombieHorde;
    public AudioClip ZombieHorde2;

    [Header("Scheduling")]
    [SerializeField, Min(0.5f)] private float minInterval = 5f;
    [SerializeField, Min(0.5f)] private float maxInterval = 11f;
    [SerializeField, Range(0f, 1f)] private float triggerChance = 0.65f;

    private float nextClipAt;

    void Start()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            return;

        if (ZombieHorde != null)
            audioSource.PlayOneShot(ZombieHorde);
        if (ZombieHorde2 != null)
            audioSource.PlayOneShot(ZombieHorde2);

        ScheduleNext(initial: true);
    }

    void Update()
    {
        if (audioSource == null || Time.time < nextClipAt)
            return;

        if (Random.value <= triggerChance)
        {
            AudioClip clip = Random.value < 0.5f ? ZombieHorde : ZombieHorde2;
            if (clip != null)
                audioSource.PlayOneShot(clip);
        }

        ScheduleNext(initial: false);
    }

    private void ScheduleNext(bool initial)
    {
        float minDelay = Mathf.Max(0.5f, minInterval);
        float maxDelay = Mathf.Max(minDelay, maxInterval);
        float delay = Random.Range(minDelay, maxDelay);
        nextClipAt = Time.time + (initial ? Random.Range(0.15f, delay) : delay);
    }
}
