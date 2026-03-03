using GrassSim.Core;
using UnityEngine;
using UnityEngine.UI;

public class OptionsMenuController : MonoBehaviour
{
    public Slider masterVolume;
    public Slider musicVolume;
    public Slider sfxVolume;
    public Slider mouseSensitivity;
    public Toggle fullscreenToggle;
    public Toggle godModeToggle;

    private bool isInitializing;

    void OnEnable()
    {
        isInitializing = true;

        masterVolume.SetValueWithoutNotify(GameSettings.MasterVolume);
        musicVolume.SetValueWithoutNotify(GameSettings.MusicVolume);
        sfxVolume.SetValueWithoutNotify(GameSettings.SfxVolume);
        mouseSensitivity.SetValueWithoutNotify(GameSettings.MouseSensitivity);
        fullscreenToggle.SetIsOnWithoutNotify(GameSettings.Fullscreen);
        ConfigureGodModeToggleForProfile();

        isInitializing = false;
    }

    public void OnMasterVolume(float v)
    {
        if (isInitializing)
            return;

        GameSettings.SetMasterVolume(v);

        if (MusicSettings.Instance != null)
            MusicSettings.Instance.Apply();

        if (SFXSettings.Instance != null)
            SFXSettings.Instance.Apply();

        MusicPhaseController phase = FindFirstObjectByType<MusicPhaseController>();
        if (phase != null)
            phase.RefreshVolumes();
    }

    public void OnMusicVolume(float v)
    {
        if (isInitializing)
            return;

        GameSettings.SetMusicVolume(v);

        if (MusicSettings.Instance != null)
            MusicSettings.Instance.Apply();

        MusicPhaseController phase = FindFirstObjectByType<MusicPhaseController>();
        if (phase != null)
            phase.RefreshVolumes();
    }

    public void OnSfxVolume(float v)
    {
        if (isInitializing)
            return;

        GameSettings.SetSfxVolume(v);

        if (SFXSettings.Instance != null)
            SFXSettings.Instance.Apply();

        foreach (SFXAutoVolume sfx in FindObjectsByType<SFXAutoVolume>(FindObjectsSortMode.None))
            sfx.Apply();
    }

    public void OnMouseSensitivity(float v)
    {
        if (isInitializing)
            return;

        GameSettings.SetMouseSensitivity(v);
    }

    public void OnFullscreen(bool v)
    {
        if (isInitializing)
            return;

        GameSettings.SetFullscreen(v);
    }

    public void OnGodMode(bool v)
    {
        if (isInitializing || !BuildProfileResolver.IsDevelopmentToolsEnabled)
            return;

        GameSettings.SetGodMode(v);
    }

    private void ConfigureGodModeToggleForProfile()
    {
        if (godModeToggle == null)
            return;

        bool isDevToolsEnabled = BuildProfileResolver.IsDevelopmentToolsEnabled;
        godModeToggle.gameObject.SetActive(isDevToolsEnabled);
        if (!isDevToolsEnabled)
        {
            godModeToggle.SetIsOnWithoutNotify(false);
            return;
        }

        godModeToggle.SetIsOnWithoutNotify(GameSettings.GodMode);
    }
}
