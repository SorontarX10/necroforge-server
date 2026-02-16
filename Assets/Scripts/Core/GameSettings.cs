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

    // ===== GRAPHICS =====
    public static void SetFullscreen(bool value)
    {
        Fullscreen = value;
        Screen.fullScreen = value;
        Save();
    }

    // ===== SAVE / LOAD =====
    public static void Load()
    {
        MasterVolume = PlayerPrefs.GetFloat("opt_master", 1f);
        MusicVolume  = PlayerPrefs.GetFloat("opt_music", 0.8f);
        SfxVolume    = PlayerPrefs.GetFloat("opt_sfx", 0.8f);
        MouseSensitivity = PlayerPrefs.GetFloat("opt_mouse", 1f);
        Fullscreen = PlayerPrefs.GetInt("opt_fullscreen", 1) == 1;

        Apply();
    }

    static void Save()
    {
        PlayerPrefs.SetFloat("opt_master", MasterVolume);
        PlayerPrefs.SetFloat("opt_music", MusicVolume);
        PlayerPrefs.SetFloat("opt_sfx", SfxVolume);
        PlayerPrefs.SetFloat("opt_mouse", MouseSensitivity);
        PlayerPrefs.SetInt("opt_fullscreen", Fullscreen ? 1 : 0);
        PlayerPrefs.Save();
    }

    static void Apply()
    {
        Screen.fullScreen = Fullscreen;
    }
}
