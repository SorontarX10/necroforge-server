using UnityEngine;

public static class GameSettings
{
    // AUDIO
    public static float MasterVolume { get; private set; } = 1f;
    public static float MusicVolume  { get; private set; } = 0.8f;
    public static float SfxVolume    { get; private set; } = 0.8f;

    public static event System.Action OnMouseSensitivityChanged;

    // GAMEPLAY
    public static float MouseSensitivity { get; private set; } = 1f;
    public static bool GodMode { get; private set; } = false;

    // GRAPHICS
    public static bool Fullscreen { get; private set; } = true;

    // ===== AUDIO =====
    public static void SetMasterVolume(float v)
    {
        MasterVolume = Mathf.Clamp01(v);
        Debug.Log("Master Volume: " + v);
        AudioListener.volume = MasterVolume;
        Save();
    }

    public static void SetMusicVolume(float v)
    {
        MusicVolume = Mathf.Clamp01(v);
        Save();
    }

    public static void SetSfxVolume(float v)
    {
        SfxVolume = Mathf.Clamp01(v);
        Save();
    }

    // ===== GAMEPLAY =====
    public static void SetMouseSensitivity(float v)
    {
        MouseSensitivity = Mathf.Clamp(v, 0.1f, 5f);
        Save();
        OnMouseSensitivityChanged?.Invoke();
    }

    public static void SetGodMode(bool value)
    {
        GodMode = value;
        Save();
    }

    // ===== GRAPHICS =====
    public static void SetFullscreen(bool value)
    {
        Fullscreen = value;
        ApplyFullscreenMode();
        Save();
    }

    // ===== SAVE / LOAD =====
    public static void Load()
    {
        MasterVolume = PlayerPrefs.GetFloat("opt_master", 1f);
        MusicVolume  = PlayerPrefs.GetFloat("opt_music", 0.8f);
        SfxVolume    = PlayerPrefs.GetFloat("opt_sfx", 0.8f);
        MouseSensitivity = PlayerPrefs.GetFloat("opt_mouse", 1f);
        GodMode = PlayerPrefs.GetInt("opt_godmode", 0) == 1;
        Fullscreen = PlayerPrefs.GetInt("opt_fullscreen", 1) == 1;

        Apply();
    }

    static void Save()
    {
        PlayerPrefs.SetFloat("opt_master", MasterVolume);
        PlayerPrefs.SetFloat("opt_music", MusicVolume);
        PlayerPrefs.SetFloat("opt_sfx", SfxVolume);
        PlayerPrefs.SetFloat("opt_mouse", MouseSensitivity);
        PlayerPrefs.SetInt("opt_godmode", GodMode ? 1 : 0);
        PlayerPrefs.SetInt("opt_fullscreen", Fullscreen ? 1 : 0);
        PlayerPrefs.Save();
    }

    static void Apply()
    {
        ApplyFullscreenMode();
    }

    static void ApplyFullscreenMode()
    {
        if (Fullscreen)
        {
            Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            Screen.fullScreen = true;
            return;
        }

        Screen.fullScreen = false;
        Screen.fullScreenMode = FullScreenMode.Windowed;
    }
}
