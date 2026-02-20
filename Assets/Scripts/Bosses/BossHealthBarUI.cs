using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GrassSim.Combat;

[DisallowMultipleComponent]
public class BossHealthBarUI : MonoBehaviour
{
    [Header("Position")]
    [SerializeField] private Vector3 worldOffset = new(0f, 0.2f, 0f);
    [SerializeField] private Vector3 chargingOffset = new(0f, 0.75f, 0f);

    [Header("HUD Template")]
    [SerializeField] private GameObject hudHealthBarTemplateOverride;

    [Header("Charging Label")]
    [SerializeField] private TMP_FontAsset chargingFontOverride;
    [SerializeField] private string chargingText = "charging";
    [SerializeField] private int chargingFontSize = 26;
    [SerializeField] private Color chargingColor = new(1f, 0.2f, 0.2f, 1f);

    private static Canvas sharedCanvas;
    private static GameObject cachedHudHealthBarTemplate;
    private static TMP_FontAsset cachedNecroforgeFont;

    private Combatant combatant;
    private Transform target;
    private GameObject healthBarRoot;
    private Image fillImage;
    private GameObject chargingTextRoot;
    private bool chargingActive;
    private bool healthSubscribed;

    public void Initialize(Combatant combatant, Transform target)
    {
        UnsubscribeHealth();

        this.combatant = combatant;
        this.target = target != null ? target : transform;

        CreateHealthBarIfNeeded();
        CreateChargingTextIfNeeded();
        SubscribeHealth();
        UpdateFill();
    }

    public static void ResetSharedCanvas()
    {
        if (sharedCanvas != null)
        {
            if (Application.isPlaying)
                Object.Destroy(sharedCanvas.gameObject);
            else
                Object.DestroyImmediate(sharedCanvas.gameObject);
        }

        sharedCanvas = null;
        cachedHudHealthBarTemplate = null;
        cachedNecroforgeFont = null;
    }

    public void SetChargingState(bool charging)
    {
        chargingActive = charging;
        if (chargingTextRoot != null)
            chargingTextRoot.SetActive(chargingActive);
    }

    private void OnDestroy()
    {
        UnsubscribeHealth();

        if (healthBarRoot != null)
            Destroy(healthBarRoot);

        if (chargingTextRoot != null)
            Destroy(chargingTextRoot);
    }

    private void HandleHealthChanged()
    {
        UpdateFill();
    }

    private void UpdateFill()
    {
        if (combatant == null || fillImage == null)
            return;

        float max = Mathf.Max(1f, combatant.MaxHealth);
        fillImage.fillAmount = Mathf.Clamp01(combatant.CurrentHealth / max);
    }

    private void CreateHealthBarIfNeeded()
    {
        if (healthBarRoot != null)
            return;

        Canvas canvas = GetOrCreateCanvas();
        if (canvas == null)
            return;

        GameObject template = ResolveHudHealthBarTemplate();
        if (template != null)
            healthBarRoot = Instantiate(template, canvas.transform, false);
        else
            healthBarRoot = CreateFallbackHealthBar(canvas);

        if (healthBarRoot == null)
            return;

        healthBarRoot.name = "BossHealthBar";

        var group = healthBarRoot.GetComponent<CanvasGroup>();
        if (group == null)
            group = healthBarRoot.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        var follower = healthBarRoot.GetComponent<ScreenSpaceWorldUI>();
        if (follower == null)
            follower = healthBarRoot.AddComponent<ScreenSpaceWorldUI>();
        follower.target = target;
        follower.worldOffset = worldOffset + Vector3.up * GetExtraHeightOffset();
        follower.scaleByDistance = true;

        DisableRaycasts(healthBarRoot);
        fillImage = ResolveFillImage(healthBarRoot);

        if (fillImage == null)
            Debug.LogWarning("[BossHealthBarUI] Could not find Fill image on health bar template.", this);
    }

    private void CreateChargingTextIfNeeded()
    {
        if (chargingTextRoot != null)
            return;

        Canvas canvas = GetOrCreateCanvas();
        if (canvas == null)
            return;

        chargingTextRoot = new GameObject(
            "BossChargingText",
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(ScreenSpaceWorldUI),
            typeof(TextMeshProUGUI)
        );
        chargingTextRoot.transform.SetParent(canvas.transform, false);

        var rect = chargingTextRoot.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(220f, 48f);

        var group = chargingTextRoot.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        var follower = chargingTextRoot.GetComponent<ScreenSpaceWorldUI>();
        follower.target = target;
        follower.worldOffset = chargingOffset + Vector3.up * GetExtraHeightOffset();
        follower.scaleByDistance = true;

        TMP_Text chargingLabel = chargingTextRoot.GetComponent<TextMeshProUGUI>();
        chargingLabel.text = chargingText;
        chargingLabel.fontSize = Mathf.Max(8, chargingFontSize);
        chargingLabel.textWrappingMode = TextWrappingModes.NoWrap;
        chargingLabel.alignment = TextAlignmentOptions.Center;
        chargingLabel.color = chargingColor;
        chargingLabel.raycastTarget = false;

        TMP_FontAsset font = ResolveNecroforgeFont();
        if (font != null)
            chargingLabel.font = font;
        else
            Debug.LogWarning("[BossHealthBarUI] Could not resolve Necroforge font for charging label.", this);

        chargingTextRoot.SetActive(chargingActive);
    }

    private GameObject ResolveHudHealthBarTemplate()
    {
        if (hudHealthBarTemplateOverride != null)
            return hudHealthBarTemplateOverride;

        if (cachedHudHealthBarTemplate != null)
            return cachedHudHealthBarTemplate;

        var hud = FindFirstObjectByType<PlayerHUDController>();
        if (hud != null && hud.healthFill != null)
        {
            Transform parent = hud.healthFill.transform.parent;
            if (parent != null)
            {
                cachedHudHealthBarTemplate = parent.gameObject;
                return cachedHudHealthBarTemplate;
            }
        }

        var combatantUi = FindFirstObjectByType<CombatantUI>();
        if (combatantUi != null && combatantUi.healthBarPrefab != null)
        {
            cachedHudHealthBarTemplate = combatantUi.healthBarPrefab;
            return cachedHudHealthBarTemplate;
        }

        return null;
    }

    private TMP_FontAsset ResolveNecroforgeFont()
    {
        if (chargingFontOverride != null)
            return chargingFontOverride;

        if (cachedNecroforgeFont != null)
            return cachedNecroforgeFont;

        TMP_Text[] texts = FindObjectsByType<TMP_Text>(FindObjectsSortMode.None);
        for (int i = 0; i < texts.Length; i++)
        {
            var text = texts[i];
            if (text == null || text.font == null)
                continue;

            if (text.font.name.ToLowerInvariant().Contains("necroforge"))
            {
                cachedNecroforgeFont = text.font;
                return cachedNecroforgeFont;
            }
        }

        TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        for (int i = 0; i < fonts.Length; i++)
        {
            var font = fonts[i];
            if (font == null)
                continue;

            if (font.name.ToLowerInvariant().Contains("necroforge"))
            {
                cachedNecroforgeFont = font;
                return cachedNecroforgeFont;
            }
        }

        return null;
    }

    private void SubscribeHealth()
    {
        if (healthSubscribed || combatant == null)
            return;

        combatant.OnHealthChanged += HandleHealthChanged;
        healthSubscribed = true;
    }

    private void UnsubscribeHealth()
    {
        if (!healthSubscribed || combatant == null)
            return;

        combatant.OnHealthChanged -= HandleHealthChanged;
        healthSubscribed = false;
    }

    private static void DisableRaycasts(GameObject root)
    {
        if (root == null)
            return;

        var images = root.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] != null)
                images[i].raycastTarget = false;
        }

        var tmpTexts = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < tmpTexts.Length; i++)
        {
            if (tmpTexts[i] != null)
                tmpTexts[i].raycastTarget = false;
        }
    }

    private static Image ResolveFillImage(GameObject root)
    {
        if (root == null)
            return null;

        var fillTf = root.transform.Find("Fill");
        if (fillTf != null)
        {
            var namedFill = fillTf.GetComponent<Image>();
            if (namedFill != null)
                return namedFill;
        }

        var images = root.GetComponentsInChildren<Image>(true);

        for (int i = 0; i < images.Length; i++)
        {
            var image = images[i];
            if (image != null && image.type == Image.Type.Filled)
                return image;
        }

        for (int i = 0; i < images.Length; i++)
        {
            var image = images[i];
            if (image != null && image.name.ToLowerInvariant().Contains("fill"))
                return image;
        }

        return images.Length > 0 ? images[0] : null;
    }

    private static GameObject CreateFallbackHealthBar(Canvas canvas)
    {
        var root = new GameObject("BossHealthBarFallback", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(canvas.transform, false);

        var rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(120f, 60f);

        var background = new GameObject("BG", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(root.transform, false);

        var bgRect = background.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        var bgImage = background.GetComponent<Image>();
        bgImage.color = Color.black;

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(root.transform, false);

        var fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(3f, 3f);
        fillRect.offsetMax = new Vector2(-3f, -3f);

        var fillImage = fill.GetComponent<Image>();
        fillImage.color = new Color(0.9f, 0.15f, 0.15f, 1f);
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;

        return root;
    }

    private static Canvas GetOrCreateCanvas()
    {
        if (sharedCanvas != null)
            return sharedCanvas;

        Canvas best = null;
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);

        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas c = canvases[i];
            if (c == null || !c.isActiveAndEnabled)
                continue;

            if (c.renderMode != RenderMode.ScreenSpaceOverlay)
                continue;

            Vector3 scale = c.transform.lossyScale;
            if (Mathf.Abs(scale.x) < 0.001f || Mathf.Abs(scale.y) < 0.001f)
                continue;

            if (best == null || c.sortingOrder >= best.sortingOrder)
                best = c;
        }

        if (best != null)
        {
            sharedCanvas = best;
            return sharedCanvas;
        }

        GameObject canvasGo = new("BossHudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        sharedCanvas = canvasGo.GetComponent<Canvas>();
        sharedCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        sharedCanvas.sortingOrder = 180;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        DontDestroyOnLoad(canvasGo);
        return sharedCanvas;
    }

    private float GetExtraHeightOffset()
    {
        if (target == null)
            return 2f;

        var cap = target.GetComponentInParent<CapsuleCollider>();
        if (cap != null)
            return cap.height + 0.25f;

        var cc = target.GetComponentInParent<CharacterController>();
        if (cc != null)
            return cc.height + 0.25f;

        var rend = target.GetComponentInChildren<Renderer>();
        if (rend != null)
            return rend.bounds.size.y + 0.25f;

        return 2f;
    }
}
