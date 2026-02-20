using UnityEngine;
using GrassSim.Core;
using GrassSim.UI;

public class ChestRelicTrigger : MonoBehaviour
{
    [Header("Config")]
    public int relicChoices = 3;

    [Header("Data")]
    public RelicLibrary relicLibrary;

    private RelicSelectionUI relicUI;
    private bool used;
    private PlayerRelicController currentPlayer;

    private void Awake()
    {
        relicUI = FindFirstObjectByType<RelicSelectionUI>();

        if (relicUI == null)
        {
            Debug.LogError(
                "[ChestRelicTrigger] RelicSelectionUI not found in scene!",
                this
            );
        }

        if (relicLibrary == null)
        {
            Debug.LogError(
                "[ChestRelicTrigger] RelicLibrary not assigned!",
                this
            );
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (used)
            return;

        if (!other.CompareTag("Player"))
            return;

        if (relicUI == null || relicLibrary == null)
            return;

        currentPlayer = ResolvePlayer(other);
        used = true;
        OpenChest();
    }

    private void OnEnable()
    {
        MapCollectibleRegistry.RegisterChest(this);
    }

    private void OnDisable()
    {
        MapCollectibleRegistry.UnregisterChest(this);
    }

    private void OpenChest()
    {
        ResolvePlayer();

        var relics = relicLibrary.Roll(relicChoices, currentPlayer);
        relicUI.Show(relics);

        gameObject.SetActive(false);
    }

    private PlayerRelicController ResolvePlayer(Collider source = null)
    {
        if (currentPlayer != null)
            return currentPlayer;

        if (source != null)
        {
            currentPlayer = source.GetComponent<PlayerRelicController>();
            if (currentPlayer == null)
                currentPlayer = source.GetComponentInParent<PlayerRelicController>();
        }

        if (currentPlayer == null)
        {
            var progression = PlayerLocator.GetProgression();
            if (progression != null)
                currentPlayer = progression.GetComponent<PlayerRelicController>();
        }

        if (currentPlayer == null)
            currentPlayer = FindFirstObjectByType<PlayerRelicController>();

        return currentPlayer;
    }
}
