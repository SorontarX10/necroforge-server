using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GrassSim.Enhancers
{
    public class EnhancerIconsPanelUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private EnhancerIconUI iconPrefab;
        [SerializeField] private Transform iconsRoot;

        [Header("Layout")]
        [SerializeField, Range(0.1f, 1f)] private float maxWidthScreenFraction = 0.5f;
        [SerializeField, Min(1)] private int maxRows = 3;
        [SerializeField, Min(0f)] private float iconSpacing = 8f;

        private WeaponEnhancerSystem system;
        private PlayerRelicController relicController;
        private RectTransform iconsRect;
        private Canvas rootCanvas;
        private Vector2 iconCellSize = new Vector2(48f, 48f);

        private readonly Dictionary<ActiveEnhancer, EnhancerIconUI> enhancerIcons = new();
        private readonly Dictionary<string, EnhancerIconUI> relicIcons = new();

        private void Awake()
        {
            EnsureLayout();
        }

        private void Start()
        {
            InvokeRepeating(nameof(TryResolveSources), 0f, 0.25f);
            ApplyWrappedLayout();
        }

        private void TryResolveSources()
        {
            bool resolvedAny = false;

            if (system == null)
            {
                system = FindFirstObjectByType<WeaponEnhancerSystem>();
                if (system != null)
                {
                    system.OnChanged += Refresh;
                    resolvedAny = true;
                }
            }

            if (relicController == null)
            {
                relicController = FindFirstObjectByType<PlayerRelicController>();
                if (relicController != null)
                {
                    relicController.OnChanged += Refresh;
                    resolvedAny = true;
                }
            }

            if (system == null && enhancerIcons.Count > 0)
                ClearEnhancerIcons();

            if (relicController == null && relicIcons.Count > 0)
                ClearRelicIcons();

            if (resolvedAny)
                Refresh();

            ApplyWrappedLayout();
        }

        private void Refresh()
        {
            RefreshEnhancerIcons();
            RefreshRelicIcons();
            ApplyWrappedLayout();
        }

        private void RefreshEnhancerIcons()
        {
            if (system == null)
                return;

            var keys = new List<ActiveEnhancer>(enhancerIcons.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                var key = keys[i];

                bool stillActive = false;
                for (int j = 0; j < system.Active.Count; j++)
                {
                    if (system.Active[j] == key)
                    {
                        stillActive = true;
                        break;
                    }
                }

                if (!stillActive)
                {
                    Destroy(enhancerIcons[key].gameObject);
                    enhancerIcons.Remove(key);
                }
            }

            if (iconPrefab == null || iconsRoot == null)
                return;

            for (int i = 0; i < system.Active.Count; i++)
            {
                var enhancer = system.Active[i];
                if (enhancer == null)
                    continue;

                if (enhancerIcons.ContainsKey(enhancer))
                    continue;

                var icon = Instantiate(iconPrefab, iconsRoot);
                icon.Bind(enhancer);
                enhancerIcons.Add(enhancer, icon);
            }
        }

        private void RefreshRelicIcons()
        {
            if (relicController == null)
                return;

            var keys = new List<string>(relicIcons.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                string relicId = keys[i];

                bool stillActive =
                    relicController.Relics.ContainsKey(relicId) &&
                    relicController.GetStacks(relicId) > 0;

                if (!stillActive)
                {
                    Destroy(relicIcons[relicId].gameObject);
                    relicIcons.Remove(relicId);
                }
            }

            if (iconPrefab == null || iconsRoot == null)
                return;

            foreach (var kvp in relicController.Relics)
            {
                string relicId = kvp.Key;
                RelicDefinition relic = kvp.Value;
                int stackCount = relicController.GetStacks(relicId);

                if (relic == null || stackCount <= 0)
                    continue;

                if (!relicIcons.TryGetValue(relicId, out var icon))
                {
                    icon = Instantiate(iconPrefab, iconsRoot);
                    relicIcons.Add(relicId, icon);
                }

                icon.BindRelic(relic, stackCount);
            }
        }

        private void ClearEnhancerIcons()
        {
            foreach (var icon in enhancerIcons.Values)
            {
                if (icon != null)
                    Destroy(icon.gameObject);
            }

            enhancerIcons.Clear();
        }

        private void ClearRelicIcons()
        {
            foreach (var icon in relicIcons.Values)
            {
                if (icon != null)
                    Destroy(icon.gameObject);
            }

            relicIcons.Clear();
        }

        private void OnDestroy()
        {
            CancelInvoke();

            if (system != null)
                system.OnChanged -= Refresh;

            if (relicController != null)
                relicController.OnChanged -= Refresh;
        }

        private void OnRectTransformDimensionsChange()
        {
            ApplyWrappedLayout();
        }

        private void EnsureLayout()
        {
            if (iconsRoot == null)
                return;

            iconsRect = iconsRoot as RectTransform;
            if (iconsRect == null)
                return;

            if (iconsRect.TryGetComponent(out HorizontalLayoutGroup horizontal))
                horizontal.enabled = false;

            if (iconsRect.TryGetComponent(out ContentSizeFitter fitter))
                fitter.enabled = false;

            var prefabRect = iconPrefab != null ? iconPrefab.GetComponent<RectTransform>() : null;
            float iconWidth = prefabRect != null && prefabRect.rect.width > 0f ? prefabRect.rect.width : 48f;
            float iconHeight = prefabRect != null && prefabRect.rect.height > 0f ? prefabRect.rect.height : 48f;
            iconCellSize = new Vector2(iconWidth, iconHeight);

            var canvas = GetComponentInParent<Canvas>();
            rootCanvas = canvas != null ? canvas.rootCanvas : null;
        }

        private void ApplyWrappedLayout()
        {
            EnsureLayout();

            if (iconsRect == null)
                return;

            float scale = rootCanvas != null && rootCanvas.scaleFactor > 0f ? rootCanvas.scaleFactor : 1f;
            float maxWidth = (Screen.width * Mathf.Clamp01(maxWidthScreenFraction)) / scale;

            float cellWidth = iconCellSize.x;
            float cellHeight = iconCellSize.y;
            float spacingX = iconSpacing;
            float spacingY = iconSpacing;

            float widthPerColumn = cellWidth + spacingX;
            int columns = Mathf.Max(1, Mathf.FloorToInt((maxWidth + spacingX) / Mathf.Max(1f, widthPerColumn)));

            int totalIcons = iconsRect.childCount;
            int maxVisibleIcons = Mathf.Max(1, columns * Mathf.Max(1, maxRows));
            int visibleIcons = Mathf.Min(totalIcons, maxVisibleIcons);

            int layoutIndex = 0;
            for (int i = 0; i < totalIcons; i++)
            {
                var child = iconsRect.GetChild(i);
                bool shouldBeVisible = i < visibleIcons;
                if (child.gameObject.activeSelf != shouldBeVisible)
                    child.gameObject.SetActive(shouldBeVisible);

                if (!shouldBeVisible)
                    continue;

                var childRect = child as RectTransform;
                if (childRect == null)
                    continue;

                int row = layoutIndex / columns;
                int col = layoutIndex % columns;

                childRect.anchorMin = new Vector2(0f, 1f);
                childRect.anchorMax = new Vector2(0f, 1f);
                childRect.pivot = new Vector2(0f, 1f);
                childRect.sizeDelta = iconCellSize;
                childRect.anchoredPosition = new Vector2(col * (cellWidth + spacingX), -row * (cellHeight + spacingY));
                layoutIndex++;
            }

            int rows = visibleIcons > 0 ? Mathf.CeilToInt((float)visibleIcons / columns) : 0;
            rows = Mathf.Min(rows, maxRows);

            Vector2 size = iconsRect.sizeDelta;
            size.x = maxWidth;
            size.y = rows > 0 ? rows * cellHeight + (rows - 1) * spacingY : 0f;
            iconsRect.sizeDelta = size;
        }
    }
}
