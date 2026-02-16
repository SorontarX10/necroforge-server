using UnityEngine;

public abstract class RelicEffect : ScriptableObject
{
    public string displayName;
    [TextArea] public string description;

    public abstract void OnAcquire(PlayerRelicController player, int stacks);
    public abstract void OnStack(PlayerRelicController player, int stacks);
}
