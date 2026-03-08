# Release Dry-Run Report (2026-03-08)

Status: FAIL/BLOCKED  
Scope: T-144 "finalny dry-run release z checklisty"

## Kontekst

- Repo commit: `9563385`
- Unity: `6000.3.8f1`
- Host: `Windows 11`
- Log dry-runa: `Temp/release_dry_run_2026-03-08.log`

## Uruchomiona komenda

```powershell
"C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe" `
  -batchmode `
  -quit `
  -projectPath "C:\Users\Komputer\Simple" `
  -executeMethod GrassSim.Editor.BuildProfileBuildAutomation.BuildDemoWindows64 `
  -buildWithSmoke true `
  -logFile "C:\Users\Komputer\Simple\Temp\release_dry_run_2026-03-08.log"
```

## Wynik checklisty (Docs/BUILD_PROFILES.md)

1. Uzyc metody `BuildDemoWindows64` -> DONE (uruchomione).
2. Upewnic sie, ze smoke test przeszedl -> BLOCKED (build nie doszedl do smoke).
3. Sprawdzic `build_profile_info.txt` i `build_profile_smoke.txt` -> BLOCKED (brak artefaktow builda).

## Blokery

Batch build zatrzymal sie na kompilacji:

- `Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleUI.cs`: brak `UnityEngine.UI` i typow `Image`, `Graphic`, `ScrollRect`, `Slider`.
- `Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModulePhysics.cs`: brak `Rigidbody`.
- `Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModulePhysics2D.cs`: brak `Rigidbody2D`.
- `Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleAudio.cs`: brak `AudioSource`/`AudioMixer`.

Log konczy sie:

- `Scripts have compiler errors.`
- `Exiting without the bug reporter. Application will terminate with return code 1`

## Co zostalo zweryfikowane mimo blokera

- EditMode tests dla retry policy leaderboardu: PASS (`5/5`)  
  plik wynikowy: `Temp/leaderboard_editmode_tests.xml`
- Runtime hardening leaderboardu (T-140/T-141/T-142/T-143) jest w kodzie i na `main`.

## Nastepny krok

Naprawic compile blocker batch builda dla DOTween/Unity modules (UI/Physics/Physics2D/Audio), potem powtorzyc ten sam dry-run i dopiero wtedy zamknac release jako PASS.
