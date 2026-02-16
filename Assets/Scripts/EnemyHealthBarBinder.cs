using UnityEngine;
using UnityEngine.UI;
using GrassSim.Combat;
using GrassSim.Enemies;

public class EnemyHealthBarBinder : MonoBehaviour
{
    [Header("Screen UI Prefab")]
    public GameObject healthBarPrefab;

    [Header("Optimization")]
    [Min(0.02f)] public float healthRefreshInterval = 0.08f;
    public float maxUiDistance = 55f;
    [Min(1)] public int uiUpdateEveryNFrames = 2;

    private GameObject instance;
    private ScreenSpaceWorldUI follower;
    private Image fillImage;

    private Combatant combatant;
    private EnemyCombatant enemyCombatant;
    private float nextHealthRefreshAt;
    private bool subscribedToHealth;

    void Awake()
    {
        combatant = GetComponentInParent<Combatant>();
        enemyCombatant = GetComponentInParent<EnemyCombatant>();

        if (combatant == null || enemyCombatant == null || healthBarPrefab == null)
        {
            enabled = false;
            return;
        }
    }

    void Start()
    {
        instance = Instantiate(healthBarPrefab);
        follower = instance.GetComponent<ScreenSpaceWorldUI>();
        fillImage = instance.GetComponentInChildren<Image>();

        if (follower == null || fillImage == null)
        {
            Debug.LogError("[EnemyHealthBarBinder] Prefab missing components.", instance);
            return;
        }

        follower.target = transform;
        follower.worldOffset = Vector3.up * GetHealthbarOffset();
        follower.maxVisibleDistance = maxUiDistance;
        follower.updateEveryNFrames = Mathf.Max(1, uiUpdateEveryNFrames);

        // kolor paska (opcjonalny)
        fillImage.color = Color.red;
        RefreshHealthFill();
    }

    private void OnEnable()
    {
        if (instance != null)
            instance.SetActive(true);

        SubscribeHealthEventsIfNeeded();
        nextHealthRefreshAt = Time.time;
        RefreshHealthFill();
    }

    private void OnDisable()
    {
        UnsubscribeHealthEventsIfNeeded();

        if (instance != null)
            instance.SetActive(false);
    }

    void Update()
    {
        if (combatant == null || fillImage == null || enemyCombatant == null || enemyCombatant.stats == null)
            return;

        float now = Time.time;
        if (now < nextHealthRefreshAt)
            return;

        nextHealthRefreshAt = now + Mathf.Max(0.02f, healthRefreshInterval);
        RefreshHealthFill();
    }

    private float GetHealthbarOffset()
    {
        var cap = GetComponentInParent<CapsuleCollider>();
        if (cap != null) return cap.height + 0.2f;

        var cc = GetComponentInParent<CharacterController>();
        if (cc != null) return cc.height + 0.2f;

        var rend = GetComponentInChildren<Renderer>();
        if (rend != null) return rend.bounds.size.y + 0.2f;

        return 2f;
    }

    private void RefreshHealthFill()
    {
        if (combatant == null || fillImage == null || enemyCombatant == null || enemyCombatant.stats == null)
            return;

        float maxHp = enemyCombatant.stats.maxHealth;
        if (maxHp <= 0f)
            return;

        fillImage.fillAmount = Mathf.Clamp01(combatant.CurrentHealth / maxHp);
    }

    private void SubscribeHealthEventsIfNeeded()
    {
        if (subscribedToHealth || combatant == null)
            return;

        combatant.OnHealthChanged += RefreshHealthFill;
        subscribedToHealth = true;
    }

    private void UnsubscribeHealthEventsIfNeeded()
    {
        if (!subscribedToHealth || combatant == null)
            return;

        combatant.OnHealthChanged -= RefreshHealthFill;
        subscribedToHealth = false;
    }

    private void OnDestroy()
    {
        UnsubscribeHealthEventsIfNeeded();

        if (instance != null)
            Destroy(instance);
    }
}
