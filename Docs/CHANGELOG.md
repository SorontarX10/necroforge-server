# Changelog

## 0.7.0 - 2026-03-01

### Release
- Bumped application version to `0.7.0`.
- Updated test sandbox version fallback to `0.7.0`.

## 0.6.2 - 2026-03-01

### Gameplay Feel and Control
- Restored strict camera lock during attack and selection overlays (upgrade/relic choice), including clearing residual look smoothing while UI blocks input.
- Re-tuned enemy head knockback response to prevent reliable head-stomp locking while keeping the pushback readable and controllable.

### Release
- Bumped application version to `0.6.2`.
- Updated test sandbox version fallback to `0.6.2`.

## 0.6.1 - 2026-03-01

### Input and Controls
- Switched the project to `Input System` only (`activeInputHandler: 1`) and removed gameplay/UI legacy `Input Manager` fallbacks.
- Stabilized keyboard and mouse handling paths across player movement, combat, pause/menu toggles, world map, and test sandbox hotkeys.

### Release
- Bumped application version to `0.6.1`.
- Updated test sandbox window title to use runtime `Application.version` (with a `0.6.1` fallback) to avoid hardcoded release strings.

### Visual Planning
- Added a new complete visual improvement roadmap with milestones, metrics, acceptance criteria, and risk mitigations (`Docs/PLAN_0.6.1_VISUAL.md`).

### Visual Readability
- Added runtime visual guardrails for post-process and fog (`RuntimeVisualReadabilityStabilizer`) to reduce black crush/overbloom extremes.
- Reworked `DynamicFogController` defaults toward readability-safe ranges and linear fog guardrails.
- Improved floating text clarity under heavy combat load (size clamp, alpha clamp, spawn jitter, stacked offset, fade-safety).
- Added emissive guardrails and smoothing for enemy eye emission, plus capped boss emissive LUT glow scale.

### Combat Feel and Telegraph
- Added anticipation/recovery with heavy attack telegraph markers for elite enemies in `HordeAISystem`, improving dodge readability.
- Added differentiated melee hit feedback: light camera shake for normal hits, stronger shake for crit/boss, and short hit-stop for crit/elite/boss.
- Retuned third-person camera feel and player turning smoothness (input smoothing, pivot damping, minimal look-ahead, reduced direction snapping).
- Added unified rarity tint normalization for persistent relic visuals (`RelicVisualRarityTint`) and applied it to standard/circle style relic effects.

## 0.6.0 - 2026-02-23

### Test Scene Sandbox
- Added a dedicated `TestScene` sandbox bootstrap that auto-spawns a clean player test runtime.
- Added an in-game test panel for selecting and applying any relics, enhancers, and upgrade entries.
- Added full loadout editing in runtime: stack increase/decrease, individual remove, full clear, and instant player rebuild.
- Added direct zombie arena controls: spawn waves, clear all spawned zombies, and switch between walking or standing zombie mode.
- Disabled autonomous spawn systems in `TestScene` during sandbox runtime, so tests are deterministic.
- Added quick utility actions for test flow: respawn with preserved loadout, refill health/stamina, cursor lock toggle, and catalog refresh.

## 0.5.2 - 2026-02-22

### Gameplay
- Added elite enemies: yellow eyes, dedicated aura, higher HP/damage and more EXP than standard enemies.
- Elite-aware relic effects are now supported.
- Elite enemies can drop a chest on death (20% chance).
- Zombies now emerge from the ground on spawn; they cannot move or deal damage until emergence completes.
- Removed the "simulated-only" enemy path from normal gameplay flow; enemies now spawn through the standard path.
- Increased boss durability (especially after the 10-minute mark) and boss damage.

### Performance and Streaming
- Decoupled fog from chunk despawn (fog no longer disappears with chunk unload).
- Added a chunk streaming mask: gray fallback surface that hides black voids after chunk despawn.
- Added global active-enemy limits (including a separate elite cap) to reduce stutters at high active GO counts.
- Reduced spawn/despawn churn via hysteresis and despawn budget tuning.
- Added limits to top relic proc snowballing (cooldown/per-target/proc budget) to stabilize frametime.

### UI / UX
- Improved overheal and barrier visibility on HUD.
- Improved card readability: better letter spacing and title positioning (higher/tighter) to avoid overlap.

### Stability and Fixes
- Fixed `Can not play a disabled audio source` during enemy attacks.
- Fixed ParticleSystem error: `Particle Velocity curves must all be in the same mode`.
- Improved player/weapon trigger colliders (including replacing problematic sword trigger setup with a simpler collider) to remove concave/convex mesh warnings.
- Fixed enemy spawn regression.

### Telemetry
- Added final difficulty snapshot logging before `quit_to_menu` / scene reset, so end-of-run metrics are not underreported.

### Visual Hotfix (0.5.2)
- Removed sources of pink/magenta orb artifacts.
- Runtime VFX material resolution now uses URP-safe paths.
- Particle renderers with invalid materials are disabled instead of rendering magenta.
- Generated relic fallback visuals no longer use spherical proxy geometry.
- Added runtime relic-VFX pool reset between sessions.
