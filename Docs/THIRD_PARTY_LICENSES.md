# Third-Party Licenses (Steam Demo)

Last update: 2026-03-08

This file lists major third-party components used by the project.
Exact package versions are defined in:

- `Packages/manifest.json`
- `Packages/packages-lock.json`

## 1. Unity Engine and Unity Packages

The project is built with Unity and Unity packages distributed through Unity Package Manager.
Use of Unity components is subject to Unity terms and licenses.

## 2. TextMesh Pro and bundled font/license files

The repository includes TextMesh Pro assets and related font/license texts under `Assets/TextMesh Pro/...`.
Applicable font licenses include Open Font License and Apache License notices included with those assets.

## 3. Unity ML-Agents package

The project includes `com.unity.ml-agents` and its dependency graph through Unity Package Manager.
Refer to package metadata and upstream license notices provided by Unity.

## 4. Other Unity registry dependencies

Additional Unity dependencies (for example Cinemachine, Input System, Navigation, Timeline, URP, Test Framework, and transitive packages) are included via Unity Package Manager.
License terms for each package should be reviewed from package documentation/metadata.

## 5. Backend/runtime dependencies

Leaderboard backend and deployment stack may use third-party components (for example .NET runtime libraries, PostgreSQL, Docker images, reverse proxy components).
Those licenses are governed by their respective projects.

## 6. Updating this file

When dependencies change, update this file together with package manifest/lock changes and include any required attribution or license texts.

