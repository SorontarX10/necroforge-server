using System.Collections.Generic;
using UnityEngine;
using GrassSim.Core;
using GrassSim.UI;
using GrassSim.Upgrades;

public class UpgradeSelectionUI : MonoBehaviour
{
    [Header("Refs")]
    public Canvas upgradeCanvas;
    public GameObject root;
    public UpgradeCardUI[] cards;
    public UpgradeStatIconLibrary iconLibrary;

    private PlayerProgressionController player;
    private CursorScript cursor;

    private void Awake()
    {
        ResolvePlayer();
        cursor = FindFirstObjectByType<CursorScript>();
        Hide();
    }

    private void Update()
    {
        ResolvePlayer();
    }

    private void OnDestroy()
    {
        if (player != null)
            player.OnLevelUpOptionsRolled -= QueueShow;
    }

    private void QueueShow(List<UpgradeOption> options)
    {
        List<UpgradeOption> snapshot = options != null
            ? new List<UpgradeOption>(options)
            : new List<UpgradeOption>();

        ChoiceUiQueue.Enqueue(() => ShowNow(snapshot), "upgrade_selection");
    }

    private void ShowNow(List<UpgradeOption> options)
    {
        if (ResolvePlayer() == null)
        {
            Hide();
            ChoiceUiQueue.CompleteCurrent("upgrade_selection_no_player");
            return;
        }

        if (cards == null || cards.Length == 0)
        {
            Hide();
            ChoiceUiQueue.CompleteCurrent("upgrade_selection_no_cards");
            return;
        }

        if (options == null || options.Count == 0)
        {
            Hide();
            ChoiceUiQueue.CompleteCurrent("upgrade_selection_empty");
            return;
        }

        if (upgradeCanvas != null)
            upgradeCanvas.enabled = true;

        if (root != null)
            root.SetActive(true);

        cursor?.ShowCursor();

        for (int i = 0; i < cards.Length; i++)
        {
            UpgradeCardUI card = cards[i];
            if (card == null)
                continue;

            UpgradeOption option = i < options.Count ? options[i] : null;
            card.gameObject.SetActive(option != null);
            if (option == null)
                continue;

            card.Bind(
                option,
                iconLibrary,
                RarityColor(option.rarity),
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
        PlayerProgressionController resolvedPlayer = ResolvePlayer();
        if (option == null || resolvedPlayer == null)
        {
            Hide();
            ChoiceUiQueue.CompleteCurrent("upgrade_selection_invalid_pick");
            return;
        }

        if (UpgradeWeightRuntime.Instance != null)
            UpgradeWeightRuntime.Instance.OnUpgradePicked(option.stat);

        resolvedPlayer.ApplyUpgrade(option);

        Hide();

        if (ChoiceUiQueue.PendingCount > 0)
        {
            Time.timeScale = 0f;
        }
        else
        {
            cursor?.HideCursor();
        }

        ChoiceUiQueue.CompleteCurrent("upgrade_selection_pick");
    }

    private PlayerProgressionController ResolvePlayer()
    {
        if (player != null)
            return player;

        player = PlayerLocator.GetProgression();
        if (player == null)
            return null;

        player.OnLevelUpOptionsRolled -= QueueShow;
        player.OnLevelUpOptionsRolled += QueueShow;
        return player;
    }

    private Color RarityColor(UpgradeRarity rarity)
    {
        return rarity switch
        {
            UpgradeRarity.Common => Hex("#808080"),
            UpgradeRarity.Uncommon => Hex("#3CB371"),
            UpgradeRarity.Rare => Hex("#3A7BD5"),
            UpgradeRarity.Legendary => Hex("#FFD700"),
            UpgradeRarity.Mythic => Hex("#8A2BE2"),
            _ => Color.white
        };
    }

    private Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color color);
        return color;
    }
}
