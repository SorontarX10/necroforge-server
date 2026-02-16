# Run Pacing Targets (0-5-10 min)

This is a quick balancing pass target table based on the current runtime formulas in:
- `Assets/Scripts/Stats/DifficultyTicker.cs`
- `Assets/Scripts/Stats/DifficultyContext.cs`
- `Assets/Scripts/Progression/PlayerExperience.cs`
- Scene config in `Assets/Scenes/Game.unity`

## Current System Snapshot

- Adaptive difficulty checkpoints (expected):
  - 0:00 -> `difficulty 1`
  - 5:00 -> `difficulty 8`
  - 10:00 -> `difficulty 11`
- Weighted base enemy EXP (from current spawn weights in `Game.unity`): `~12.19 / kill`
- Effective avg EXP per kill (after `DifficultyContext.ExpMultiplier`):
  - 0:00 -> `~10.36`
  - 5:00 -> `~23.28`
  - 10:00 -> `~30.23`
- Target simulation density multiplier (current formulas):
  - 0:00 -> `~2.86x`
  - 5:00 -> `~3.65x`
  - 10:00 -> `~3.85x`

## Target Table

| Time | Target Feel | Target Level | Target Kill Rate | Target TTK (Basic) | Target TTK (Tank) | Pack Clear (10 normals) |
|---|---|---:|---:|---:|---:|---:|
| 0:00 | Barely surviving, mistakes hurt | 1 | 6-9 kills/min | 0.9-1.3s | 2.8-3.8s | 10-14s |
| 5:00 | Stable control vs regular enemies | 12-13 | 15-18 kills/min | 0.35-0.55s | 1.3-1.8s | 4.5-6.5s |
| 10:00 | Power fantasy, focus on max kills | 18-20 | 16-22 kills/min | 0.12-0.25s | 0.5-0.9s | 1.5-2.8s |

## Level Curve Reference (Cumulative XP)

From current `PlayerExperience` (`expToNext=68`, `expGrowth=1.12`):
- Level 12: `1394 XP`
- Level 13: `1628 XP`
- Level 14: `1890 XP`
- Level 15: `2183 XP`
- Level 18: `3289 XP`
- Level 20: `4264 XP`

Practical implication:
- To hit level 12-13 by 5:00, run needs roughly `15-18 kills/min` average in first 5 minutes.
- To hit level 18-20 by 10:00, run needs roughly `14-21 kills/min` average in minutes 5-10.

## Excitement Cadence Target

Instead of flat "levels per minute", use a front-loaded cadence:
- 0:00-3:00 -> `2.5-3.0 lvl/min`
- 3:00-6:00 -> `1.8-2.2 lvl/min`
- 6:00-10:00 -> `1.2-1.6 lvl/min`

This keeps high dopamine early while respecting exponential XP cost later.

## Tuning Order (if run misses targets)

1. XP tempo:
   - `PlayerExperience.expGrowth` in `Assets/Scripts/Progression/PlayerExperience.cs`
   - `DifficultyContext.ExpMultiplier` in `Assets/Scripts/Stats/DifficultyContext.cs`
2. Enemy density:
   - `SpawnDensityMultiplier` and `ScaleSpawnCap` in `Assets/Scripts/Stats/DifficultyContext.cs`
   - `baseEnemies` in `EnemySimulationManager` (`Assets/Scenes/Game.unity`)
   - `maxActiveEnemies` in `EnemyActivationController` (`Assets/Scenes/Game.unity`)
3. Survivability pressure:
   - `EnemyDamageMultiplier` and `EnemyAttackSpeedMultiplier` in `Assets/Scripts/Stats/DifficultyContext.cs`
