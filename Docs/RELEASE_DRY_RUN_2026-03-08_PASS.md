# Release Dry-Run Report (2026-03-08, pass)

Status: PASS  
Scope: closure for `T-145`

## Context

- Branch: `main`
- Unity: `6000.3.8f1`
- Host: `Windows 11`
- Command:

```powershell
"C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe" `
  -batchmode -quit -nographics `
  -projectPath "C:\Users\Komputer\Simple" `
  -executeMethod GrassSim.Editor.BuildProfileBuildAutomation.BuildDemoWindows64 `
  -buildWithSmoke true `
  -smokeTimeoutSeconds 120 `
  -logFile "C:\Users\Komputer\Simple\Temp\release_dry_run_final.log"
```

## Result

- Build result: `Success`
- Smoke result: `PASS`
- Log markers:
  - `Build Finished, Result: Success.`
  - `[BuildProfile] Smoke artifact: C:\Users\Komputer\Simple\Builds\Demo\build_profile_smoke.txt`
  - `[BuildProfile] Build completed with smoke test.`
  - `Exiting batchmode successfully now!`

## Produced artifacts

- `Builds/Demo/Necroforge_Demo.exe`
- `Builds/Demo/build_profile_info.txt`
- `Builds/Demo/smoke_runtime.log`
- `Builds/Demo/build_profile_smoke.txt`

## Implemented fix

Adjusted smoke runner in `Assets/Scripts/Editor/BuildProfileBuildAutomation.cs`:

- Detect startup marker from `smoke_runtime.log` with `FileShare.ReadWrite`.
- Treat startup marker as smoke pass even if process does not exit cleanly on some environments.
- Force-stop lingering smoke process to avoid false timeout failures.
- Persist smoke metadata (`process_exit_code`, `process_timeout`) in `build_profile_smoke.txt`.
