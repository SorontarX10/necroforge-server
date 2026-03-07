# Changelog

## 0.7.2 - 2026-03-03

### Build Profiles
- Added runtime build profile resolver + standalone build automation/prebuild validation for `Dev`, `Demo`, `InternalQA`, and `Release`.
- Added profile smoke artifacts and setup docs in `Docs/BUILD_PROFILES.md`.

### Cheat Security
- Locked `GodMode` behind development profile checks in `GameSettings`.
- `opt_godmode` from `PlayerPrefs` is now ignored and sanitized outside dev profiles, with a warning log.
- Removed automatic `GodMode` toggle creation from options menu and limited debug cheat panel rendering to dev-tool profiles.
- Added `is_cheat_session` marker to gameplay telemetry run payloads.

### Telemetry Profiles
- Added telemetry bootstrap gating for `GameplayTelemetryRecorder`, `RuntimePerformanceSummary`, and `RuntimeHitchDiagnostics` based on active `TelemetryMode`.
- Added hard file-write guard (`LocalTelemetryFileOutput`) so telemetry/perf/hitch files are not created when `TelemetryMode=OFF`.
- Added startup diagnostics logs with explicit `TelemetryMode=OFF|DEV_LOCAL`.

### Online Leaderboard (MVP Foundation)
- Added backend service scaffold (`Backend/LeaderboardApi`) with PostgreSQL schema for `players`, `runs`, `leaderboard_entries`, and `moderation_flags`.
- Added API endpoints: `POST /runs/start`, `POST /runs/submit`, `GET /leaderboard`, `GET /leaderboard/me`, plus `/health` and `/metrics`.
- Added Oracle/VPS deployment scaffold (`Infra/leaderboard`) with `docker-compose`, PostgreSQL, API container, and Caddy reverse proxy.
- Added Unity online leaderboard client and submission flow from `GameOver` with local fallback to offline leaderboard.
- Added leaderboard UI runtime states in menu/game over: loading, error fallback, retry button support, and "my rank" online lookup.

### Tests
- Added editor tests for build profile mapping/validation, demo-mode `GodMode` guards, and telemetry file output gating.
- Added leaderboard smoke scripts for API flow and security smoke checks (`invalid signature`, `duplicate submit`).

### Security
- Added documented leaderboard threat model for Steam demo scope (`Docs/LEADERBOARD_THREAT_MODEL.md`).
- Added per-player submit/start throttling in backend (in addition to per-IP API limits).

### Release
- Bumped application version to `0.7.2`.
- Updated test sandbox version fallback to `0.7.2`.

## 0.7.1 - 2026-03-01

### Gameplay
- Reduced enemy head knockback strength significantly to avoid over-punishing bounce responses while keeping anti-head-stomp behavior.

### Visual Readability
- Added environment readability guardrails (`RuntimeVisualReadabilityStabilizer`) for ambient intensity, reflection intensity, fog color luminance, and directional light/shadow strength to improve silhouette separation.

### Combat Feedback
- Synced melee hit feedback timing to damage popups by applying hit SFX after damage resolution and batching hit-stop/camera-shake in a frame-locked feedback queue.

### Profiling
- Added runtime performance summary capture (`RuntimePerformanceSummary`) with JSONL export for `avg FPS`, `1% low`, `p95/p99`, CPU main/render, GC alloc, draw calls, and setpass counts.

### Release
- Bumped application version to `0.7.1`.
- Updated test sandbox version fallback to `0.7.1`.

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
