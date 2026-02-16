using UnityEngine;

namespace GrassSim.Stats
{
    public class WorldStats : MonoBehaviour
    {
        public static WorldStats Instance { get; private set; }

        public int difficulty = 1;

        public int enemiesSpawned { get; private set; }
        public int enemiesKilled  { get; private set; }

        public event System.Action OnChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void AddEnemySpawned(int count = 1)
        {
            enemiesSpawned += count;
            OnChanged?.Invoke();
        }

        public void RemoveEnemySpawned(int count = 1)
        {
            enemiesSpawned -= count;
            OnChanged?.Invoke();
        }

        public void AddEnemyKilled(int count = 1)
        {
            enemiesKilled += count;
            OnChanged?.Invoke();
        }

        public void NotifyChanged()
        {
            OnChanged?.Invoke();
        }

        public void ResetStats()
        {
            enemiesSpawned = 0;
            enemiesKilled = 0;
            OnChanged?.Invoke();
        }
    }
}
