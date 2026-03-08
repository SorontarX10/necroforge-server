# Release Dry-Run Report (2026-03-08, rerun)

Status: FAIL/BLOCKED  
Scope: follow-up rerun for `T-145`

## Context

- Repo branch: `main`
- Unity: `6000.3.8f1`
- Host: `Windows 11`
- Log: `Temp/release_dry_run_2026-03-08-rerun.log`

## Executed command

```powershell
"C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe" `
  -batchmode `
  -quit `
  -projectPath "C:\Users\Komputer\Simple" `
  -executeMethod GrassSim.Editor.BuildProfileBuildAutomation.BuildDemoWindows64 `
  -buildWithSmoke true `
  -logFile "C:\Users\Komputer\Simple\Temp\release_dry_run_2026-03-08-rerun.log"
```

## Observed blocker (from log)

The rerun did not reach build execution. Unity got stuck in licensing/package-manager state:

- `The following packages were not registered because your license doesn't allow it.`
- `Registered 0 packages`
- repeated licensing reconnect loop:
  - `The connection with the Unity Licensing Client has been lost`
  - `com.unity.editor.headless was not found`

This means current local batch environment cannot resolve Unity packages, so any compile/build validation is blocked before normal build pipeline steps.

## Impact on T-145

- DOTween module marker fix is present in repo (`#if false` in UI/Audio/Physics/Physics2D modules).
- `T-145` cannot be confirmed as PASS by local batch dry-run until Unity licensing/headless environment is fixed.

## Next step

Run dry-run again after repairing local Unity licensing/headless setup (or validate in CI runner with healthy Unity license) and then update release report/checklist with PASS or final failure reason.
