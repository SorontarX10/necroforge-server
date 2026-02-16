using System;
using UnityEngine;

namespace GrassSim.AI
{
    public enum EnemyBrainState
    {
        Idle,
        Patrol,
        Chasing,
        Dead
    }

    [Serializable]
    public class EnemySimState
    {
        public int id;
        public int prefabIndex;

        // aktualna pozycja symulacyjna
        public Vector3 position;

        // punkt bazowy (start / reset patrolu)
        public Vector3 anchor;

        // parametry ruchu symulacyjnego
        public float patrolRadius;
        public float speed;
        public float phase;

        public float health;
        public EnemyBrainState state;
    }
}
