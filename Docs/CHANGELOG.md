# Changelog

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
