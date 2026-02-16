using UnityEngine;
using GrassSim.UI;

public class ChestRelicTrigger : MonoBehaviour
{
    [Header("Config")]
    public int relicChoices = 3;

    [Header("Data")]
    public RelicLibrary relicLibrary; // ScriptableObject – OK w prefabie

    private RelicSelectionUI relicUI;
    private bool used;
    private PlayerRelicController currentPlayer;

    private void Awake()
    {
        // NIE w OnTrigger – tylko raz
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
        if (used) return;
        if (!other.CompareTag("Player")) return;

        if (relicUI == null || relicLibrary == null)
            return;

        currentPlayer = other.GetComponentInParent<PlayerRelicController>();
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
        var relics = relicLibrary.Roll(relicChoices, currentPlayer);
        relicUI.Show(relics);

        // anim / sfx możesz tu dorzucić
        gameObject.SetActive(false);
    }
}
