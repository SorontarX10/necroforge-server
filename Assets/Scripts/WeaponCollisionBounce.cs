using UnityEngine;

public class WeaponCollisionBounce : MonoBehaviour
{
    private WeaponController weapon;

    private void Awake()
    {
        weapon = GetComponentInParent<WeaponController>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (weapon == null)
            return;

        if (collision.contactCount == 0)
            return;

        // bierzemy normalną pierwszego punktu kontaktu (WORLD SPACE)
        Vector3 normalWorld = collision.GetContact(0).normal;

        weapon.AddBounce(normalWorld);
    }
}
