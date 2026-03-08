using GrassSim.Combat;
using GrassSim.Core;
using UnityEngine;
using UnityEngine.UI;

public class StaminaBarBinder : MonoBehaviour
{
    [Header("Runtime Target")]
    public Combatant target;

    [Header("Optional UI Source")]
    [Tooltip("Optional world-space stamina bar prefab. If set, binder will instantiate it at runtime.")]
    public GameObject staminaBar;
    [SerializeField] private bool instantiateBarPrefab;

    [Header("Direct UI bindings (optional)")]
    public Slider slider;
    public Image fillImage;

    private PlayerProgressionController playerProg;
    private BaseCombatAgent agent;
    private WorldStaminaBar worldStaminaBar;
    private GameObject runtimeBarInstance;

    private void Awake()
    {
        if (!target)
            target = GetComponentInParent<Combatant>();

        if (target != null)
        {
            playerProg = target.GetComponentInParent<PlayerProgressionController>();
            agent = target.GetComponentInParent<BaseCombatAgent>();
        }
        else
        {
            playerProg = GetComponentInParent<PlayerProgressionController>();
            agent = GetComponentInParent<BaseCombatAgent>();
        }

        ResolveUiBindings();
        RefreshStaminaUi();
    }

    private void Update()
    {
        RefreshStaminaUi();
    }

    private void OnDestroy()
    {
        if (runtimeBarInstance != null)
            Destroy(runtimeBarInstance);
    }

    private void ResolveUiBindings()
    {
        if (instantiateBarPrefab && staminaBar != null && runtimeBarInstance == null)
        {
            Canvas parentCanvas = Object.FindFirstObjectByType<Canvas>();
            runtimeBarInstance = parentCanvas != null
                ? Instantiate(staminaBar, parentCanvas.transform)
                : Instantiate(staminaBar);
        }

        GameObject searchRoot = runtimeBarInstance != null ? runtimeBarInstance : gameObject;

        if (worldStaminaBar == null)
            worldStaminaBar = searchRoot.GetComponentInChildren<WorldStaminaBar>(true);

        if (slider == null)
            slider = searchRoot.GetComponentInChildren<Slider>(true);

        if (fillImage == null && slider != null && slider.fillRect != null)
            fillImage = slider.fillRect.GetComponent<Image>();

        if (fillImage == null)
        {
            Image[] images = searchRoot.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image candidate = images[i];
                if (candidate == null)
                    continue;

                if (candidate.type == Image.Type.Filled || candidate.name.Contains("Fill"))
                {
                    fillImage = candidate;
                    break;
                }
            }
        }

        if (worldStaminaBar != null)
        {
            if (worldStaminaBar.target == null && target != null)
                worldStaminaBar.target = target.transform;

            if (slider == null)
                slider = worldStaminaBar.slider;
        }
    }

    private void RefreshStaminaUi()
    {
        float max = GetMax();
        float fraction = max > 0f ? GetCur() / max : 0f;
        fraction = Mathf.Clamp01(fraction);

        if (worldStaminaBar != null)
            worldStaminaBar.SetStaminaFraction(fraction);

        if (slider != null)
            slider.value = fraction;

        if (fillImage != null)
            fillImage.fillAmount = fraction;
    }

    private float GetCur()
    {
        if (playerProg != null)
            return playerProg.CurrentStamina;
        if (agent != null)
            return agent.stamina;
        return 0f;
    }

    private float GetMax()
    {
        if (playerProg != null)
            return playerProg.MaxStamina;
        if (agent != null)
            return agent.maxStamina;
        return 0f;
    }
}
