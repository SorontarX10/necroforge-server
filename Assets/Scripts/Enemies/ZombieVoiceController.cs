using UnityEngine;

public class ZombieVoiceController : MonoBehaviour
{
    public AudioSource audioSource;

    public AudioClip ZombieArgh;
    public AudioClip ZombieHurts;
    public AudioClip ZombieMoan;
    public AudioClip ZombieMoan2;
    public AudioClip ZombieMoan3;
    public AudioClip ZombieMoan4;
    public AudioClip ZombieOargh;

    [Header("Scheduling")]
    [SerializeField, Min(0.2f)] private float minVoiceInterval = 3.6f;
    [SerializeField, Min(0.2f)] private float maxVoiceInterval = 7.5f;
    [SerializeField, Range(0f, 1f)] private float voiceTriggerChance = 0.85f;

    private float nextVoiceAt;

    private void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    private void OnEnable()
    {
        ScheduleNextVoice(initial: true);
    }

    void Update()
    {
        if (audioSource == null || Time.time < nextVoiceAt)
            return;

        if (Random.value <= voiceTriggerChance)
            PlayRandomVoiceClip();

        ScheduleNextVoice(initial: false);
    }

    private void ScheduleNextVoice(bool initial)
    {
        float minInterval = Mathf.Max(0.2f, minVoiceInterval);
        float maxInterval = Mathf.Max(minInterval, maxVoiceInterval);
        float jitter = Random.Range(minInterval, maxInterval);
        nextVoiceAt = Time.time + (initial ? Random.Range(0.15f, jitter) : jitter);
    }

    private void PlayRandomVoiceClip()
    {
        AudioClip[] pool =
        {
            ZombieArgh,
            ZombieHurts,
            ZombieMoan,
            ZombieMoan2,
            ZombieMoan3,
            ZombieMoan4,
            ZombieOargh
        };

        int available = 0;
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] != null)
                available++;
        }

        if (available == 0)
            return;

        int pick = Random.Range(0, available);
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] == null)
                continue;

            if (pick-- == 0)
            {
                audioSource.PlayOneShot(pool[i]);
                return;
            }
        }
    }
}
