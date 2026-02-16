using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class SFXAutoVolume : MonoBehaviour
{
    public float baseVolume = 1f;

    AudioSource src;

    void Awake()
    {
        src = GetComponent<AudioSource>();
    }

    void Start()
    {
        Apply();
    }

    public void Apply()
    {
        if (SFXSettings.Instance == null) return;

        src.volume = SFXSettings.Instance.GetVolume(baseVolume);
    }
}
