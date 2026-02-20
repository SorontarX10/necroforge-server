using System;
using UnityEngine;

namespace GrassSim.Progression
{
    [Serializable]
    public class PlayerExperience
    {
        public int level = 1;
        public int exp = 0;
        public int expToNext = 76;

        public float expGrowth = 1.17f;

        public bool AddExp(int amount)
        {
            return AddExpAndGetLevelUps(amount) > 0;
        }

        public int AddExpAndGetLevelUps(int amount)
        {
            if (amount <= 0) return 0;

            exp += amount;

            int levelsGained = 0;
            while (exp >= expToNext)
            {
                exp -= expToNext;
                level++;
                expToNext = Mathf.Max(1, Mathf.RoundToInt(expToNext * expGrowth));
                levelsGained++;
            }

            return levelsGained;
        }
    }
}
