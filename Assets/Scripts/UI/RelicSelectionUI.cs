using System.Collections.Generic;
using GrassSim.Core;
using GrassSim.UI;
using UnityEngine;

public class RelicSelectionUI : MonoBehaviour
{
    public GameObject root;
    public RelicCardUI[] cards;

    private PlayerRelicController player;
    private CursorScript cursor;
    private readonly List<RelicDefinition> eligibleRelics = new(8);

    private void Awake()
    {
        if (root != null)
            root.SetActive(false);

        cursor = FindFirstObjectByType<CursorScript>();
        Hide();
    }

    public void Show(List<RelicDefinition> relics)
    {
        List<RelicDefinition> snapshot = relics != null ? new List<RelicDefinition>(relics) : new List<RelicDefinition>();
        ChoiceUiQueue.Enqueue(() => ShowNow(snapshot));
    }

    private void ShowNow(List<RelicDefinition> relics)
    {
        BuildEligibleList(relics);
        if (eligibleRelics.Count == 0)
        {
            Hide();
            ChoiceUiQueue.CompleteCurrent();
            return;
        }

        Time.timeScale = 0f;
        if (root != null)
            root.SetActive(true);

        cursor?.ShowCursor();
        UnlockCursor();

        ResolvePlayer();

        if (cards == null || cards.Length == 0)
        {
            Hide();
            ChoiceUiQueue.CompleteCurrent();
            return;
        }

        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == null)
                continue;

            RelicDefinition relic = i < eligibleRelics.Count ? eligibleRelics[i] : null;
            cards[i].Bind(relic, OnPick);
        }
    }

    private void OnPick(RelicDefinition relic)
    {
        PlayerRelicController resolvedPlayer = ResolvePlayer();
        if (relic == null || resolvedPlayer == null)
        {
            Hide();
            ChoiceUiQueue.CompleteCurrent();
            return;
        }

        bool applied = resolvedPlayer.AddRelic(relic);
        if (!applied)
        {
            Hide();
            ChoiceUiQueue.CompleteCurrent();
            return;
        }

        Hide();
        ChoiceUiQueue.CompleteCurrent();
    }

    private void Hide()
    {
        if (root != null)
            root.SetActive(false);

        bool hasPendingModal = ChoiceUiQueue.PendingCount > 0;
        PlayerRelicController resolvedPlayer = player != null ? player : ResolvePlayer();
        bool upgradeModalActive =
            resolvedPlayer != null
            && resolvedPlayer.Progression != null
            && resolvedPlayer.Progression.IsChoosingUpgrade;

        if (!hasPendingModal && !upgradeModalActive)
        {
            cursor?.HideCursor();
            LockCursor();
            Time.timeScale = 1f;
        }
    }

    private void BuildEligibleList(List<RelicDefinition> relics)
    {
        eligibleRelics.Clear();
        if (relics == null || relics.Count == 0)
            return;

        PlayerRelicController resolvedPlayer = ResolvePlayer();
        for (int i = 0; i < relics.Count; i++)
        {
            RelicDefinition relic = relics[i];
            if (relic == null)
                continue;

            if (resolvedPlayer != null && !resolvedPlayer.CanAcceptRelic(relic))
                continue;

            eligibleRelics.Add(relic);
        }
    }

    private PlayerRelicController ResolvePlayer()
    {
        if (player != null)
            return player;

        player = FindFirstObjectByType<PlayerRelicController>();
        if (player != null)
            return player;

        PlayerProgressionController progression = PlayerLocator.GetProgression();
        if (progression != null)
            player = progression.GetComponent<PlayerRelicController>();

        return player;
    }

    private static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
