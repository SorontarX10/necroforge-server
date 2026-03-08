using GrassSim.Core;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class OptionsMenuController : MonoBehaviour
{
    public Slider masterVolume;
    public Slider musicVolume;
    public Slider sfxVolume;
    public Slider mouseSensitivity;
    public Toggle fullscreenToggle;
    public Dropdown resolutionDropdown;
    public Dropdown windowModeDropdown;
    public Toggle vSyncToggle;
    public Dropdown fpsCapDropdown;
    public Dropdown qualityPresetDropdown;
    public Toggle godModeToggle;

    private bool isInitializing;
    private bool bindingsConfigured;
    private readonly List<ResolutionOption> resolutionOptions = new();
    private int[] fpsCapOptions = { 30, 60, 120, -1 };

    private readonly struct ResolutionOption
    {
        public readonly int width;
        public readonly int height;
        public readonly int refreshHz;

        public ResolutionOption(int width, int height, int refreshHz)
        {
            this.width = width;
            this.height = height;
            this.refreshHz = refreshHz;
        }

        public string Label => $"{width}x{height} ({refreshHz} Hz)";
    }

    private void Awake()
    {
        ConfigureBindings();
    }

    void OnEnable()
    {
        ConfigureBindings();
        isInitializing = true;

        if (masterVolume != null)
            masterVolume.SetValueWithoutNotify(GameSettings.MasterVolume);
        if (musicVolume != null)
            musicVolume.SetValueWithoutNotify(GameSettings.MusicVolume);
        if (sfxVolume != null)
            sfxVolume.SetValueWithoutNotify(GameSettings.SfxVolume);
        if (mouseSensitivity != null)
            mouseSensitivity.SetValueWithoutNotify(GameSettings.MouseSensitivity);
        if (fullscreenToggle != null)
            fullscreenToggle.SetIsOnWithoutNotify(GameSettings.Fullscreen);

        InitializeWindowModeDropdown();
        InitializeResolutionDropdown();
        InitializeVSyncToggle();
        InitializeFpsCapDropdown();
        InitializeQualityPresetDropdown();
        ConfigureGodModeToggleForProfile();

        isInitializing = false;
    }

    private void OnDisable()
    {
        RemoveBindings();
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

        if (windowModeDropdown != null)
        {
            int index = GameSettings.WindowMode == GameSettings.DisplayWindowMode.Windowed ? 0 : 1;
            windowModeDropdown.SetValueWithoutNotify(index);
        }
    }

    public void OnGodMode(bool v)
    {
        if (isInitializing || !BuildProfileResolver.IsDevelopmentToolsEnabled)
            return;

        GameSettings.SetGodMode(v);
    }

    public void OnWindowModeChanged(int dropdownIndex)
    {
        if (isInitializing)
            return;

        GameSettings.DisplayWindowMode mode = dropdownIndex == 0
            ? GameSettings.DisplayWindowMode.Windowed
            : GameSettings.DisplayWindowMode.BorderlessFullscreen;
        GameSettings.SetWindowMode(mode);

        if (fullscreenToggle != null)
            fullscreenToggle.SetIsOnWithoutNotify(GameSettings.Fullscreen);
    }

    public void OnResolutionChanged(int dropdownIndex)
    {
        if (isInitializing)
            return;

        if (dropdownIndex < 0 || dropdownIndex >= resolutionOptions.Count)
            return;

        ResolutionOption selected = resolutionOptions[dropdownIndex];
        GameSettings.SetResolution(selected.width, selected.height, selected.refreshHz);
    }

    public void OnVSyncChanged(bool value)
    {
        if (isInitializing)
            return;

        GameSettings.SetVSyncEnabled(value);
    }

    public void OnFpsCapChanged(int dropdownIndex)
    {
        if (isInitializing)
            return;

        if (dropdownIndex < 0 || dropdownIndex >= fpsCapOptions.Length)
            return;

        GameSettings.SetFpsCap(fpsCapOptions[dropdownIndex]);
    }

    public void OnQualityPresetChanged(int dropdownIndex)
    {
        if (isInitializing)
            return;

        GameSettings.SetQualityPresetIndex(dropdownIndex);
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

    private void InitializeWindowModeDropdown()
    {
        if (windowModeDropdown == null)
            return;

        windowModeDropdown.ClearOptions();
        windowModeDropdown.AddOptions(new List<string> { "Windowed", "Borderless Fullscreen" });
        int selectedIndex = GameSettings.WindowMode == GameSettings.DisplayWindowMode.Windowed ? 0 : 1;
        windowModeDropdown.SetValueWithoutNotify(selectedIndex);
    }

    private void InitializeResolutionDropdown()
    {
        if (resolutionDropdown == null)
            return;

        BuildResolutionOptions();
        resolutionDropdown.ClearOptions();

        List<string> labels = new(resolutionOptions.Count);
        for (int i = 0; i < resolutionOptions.Count; i++)
            labels.Add(resolutionOptions[i].Label);

        resolutionDropdown.AddOptions(labels);

        int selectedIndex = FindBestResolutionIndex(
            GameSettings.ResolutionWidth,
            GameSettings.ResolutionHeight,
            GameSettings.ResolutionRefreshHz
        );
        resolutionDropdown.SetValueWithoutNotify(selectedIndex);
    }

    private void InitializeVSyncToggle()
    {
        if (vSyncToggle == null)
            return;

        vSyncToggle.SetIsOnWithoutNotify(GameSettings.VSyncEnabled);
    }

    private void InitializeFpsCapDropdown()
    {
        if (fpsCapDropdown == null)
            return;

        fpsCapOptions = GameSettings.GetSupportedFpsCaps();
        List<string> labels = new(fpsCapOptions.Length);
        int selectedIndex = 0;

        for (int i = 0; i < fpsCapOptions.Length; i++)
        {
            int value = fpsCapOptions[i];
            labels.Add(value > 0 ? value.ToString() : "Uncapped");
            if (value == GameSettings.FpsCap)
                selectedIndex = i;
        }

        fpsCapDropdown.ClearOptions();
        fpsCapDropdown.AddOptions(labels);
        fpsCapDropdown.SetValueWithoutNotify(selectedIndex);
    }

    private void InitializeQualityPresetDropdown()
    {
        if (qualityPresetDropdown == null)
            return;

        string[] qualityNames = QualitySettings.names;
        if (qualityNames == null || qualityNames.Length == 0)
        {
            qualityPresetDropdown.ClearOptions();
            return;
        }

        qualityPresetDropdown.ClearOptions();
        qualityPresetDropdown.AddOptions(new List<string>(qualityNames));
        int selectedIndex = Mathf.Clamp(GameSettings.QualityPresetIndex, 0, qualityNames.Length - 1);
        qualityPresetDropdown.SetValueWithoutNotify(selectedIndex);
    }

    private void BuildResolutionOptions()
    {
        resolutionOptions.Clear();
        Resolution[] all = Screen.resolutions;

        if (all == null || all.Length == 0)
        {
            resolutionOptions.Add(
                new ResolutionOption(
                    GameSettings.ResolutionWidth,
                    GameSettings.ResolutionHeight,
                    Mathf.Max(30, GameSettings.ResolutionRefreshHz)
                )
            );
            return;
        }

        for (int i = 0; i < all.Length; i++)
        {
            Resolution res = all[i];
            int width = Mathf.Max(640, res.width);
            int height = Mathf.Max(360, res.height);
            int refreshHz = Mathf.Max(30, ResolveRefreshRateHz(res));

            int existingIndex = FindResolutionIndex(width, height);
            if (existingIndex < 0)
            {
                resolutionOptions.Add(new ResolutionOption(width, height, refreshHz));
                continue;
            }

            ResolutionOption existing = resolutionOptions[existingIndex];
            if (refreshHz > existing.refreshHz)
                resolutionOptions[existingIndex] = new ResolutionOption(width, height, refreshHz);
        }

        resolutionOptions.Sort((a, b) =>
        {
            int widthCompare = a.width.CompareTo(b.width);
            if (widthCompare != 0)
                return widthCompare;

            return a.height.CompareTo(b.height);
        });
    }

    private int FindResolutionIndex(int width, int height)
    {
        for (int i = 0; i < resolutionOptions.Count; i++)
        {
            ResolutionOption option = resolutionOptions[i];
            if (option.width == width && option.height == height)
                return i;
        }

        return -1;
    }

    private int FindBestResolutionIndex(int width, int height, int refreshHz)
    {
        if (resolutionOptions.Count == 0)
            return 0;

        int exactIndex = -1;
        int bestDistance = int.MaxValue;
        int bestIndex = 0;
        int targetRefresh = Mathf.Max(30, refreshHz);

        for (int i = 0; i < resolutionOptions.Count; i++)
        {
            ResolutionOption option = resolutionOptions[i];
            if (option.width == width && option.height == height && option.refreshHz == targetRefresh)
                return i;

            if (option.width == width && option.height == height)
                exactIndex = i;

            int dw = option.width - width;
            int dh = option.height - height;
            int distance = dw * dw + dh * dh;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return exactIndex >= 0 ? exactIndex : bestIndex;
    }

    private static int ResolveRefreshRateHz(Resolution resolution)
    {
#if UNITY_2022_2_OR_NEWER
        return Mathf.RoundToInt((float)resolution.refreshRateRatio.value);
#else
        return resolution.refreshRate;
#endif
    }

    private void ConfigureBindings()
    {
        if (bindingsConfigured)
            RemoveBindings();

        if (masterVolume != null)
            masterVolume.onValueChanged.AddListener(OnMasterVolume);
        if (musicVolume != null)
            musicVolume.onValueChanged.AddListener(OnMusicVolume);
        if (sfxVolume != null)
            sfxVolume.onValueChanged.AddListener(OnSfxVolume);
        if (mouseSensitivity != null)
            mouseSensitivity.onValueChanged.AddListener(OnMouseSensitivity);
        if (fullscreenToggle != null)
            fullscreenToggle.onValueChanged.AddListener(OnFullscreen);
        if (windowModeDropdown != null)
            windowModeDropdown.onValueChanged.AddListener(OnWindowModeChanged);
        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
        if (vSyncToggle != null)
            vSyncToggle.onValueChanged.AddListener(OnVSyncChanged);
        if (fpsCapDropdown != null)
            fpsCapDropdown.onValueChanged.AddListener(OnFpsCapChanged);
        if (qualityPresetDropdown != null)
            qualityPresetDropdown.onValueChanged.AddListener(OnQualityPresetChanged);
        if (godModeToggle != null)
            godModeToggle.onValueChanged.AddListener(OnGodMode);

        bindingsConfigured = true;
    }

    private void RemoveBindings()
    {
        if (!bindingsConfigured)
            return;

        if (masterVolume != null)
            masterVolume.onValueChanged.RemoveListener(OnMasterVolume);
        if (musicVolume != null)
            musicVolume.onValueChanged.RemoveListener(OnMusicVolume);
        if (sfxVolume != null)
            sfxVolume.onValueChanged.RemoveListener(OnSfxVolume);
        if (mouseSensitivity != null)
            mouseSensitivity.onValueChanged.RemoveListener(OnMouseSensitivity);
        if (fullscreenToggle != null)
            fullscreenToggle.onValueChanged.RemoveListener(OnFullscreen);
        if (windowModeDropdown != null)
            windowModeDropdown.onValueChanged.RemoveListener(OnWindowModeChanged);
        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.RemoveListener(OnResolutionChanged);
        if (vSyncToggle != null)
            vSyncToggle.onValueChanged.RemoveListener(OnVSyncChanged);
        if (fpsCapDropdown != null)
            fpsCapDropdown.onValueChanged.RemoveListener(OnFpsCapChanged);
        if (qualityPresetDropdown != null)
            qualityPresetDropdown.onValueChanged.RemoveListener(OnQualityPresetChanged);
        if (godModeToggle != null)
            godModeToggle.onValueChanged.RemoveListener(OnGodMode);

        bindingsConfigured = false;
    }
}
