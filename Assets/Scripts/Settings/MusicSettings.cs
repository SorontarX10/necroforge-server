using UnityEngine;

public class MusicSettings : MonoBehaviour
{
    public static MusicSettings Instance { get; private set; }

    public AudioSource musicSource;
    public float baseVolume = 1f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        Apply();
    }

    public void Apply()
    {
        if (!musicSource) return;

        musicSource.volume =
            baseVolume *
            GameSettings.MusicVolume *
            GameSettings.MasterVolume;
    }
}
