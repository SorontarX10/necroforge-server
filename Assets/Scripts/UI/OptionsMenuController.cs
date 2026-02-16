using UnityEngine;
using UnityEngine.UI;

public class OptionsMenuController : MonoBehaviour
{
    public Slider masterVolume;
    public Slider musicVolume;
    public Slider sfxVolume;
    public Slider mouseSensitivity;
    public Toggle fullscreenToggle;

    private bool isInitializing = false;

    void OnEnable()
    {
        isInitializing = true;

        masterVolume.SetValueWithoutNotify(GameSettings.MasterVolume);
        musicVolume.SetValueWithoutNotify(GameSettings.MusicVolume);
        sfxVolume.SetValueWithoutNotify(GameSettings.SfxVolume);
        mouseSensitivity.SetValueWithoutNotify(GameSettings.MouseSensitivity);
        fullscreenToggle.SetIsOnWithoutNotify(GameSettings.Fullscreen);

        isInitializing = false;
    }

    public void OnMasterVolume(float v)
    {
        if (isInitializing) return;

        GameSettings.SetMasterVolume(v);

        if (MusicSettings.Instance != null)
            MusicSettings.Instance.Apply();
        
        if (SFXSettings.Instance != null)
            SFXSettings.Instance.Apply();

        var phase = FindFirstObjectByType<MusicPhaseController>();
        if (phase != null)
            phase.RefreshVolumes();
    }


    public void OnMusicVolume(float v)
    {
        if (isInitializing) return;

        GameSettings.SetMusicVolume(v);

        if (MusicSettings.Instance != null)
            MusicSettings.Instance.Apply();
        
        var phase = FindFirstObjectByType<MusicPhaseController>();
        if (phase != null)
            phase.RefreshVolumes();
    }

    public void OnSfxVolume(float v)
    {
        if (isInitializing) return;

        GameSettings.SetSfxVolume(v);

        if (SFXSettings.Instance != null)
            SFXSettings.Instance.Apply();
        
        foreach (var sfx in FindObjectsByType<SFXAutoVolume>(
            FindObjectsSortMode.None))
        {
            sfx.Apply();
        }
    }

    public void OnMouseSensitivity(float v)
    {
        if (isInitializing) return;
        GameSettings.SetMouseSensitivity(v);
    }

    public void OnFullscreen(bool v)
    {
        if (isInitializing) return;
        GameSettings.SetFullscreen(v);
    }
}
