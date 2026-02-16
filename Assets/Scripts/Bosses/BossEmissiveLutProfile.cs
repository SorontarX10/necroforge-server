using System;
using UnityEngine;

[CreateAssetMenu(fileName = "BossEmissiveLutProfile", menuName = "Bosses/Emissive LUT Profile")]
public class BossEmissiveLutProfile : ScriptableObject
{
    [Serializable]
    private struct EmissiveEntry
    {
        public BossEnemyController.BossArchetype archetype;
        public Color lutColor;
        [Min(0.1f)] public float glowScale;
    }

    [SerializeField] private Color fallbackLutColor = Color.white;
    [SerializeField, Min(0.1f)] private float fallbackGlowScale = 1f;
    [SerializeField] private EmissiveEntry[] entries =
    {
        new EmissiveEntry { archetype = BossEnemyController.BossArchetype.Zombie, lutColor = new Color(0.24f, 0.95f, 0.38f, 1f), glowScale = 0.95f },
        new EmissiveEntry { archetype = BossEnemyController.BossArchetype.Quick, lutColor = new Color(0.32f, 0.82f, 1f, 1f), glowScale = 1.2f },
        new EmissiveEntry { archetype = BossEnemyController.BossArchetype.Tank, lutColor = new Color(1f, 0.34f, 0.28f, 1f), glowScale = 0.9f },
        new EmissiveEntry { archetype = BossEnemyController.BossArchetype.Dog, lutColor = new Color(1f, 0.76f, 0.27f, 1f), glowScale = 1.12f }
    };

    public bool TryGet(BossEnemyController.BossArchetype archetype, out Color lutColor, out float glowScale)
    {
        if (entries != null)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                EmissiveEntry entry = entries[i];
                if (entry.archetype != archetype)
                    continue;

                lutColor = entry.lutColor;
                glowScale = Mathf.Max(0.1f, entry.glowScale);
                return true;
            }
        }

        lutColor = fallbackLutColor;
        glowScale = Mathf.Max(0.1f, fallbackGlowScale);
        return false;
    }

    private void OnValidate()
    {
        fallbackGlowScale = Mathf.Max(0.1f, fallbackGlowScale);
        if (entries == null)
            return;

        for (int i = 0; i < entries.Length; i++)
        {
            EmissiveEntry entry = entries[i];
            entry.glowScale = Mathf.Max(0.1f, entry.glowScale);
            entries[i] = entry;
        }
    }
}
