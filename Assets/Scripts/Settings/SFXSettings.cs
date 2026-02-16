using UnityEngine;

public class SFXSettings : MonoBehaviour
{
    public static SFXSettings Instance { get; private set; }

    public AudioSource sfxSource;
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
        if (!sfxSource) return;

        sfxSource.volume =
            baseVolume *
            GameSettings.SfxVolume *
            GameSettings.MasterVolume;
    }

    public float GetVolume(float baseVolume = 1f)
    {
        return baseVolume
             * GameSettings.SfxVolume
             * GameSettings.MasterVolume;
    }
}
