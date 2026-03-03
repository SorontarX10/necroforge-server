using GrassSim.Core;
using UnityEngine;

public static class GameSettings
{
    private const string PrefMasterVolume = "opt_master";
    private const string PrefMusicVolume = "opt_music";
    private const string PrefSfxVolume = "opt_sfx";
    private const string PrefMouseSensitivity = "opt_mouse";
    private const string PrefGodMode = "opt_godmode";
    private const string PrefFullscreen = "opt_fullscreen";

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
    private static bool warnedAboutGodModeSanitization;

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
        if (!BuildProfileResolver.IsDevelopmentToolsEnabled)
        {
            if (value || GodMode)
                WarnAndSanitizeGodMode("Ignored SetGodMode request in non-dev build profile.");

            GodMode = false;
            Save();
            return;
        }

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
        MasterVolume = PlayerPrefs.GetFloat(PrefMasterVolume, 1f);
        MusicVolume  = PlayerPrefs.GetFloat(PrefMusicVolume, 0.8f);
        SfxVolume    = PlayerPrefs.GetFloat(PrefSfxVolume, 0.8f);
        MouseSensitivity = PlayerPrefs.GetFloat(PrefMouseSensitivity, 1f);
        GodMode = PlayerPrefs.GetInt(PrefGodMode, 0) == 1;
        Fullscreen = PlayerPrefs.GetInt(PrefFullscreen, 1) == 1;
        SanitizeGodModeIfNeeded("Load");

        Apply();
    }

    static void Save()
    {
        SanitizeGodModeIfNeeded("Save");

        PlayerPrefs.SetFloat(PrefMasterVolume, MasterVolume);
        PlayerPrefs.SetFloat(PrefMusicVolume, MusicVolume);
        PlayerPrefs.SetFloat(PrefSfxVolume, SfxVolume);
        PlayerPrefs.SetFloat(PrefMouseSensitivity, MouseSensitivity);
        PlayerPrefs.SetInt(PrefGodMode, GodMode ? 1 : 0);
        PlayerPrefs.SetInt(PrefFullscreen, Fullscreen ? 1 : 0);
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

    private static void SanitizeGodModeIfNeeded(string source)
    {
        if (BuildProfileResolver.IsDevelopmentToolsEnabled || !GodMode)
            return;

        WarnAndSanitizeGodMode($"Forced GodMode off in non-dev build profile during {source}.");
    }

    private static void WarnAndSanitizeGodMode(string message)
    {
        GodMode = false;
        if (warnedAboutGodModeSanitization)
            return;

        warnedAboutGodModeSanitization = true;
        Debug.LogWarning($"[GameSettings] {message}");
    }
}
