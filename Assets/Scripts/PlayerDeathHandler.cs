using UnityEngine;

public class PlayerDeathHandler : MonoBehaviour
{
    private bool handled;

    private void OnCombatantDied()
    {
        if (handled) return;
        handled = true;

        Debug.Log("[PlayerDeathHandler] Player died.");

        DisablePlayerControl();

        // NOWY GAME OVER OVERLAY
        var go = FindAnyObjectByType<GameOverController>();
        if (go != null)
            go.OnPlayerDied();
        else
            Debug.LogError("[PlayerDeathHandler] GameOverController not found in scene!");
    }

    private void DisablePlayerControl()
    {
        var input = GetComponent<PlayerCombatInput>();
        if (input) input.enabled = false;

        var weapon = GetComponentInChildren<WeaponController>();
        if (weapon) weapon.enabled = false;
    }
}
