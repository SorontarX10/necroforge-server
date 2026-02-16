using System.Collections.Generic;
using GrassSim.UI;
using UnityEngine;

public class RelicSelectionUI : MonoBehaviour
{
    public GameObject root;
    public RelicCardUI[] cards;

    private PlayerRelicController player;
    private CursorScript cursor;

    private void Awake()
    {
        ChoiceUiQueue.Clear();

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
        if (relics == null || relics.Count == 0)
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

        player = FindFirstObjectByType<PlayerRelicController>();

        if (cards == null)
            return;

        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == null)
                continue;

            RelicDefinition relic = i < relics.Count ? relics[i] : null;
            cards[i].Bind(relic, OnPick);
        }
    }

    private void OnPick(RelicDefinition relic)
    {
        if (relic == null || player == null)
            return;

        bool applied = player.AddRelic(relic);
        if (!applied)
            return;

        Hide();
        ChoiceUiQueue.CompleteCurrent();
    }

    private void Hide()
    {
        if (root != null)
            root.SetActive(false);

        bool hasPendingModal = ChoiceUiQueue.PendingCount > 0;
        bool upgradeModalActive = player != null && player.Progression != null && player.Progression.IsChoosingUpgrade;
        if (!hasPendingModal && !upgradeModalActive)
        {
            cursor?.HideCursor();
            LockCursor();
            Time.timeScale = 1f;
        }
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
