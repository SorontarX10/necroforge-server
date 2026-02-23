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

    private bool isInitializing = false;
    private const string GodModeToggleObjectName = "GodMode";
    private const string GodModeLabel = "GOD MODE";

    void OnEnable()
    {
        EnsureGodModeToggle();
        isInitializing = true;

        masterVolume.SetValueWithoutNotify(GameSettings.MasterVolume);
        musicVolume.SetValueWithoutNotify(GameSettings.MusicVolume);
        sfxVolume.SetValueWithoutNotify(GameSettings.SfxVolume);
        mouseSensitivity.SetValueWithoutNotify(GameSettings.MouseSensitivity);
        fullscreenToggle.SetIsOnWithoutNotify(GameSettings.Fullscreen);
        if (godModeToggle != null)
            godModeToggle.SetIsOnWithoutNotify(GameSettings.GodMode);

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

    public void OnGodMode(bool v)
    {
        if (isInitializing) return;
        GameSettings.SetGodMode(v);
    }

    private void EnsureGodModeToggle()
    {
        if (godModeToggle != null)
            return;

        if (fullscreenToggle == null || fullscreenToggle.transform.parent == null)
            return;

        Transform parent = fullscreenToggle.transform.parent;
        Transform existing = parent.Find(GodModeToggleObjectName);
        if (existing != null)
        {
            godModeToggle = existing.GetComponent<Toggle>();
            ConfigureGodModeToggle();
            return;
        }

        GameObject clone = Instantiate(fullscreenToggle.gameObject, parent);
        clone.name = GodModeToggleObjectName;
        clone.transform.SetSiblingIndex(fullscreenToggle.transform.GetSiblingIndex() + 1);

        RectTransform fullRect = fullscreenToggle.GetComponent<RectTransform>();
        RectTransform cloneRect = clone.GetComponent<RectTransform>();
        if (fullRect != null && cloneRect != null)
            cloneRect.anchoredPosition = fullRect.anchoredPosition + new Vector2(0f, -30f);

        godModeToggle = clone.GetComponent<Toggle>();
        ConfigureGodModeToggle();
    }

    private void ConfigureGodModeToggle()
    {
        if (godModeToggle == null)
            return;

        Text[] labels = godModeToggle.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < labels.Length; i++)
            labels[i].text = GodModeLabel;

        godModeToggle.onValueChanged = new Toggle.ToggleEvent();
        godModeToggle.onValueChanged.AddListener(OnGodMode);
    }
}
