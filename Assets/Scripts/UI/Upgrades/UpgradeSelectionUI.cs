using System.Collections.Generic;
using UnityEngine;
using GrassSim.Core;
using GrassSim.UI;
using GrassSim.Upgrades;

public class UpgradeSelectionUI : MonoBehaviour
{
    [Header("Refs")]
    public Canvas upgradeCanvas;        // <— WAŻNE
    public GameObject root;              // panel z kartami
    public UpgradeCardUI[] cards;
    public UpgradeStatIconLibrary iconLibrary;

    private PlayerProgressionController player;
    private CursorScript cursor;

    private void Awake()
    {
        ChoiceUiQueue.Clear();

        player = PlayerLocator.GetProgression();
        if (player == null)
        {
            Debug.LogWarning("[UpgradeSelectionUI] Player not found yet, waiting...");
        }
        else
        {
            player.OnLevelUpOptionsRolled += QueueShow;
        }

        cursor = FindFirstObjectByType<CursorScript>();
        Hide(); // ⬅️ DOMYŚLNIE UKRYTE
    }

    void Update()
    {
        if (player == null)
        {
            player = PlayerLocator.GetProgression();
            if (player != null)
                player.OnLevelUpOptionsRolled += QueueShow;
        }
    }

    private void OnDestroy()
    {
        if (player != null)
            player.OnLevelUpOptionsRolled -= QueueShow;
    }

    private void QueueShow(List<UpgradeOption> options)
    {
        List<UpgradeOption> snapshot = options != null ? new List<UpgradeOption>(options) : new List<UpgradeOption>();
        ChoiceUiQueue.Enqueue(() => ShowNow(snapshot));
    }

    private void ShowNow(List<UpgradeOption> options)
    {
        if (options == null || options.Count == 0)
        {
            Hide();
            ChoiceUiQueue.CompleteCurrent();
            return;
        }

        if (upgradeCanvas != null)
            upgradeCanvas.enabled = true;

        if (root != null)
            root.SetActive(true);

        cursor?.ShowCursor();

        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == null) continue;

            var opt = (options != null && i < options.Count) ? options[i] : null;
            cards[i].gameObject.SetActive(opt != null);
            if (opt == null)
                continue;

            cards[i].Bind(
                opt,
                iconLibrary,
                RarityColor(opt.rarity),
                OnPick
            );
        }
    }

    private void Hide()
    {
        if (root != null)
            root.SetActive(false);

        if (upgradeCanvas != null)
            upgradeCanvas.enabled = false;
    }

    private void OnPick(UpgradeOption option)
    {
        if (option == null || player == null)
            return;

        // ⬅️ NOWE: zwiększamy weight
        if (UpgradeWeightRuntime.Instance != null)
        {
            UpgradeWeightRuntime.Instance.OnUpgradePicked(option.stat);
        }

        player.ApplyUpgrade(option);
        Hide();
        cursor?.HideCursor();
        ChoiceUiQueue.CompleteCurrent();
    }

    private Color RarityColor(UpgradeRarity r)
    {
        return r switch
        {
            UpgradeRarity.Common     => Hex("#808080"),
            UpgradeRarity.Uncommon   => Hex("#3CB371"),
            UpgradeRarity.Rare       => Hex("#3A7BD5"),
            UpgradeRarity.Legendary  => Hex("#FFD700"), // GOLD
            UpgradeRarity.Mythic     => Hex("#8A2BE2"), // PURPLE
            _ => Color.white
        };
    }

    private Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out var c);
        return c;
    }
}
