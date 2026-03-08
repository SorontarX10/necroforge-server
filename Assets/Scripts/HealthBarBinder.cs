using GrassSim.Combat;
using GrassSim.Core;
using GrassSim.Enemies;
using UnityEngine;
using UnityEngine.UI;

public class HealthBarBinder : MonoBehaviour
{
    [Header("Runtime Target")]
    public Combatant target;

    [Header("Optional UI Source")]
    [Tooltip("Optional world-space health bar prefab. If set, binder will instantiate it at runtime.")]
    public GameObject bar;
    [SerializeField] private bool instantiateBarPrefab;

    [Header("Direct UI bindings (optional)")]
    public Slider slider;
    public Image fillImage;

    private PlayerProgressionController playerProg;
    private EnemyCombatant enemyCombatant;
    private WorldHealthBar worldHealthBar;
    private GameObject runtimeBarInstance;

    private void Awake()
    {
        if (!target)
            target = GetComponentInParent<Combatant>();

        playerProg = GetComponentInParent<PlayerProgressionController>();
        enemyCombatant = GetComponentInParent<EnemyCombatant>();

        ResolveUiBindings();
        RefreshHealthUi();
    }

    private void Update()
    {
        RefreshHealthUi();
    }

    private void OnDestroy()
    {
        if (runtimeBarInstance != null)
            Destroy(runtimeBarInstance);
    }

    private void ResolveUiBindings()
    {
        if (instantiateBarPrefab && bar != null && runtimeBarInstance == null)
        {
            Canvas parentCanvas = Object.FindFirstObjectByType<Canvas>();
            runtimeBarInstance = parentCanvas != null
                ? Instantiate(bar, parentCanvas.transform)
                : Instantiate(bar);
        }

        GameObject searchRoot = runtimeBarInstance != null ? runtimeBarInstance : gameObject;

        if (worldHealthBar == null)
            worldHealthBar = searchRoot.GetComponentInChildren<WorldHealthBar>(true);

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

        if (worldHealthBar != null && target != null)
            worldHealthBar.Initialize(target.transform, playerProg != null, Camera.main);
    }

    private void RefreshHealthUi()
    {
        if (target == null)
            return;

        float maxHp = GetMaxHp();
        float fraction = maxHp > 0f ? target.CurrentHealth / maxHp : 0f;
        fraction = Mathf.Clamp01(fraction);

        if (worldHealthBar != null)
            worldHealthBar.SetHealthFraction(fraction);

        if (slider != null)
            slider.value = fraction;

        if (fillImage != null)
            fillImage.fillAmount = fraction;
    }

    private float GetMaxHp()
    {
        if (playerProg != null)
            return playerProg.MaxHealth;
        if (enemyCombatant != null && enemyCombatant.stats != null)
            return enemyCombatant.stats.maxHealth;
        if (target != null)
            return Mathf.Max(1f, target.MaxHealth);

        return 1f;
    }
}
