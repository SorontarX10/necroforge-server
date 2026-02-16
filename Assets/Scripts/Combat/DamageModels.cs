using System;

namespace GrassSim.Combat
{
    [Serializable]
    public struct DamageResult
    {
        public float finalDamage;
        public bool isCrit;
        public float lifestealHeal;
    }
}
