using GrassSim.Stats;
using UnityEngine;

public struct GameRunStats
{
    public int kills;
    public float timeSurvived;
    public int finalScore;

    public static GameRunStats Collect()
    {
        GameRunStats s = new GameRunStats();

        if (WorldStats.Instance != null)
        {
            s.kills = WorldStats.Instance.enemiesKilled;
        }
        else
        {
            s.kills = 0;
            Debug.LogWarning("WorldStats.Instance == null when collecting GameRunStats");
        }

        s.timeSurvived = GameTimerController.Instance != null
            ? GameTimerController.Instance.elapsedTime
            : 0f;

        // PROSTA, STABILNA FORMUŁA SCORE
        s.finalScore = s.kills * 100 + Mathf.FloorToInt(s.timeSurvived);

        return s;
    }
}
