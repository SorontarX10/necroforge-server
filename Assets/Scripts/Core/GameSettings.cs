using System;
using GrassSim.Core;
using UnityEngine;

public static class GameSettings
{
    public enum DisplayWindowMode
    {
        Windowed = 0,
        BorderlessFullscreen = 1
    }

    private const string PrefMasterVolume = "opt_master";
    private const string PrefMusicVolume = "opt_music";
    private const string PrefSfxVolume = "opt_sfx";
    private const string PrefMouseSensitivity = "opt_mouse";
    private const string PrefGodMode = "opt_godmode";
    private const string PrefFullscreenLegacy = "opt_fullscreen";
    private const string PrefWindowMode = "opt_window_mode";
    private const string PrefWindowModeDefaultMigrationV2 = "opt_window_mode_default_v2";
    private const string PrefResolutionWidth = "opt_resolution_width";
    private const string PrefResolutionHeight = "opt_resolution_height";
    private const string PrefResolutionRefreshHz = "opt_resolution_refresh_hz";
    private const string PrefVSyncEnabled = "opt_vsync";
    private const string PrefFpsCap = "opt_fps_cap";
    private const string PrefQualityPreset = "opt_quality_preset";

    private const int DefaultFpsCap = 120;
    private const int UncappedFps = -1;

    private static readonly int[] SupportedFpsCaps = { 30, 60, 120, UncappedFps };
    private static bool warnedAboutGodModeSanitization;

    // AUDIO
    public static float MasterVolume { get; private set; } = 1f;
    public static float MusicVolume { get; private set; } = 0.8f;
    public static float SfxVolume { get; private set; } = 0.8f;

    // GAMEPLAY
    public static float MouseSensitivity { get; private set; } = 1f;
    public static bool GodMode { get; private set; }

    // GRAPHICS
    public static DisplayWindowMode WindowMode { get; private set; } = DisplayWindowMode.BorderlessFullscreen;
    public static bool Fullscreen => WindowMode == DisplayWindowMode.BorderlessFullscreen;
    public static int ResolutionWidth { get; private set; } = 1920;
    public static int ResolutionHeight { get; private set; } = 1080;
    public static int ResolutionRefreshHz { get; private set; } = 60;
    public static bool VSyncEnabled { get; private set; } = true;
    public static int FpsCap { get; private set; } = DefaultFpsCap;
    public static int QualityPresetIndex { get; private set; }

    public static event Action OnMouseSensitivityChanged;

    // ===== AUDIO =====
    public static void SetMasterVolume(float value)
    {
        MasterVolume = Mathf.Clamp01(value);
        AudioListener.volume = MasterVolume;
        Save();
    }

    public static void SetMusicVolume(float value)
    {
        MusicVolume = Mathf.Clamp01(value);
        Save();
    }

    public static void SetSfxVolume(float value)
    {
        SfxVolume = Mathf.Clamp01(value);
        Save();
    }

    // ===== GAMEPLAY =====
    public static void SetMouseSensitivity(float value)
    {
        MouseSensitivity = Mathf.Clamp(value, 0.1f, 5f);
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
        SetWindowMode(value ? DisplayWindowMode.BorderlessFullscreen : DisplayWindowMode.Windowed);
    }

    public static void SetWindowMode(DisplayWindowMode mode)
    {
        WindowMode = mode;
        ApplyGraphicsSettings();
        Save();
    }

    public static void SetResolution(int width, int height, int refreshHz = 0)
    {
        ResolutionWidth = Mathf.Max(640, width);
        ResolutionHeight = Mathf.Max(360, height);
        ResolutionRefreshHz = Mathf.Max(30, refreshHz > 0 ? refreshHz : ResolutionRefreshHz);
        ApplyGraphicsSettings();
        Save();
    }

    public static void SetVSyncEnabled(bool value)
    {
        VSyncEnabled = value;
        ApplyGraphicsSettings();
        Save();
    }

    public static void SetFpsCap(int value)
    {
        FpsCap = NormalizeFpsCap(value);
        ApplyGraphicsSettings();
        Save();
    }

    public static void SetQualityPresetIndex(int index)
    {
        QualityPresetIndex = NormalizeQualityPresetIndex(index);
        ApplyGraphicsSettings();
        Save();
    }

    public static int[] GetSupportedFpsCaps()
    {
        int[] copy = new int[SupportedFpsCaps.Length];
        Array.Copy(SupportedFpsCaps, copy, SupportedFpsCaps.Length);
        return copy;
    }

    // ===== SAVE / LOAD =====
    public static void Load()
    {
        MasterVolume = PlayerPrefs.GetFloat(PrefMasterVolume, 1f);
        MusicVolume = PlayerPrefs.GetFloat(PrefMusicVolume, 0.8f);
        SfxVolume = PlayerPrefs.GetFloat(PrefSfxVolume, 0.8f);
        MouseSensitivity = PlayerPrefs.GetFloat(PrefMouseSensitivity, 1f);
        GodMode = PlayerPrefs.GetInt(PrefGodMode, 0) == 1;
        SanitizeGodModeIfNeeded("Load");

        WindowMode = LoadWindowMode();
        ApplyWindowModeDefaultMigrationV2();

        Vector3Int defaultResolution = ResolveDefaultResolution();
        ResolutionWidth = Mathf.Max(640, PlayerPrefs.GetInt(PrefResolutionWidth, defaultResolution.x));
        ResolutionHeight = Mathf.Max(360, PlayerPrefs.GetInt(PrefResolutionHeight, defaultResolution.y));
        ResolutionRefreshHz = Mathf.Max(30, PlayerPrefs.GetInt(PrefResolutionRefreshHz, defaultResolution.z));

        VSyncEnabled = PlayerPrefs.GetInt(PrefVSyncEnabled, 1) == 1;
        FpsCap = NormalizeFpsCap(PlayerPrefs.GetInt(PrefFpsCap, DefaultFpsCap));
        QualityPresetIndex = NormalizeQualityPresetIndex(
            PlayerPrefs.GetInt(PrefQualityPreset, ResolveDefaultQualityPreset())
        );

        Apply();
    }

    private static void ApplyWindowModeDefaultMigrationV2()
    {
        if (PlayerPrefs.GetInt(PrefWindowModeDefaultMigrationV2, 0) == 1)
            return;

        WindowMode = DisplayWindowMode.BorderlessFullscreen;
        PlayerPrefs.SetInt(PrefWindowMode, (int)WindowMode);
        PlayerPrefs.SetInt(PrefFullscreenLegacy, 1);
        PlayerPrefs.SetInt(PrefWindowModeDefaultMigrationV2, 1);
        PlayerPrefs.Save();
    }

    private static void Save()
    {
        SanitizeGodModeIfNeeded("Save");

        PlayerPrefs.SetFloat(PrefMasterVolume, MasterVolume);
        PlayerPrefs.SetFloat(PrefMusicVolume, MusicVolume);
        PlayerPrefs.SetFloat(PrefSfxVolume, SfxVolume);
        PlayerPrefs.SetFloat(PrefMouseSensitivity, MouseSensitivity);
        PlayerPrefs.SetInt(PrefGodMode, GodMode ? 1 : 0);
        PlayerPrefs.SetInt(PrefFullscreenLegacy, Fullscreen ? 1 : 0);
        PlayerPrefs.SetInt(PrefWindowMode, (int)WindowMode);
        PlayerPrefs.SetInt(PrefResolutionWidth, ResolutionWidth);
        PlayerPrefs.SetInt(PrefResolutionHeight, ResolutionHeight);
        PlayerPrefs.SetInt(PrefResolutionRefreshHz, ResolutionRefreshHz);
        PlayerPrefs.SetInt(PrefVSyncEnabled, VSyncEnabled ? 1 : 0);
        PlayerPrefs.SetInt(PrefFpsCap, FpsCap);
        PlayerPrefs.SetInt(PrefQualityPreset, QualityPresetIndex);
        PlayerPrefs.Save();
    }

    private static void Apply()
    {
        AudioListener.volume = Mathf.Clamp01(MasterVolume);
        ApplyGraphicsSettings();
    }

    private static void ApplyGraphicsSettings()
    {
        ApplyWindowModeAndResolution();
        ApplyQualityPreset();
        ApplyFrameTiming();
    }

    private static void ApplyWindowModeAndResolution()
    {
        FullScreenMode mode = WindowMode == DisplayWindowMode.BorderlessFullscreen
            ? FullScreenMode.FullScreenWindow
            : FullScreenMode.Windowed;

        int width;
        int height;
        int refreshHz;

        if (mode == FullScreenMode.FullScreenWindow)
        {
            Resolution desktop = Screen.currentResolution;
            width = Mathf.Max(640, desktop.width > 0 ? desktop.width : ResolutionWidth);
            height = Mathf.Max(360, desktop.height > 0 ? desktop.height : ResolutionHeight);
            refreshHz = Mathf.Max(30, ResolveRefreshRateHz(desktop));
        }
        else
        {
            width = Mathf.Max(640, ResolutionWidth);
            height = Mathf.Max(360, ResolutionHeight);
            refreshHz = Mathf.Max(30, ResolutionRefreshHz);
        }

        Screen.fullScreenMode = mode;

#if UNITY_2022_2_OR_NEWER
        RefreshRate refreshRate = new RefreshRate
        {
            numerator = (uint)Mathf.Max(30, refreshHz),
            denominator = 1u
        };
        Screen.SetResolution(width, height, mode, refreshRate);
#else
        Screen.SetResolution(width, height, mode, refreshHz);
#endif
        if (mode == FullScreenMode.Windowed && Screen.fullScreen)
            Screen.fullScreen = false;
    }

    private static void ApplyFrameTiming()
    {
        QualitySettings.vSyncCount = VSyncEnabled ? 1 : 0;
        if (VSyncEnabled)
        {
            Application.targetFrameRate = -1;
            return;
        }

        Application.targetFrameRate = FpsCap > 0 ? FpsCap : -1;
    }

    private static void ApplyQualityPreset()
    {
        if (QualitySettings.names == null || QualitySettings.names.Length == 0)
            return;

        int clamped = NormalizeQualityPresetIndex(QualityPresetIndex);
        QualityPresetIndex = clamped;

        if (QualitySettings.GetQualityLevel() != clamped)
            QualitySettings.SetQualityLevel(clamped, true);
    }

    private static DisplayWindowMode LoadWindowMode()
    {
        if (PlayerPrefs.HasKey(PrefWindowMode))
        {
            int storedMode = PlayerPrefs.GetInt(PrefWindowMode, (int)DisplayWindowMode.BorderlessFullscreen);
            return storedMode == (int)DisplayWindowMode.Windowed
                ? DisplayWindowMode.Windowed
                : DisplayWindowMode.BorderlessFullscreen;
        }

        bool fullscreenLegacy = PlayerPrefs.GetInt(PrefFullscreenLegacy, 1) == 1;
        return fullscreenLegacy ? DisplayWindowMode.BorderlessFullscreen : DisplayWindowMode.Windowed;
    }

    private static Vector3Int ResolveDefaultResolution()
    {
        Resolution current = Screen.currentResolution;
        int width = Mathf.Max(640, current.width > 0 ? current.width : 1920);
        int height = Mathf.Max(360, current.height > 0 ? current.height : 1080);
        int refreshHz = Mathf.Max(30, ResolveRefreshRateHz(current));
        return new Vector3Int(width, height, refreshHz);
    }

    private static int ResolveRefreshRateHz(Resolution resolution)
    {
#if UNITY_2022_2_OR_NEWER
        return Mathf.RoundToInt((float)resolution.refreshRateRatio.value);
#else
        return resolution.refreshRate;
#endif
    }

    private static int NormalizeFpsCap(int value)
    {
        for (int i = 0; i < SupportedFpsCaps.Length; i++)
        {
            if (SupportedFpsCaps[i] == value)
                return value;
        }

        return DefaultFpsCap;
    }

    private static int ResolveDefaultQualityPreset()
    {
        if (QualitySettings.names == null || QualitySettings.names.Length == 0)
            return 0;

        int current = QualitySettings.GetQualityLevel();
        return Mathf.Clamp(current, 0, QualitySettings.names.Length - 1);
    }

    private static int NormalizeQualityPresetIndex(int index)
    {
        if (QualitySettings.names == null || QualitySettings.names.Length == 0)
            return 0;

        return Mathf.Clamp(index, 0, QualitySettings.names.Length - 1);
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
