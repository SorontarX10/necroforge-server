# Privacy Policy (Steam Demo)

Last update: 2026-03-08

This policy describes how the Necroforge Steam Demo handles data.

## 1. Data we process

The demo can process gameplay and leaderboard data needed to run online ranking:

- `player_id` (platform ID or local fallback ID)
- `display_name`
- run result data (`score`, `kills`, `run_duration_sec`, `build_version`)
- anti-abuse metadata used for leaderboard validation
- service operational logs on the backend (for stability and security)

## 2. Local telemetry in production demo

Production demo builds are configured with telemetry mode `OFF`.
This means the demo build does not create local diagnostic telemetry/perf/hitch files intended for development.

## 3. Why data is used

Data is used to:

- accept or reject leaderboard submissions,
- display ranking and player position,
- protect leaderboard integrity against abuse,
- operate and troubleshoot backend service availability.

## 4. Sharing

We do not sell personal data.
Data is processed by infrastructure providers used to host backend services (for example VPS, DNS, TLS, and platform services).

## 5. Retention

Leaderboard and operational data may be retained as long as needed for service operation, abuse prevention, and debugging.
Retention windows may change during demo operation.

## 6. Your choices

If you do not want leaderboard-related processing, do not submit runs to online leaderboard features.

## 7. Contact

Project contact: `SorontarX10` (repository owner).
For privacy-related requests, use the project support/contact channel published with the demo/store page.

## 8. Changes

This policy can be updated.
The latest version is published in this repository and linked from the game menu.

