using UnityEngine;

public class BossRootDebuff : MonoBehaviour
{
    [SerializeField] private float rootedUntil;

    public bool IsRooted => Time.time < rootedUntil;

    public void Apply(float duration)
    {
        rootedUntil = Mathf.Max(rootedUntil, Time.time + Mathf.Max(0.05f, duration));
    }
}
