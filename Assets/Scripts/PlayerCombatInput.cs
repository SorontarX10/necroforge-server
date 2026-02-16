using UnityEngine;
using UnityEngine.InputSystem;
using GrassSim.Core;

public class PlayerCombatInput : MonoBehaviour, ICombatInput
{
    public float sensitivity;

    private PlayerControls controls;
    private PlayerProgressionController ppc;
    public bool attacking;

    private void Awake()
    {
        controls = new PlayerControls();
        ppc = GetComponentInParent<PlayerProgressionController>();

        controls.Gameplay.Attack.performed += _ => attacking = true;
        controls.Gameplay.Attack.canceled += _ => attacking = false;
    }

    private void Start()
    {
        // Tak było u Ciebie: WeaponController dostaje input source automatycznie.
        WeaponController weapon = GetComponentInChildren<WeaponController>();
        if (weapon == null)
        {
            Debug.LogError("PlayerCombatInput: brak WeaponController w dzieciach Playera.");
            return;
        }
        sensitivity = GameSettings.MouseSensitivity / 5;
        weapon.combatInputSource = this;
    }

    public Vector2 GetSwingInput()
    {
        Vector2 mouse = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        return mouse * sensitivity;
    }

    public bool IsAttacking()
    {
        if (ppc != null && ppc.IsChoosingUpgrade)
            return false;
        
        return attacking;
    }

    public Vector3 GetMoveDirection()
    {
        return Vector3.zero;
    }

    void OnEnable()
    {
        GameSettings.OnMouseSensitivityChanged += RefreshSensitivity;
        controls?.Gameplay.Enable();
    }

    void OnDisable()
    {
        GameSettings.OnMouseSensitivityChanged -= RefreshSensitivity;
        controls?.Gameplay.Disable();
    }

    void RefreshSensitivity()
    {
        sensitivity = GameSettings.MouseSensitivity;
    }
}
