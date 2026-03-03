# Build Profiles (Demo / Dev / QA / Release)

Data: 2026-03-03

## Cel

Ta dokumentacja opisuje mechanizm profili builda i jednokomendowe budowanie artefaktow `Demo` oraz `Dev`.

## Profile i symbole

- `BUILD_DEMO` -> profil `Demo`
- `BUILD_DEVTOOLS` -> profil `Dev`
- `BUILD_INTERNAL_QA` -> profil `InternalQA`
- brak symboli profilu (poza Edytorem) -> profil `Release`

Uwagi:
- Symbole sa ustawiane automatycznie przez skrypt builda.
- Prebuild validator blokuje niedozwolone kombinacje symboli.

## Runtime log profilu

Przy starcie gry logowany jest wpis:
- `[BuildProfile] profile=... symbols=(...) flags=(...)`

Ten wpis jest uzywany m.in. przez smoke test po buildzie.

## Jedna komenda: Build Demo

Menu:
- `Tools/Build Profiles/Build Demo Win64`

CLI:
```powershell
"C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe" `
  -batchmode `
  -quit `
  -projectPath "C:\Users\Komputer\Simple" `
  -executeMethod GrassSim.Editor.BuildProfileBuildAutomation.BuildDemoWindows64 `
  -buildWithSmoke true
```

Domyslny output:
- `Builds/Demo/Necroforge_Demo.exe`

## Jedna komenda: Build Dev

Menu:
- `Tools/Build Profiles/Build Dev Win64`

CLI:
```powershell
"C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe" `
  -batchmode `
  -quit `
  -projectPath "C:\Users\Komputer\Simple" `
  -executeMethod GrassSim.Editor.BuildProfileBuildAutomation.BuildDevWindows64 `
  -buildWithSmoke true
```

Domyslny output:
- `Builds/Dev/Necroforge_Dev.exe`

## Parametry CLI

- `-buildOutput <sciezka>`: nadpisuje domyslna sciezke output.
- `-buildWithSmoke <true|false|1|0>`: wlacza/wyklacza smoke test.
- `-smokeTimeoutSeconds <int>`: timeout smoke testu (domyslnie `90`).

Przyklad:
```powershell
"C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe" `
  -batchmode `
  -quit `
  -projectPath "C:\Users\Komputer\Simple" `
  -executeMethod GrassSim.Editor.BuildProfileBuildAutomation.BuildDemoWindows64 `
  -buildOutput "C:\Builds\steam-demo\NecroforgeDemo.exe" `
  -buildWithSmoke true `
  -smokeTimeoutSeconds 120
```

## Artefakty po buildzie

W katalogu output builda zapisywane sa:
- `build_profile_info.txt` - profil, symbole, flagi, sciezka i rozmiar builda
- `smoke_runtime.log` - log uruchomienia smoke testu
- `build_profile_smoke.txt` - marker `[BuildProfile]` wyciagniety z logu runtime

## Walidacje prebuild

Build jest blokowany, jezeli:
- `BUILD_DEMO` i `BUILD_DEVTOOLS` sa aktywne jednoczesnie
- `BUILD_DEMO` nie mapuje sie na `TelemetryMode=Off`

## Szybki checklist przed release demo

- Uzyc metody `BuildDemoWindows64`.
- Upewnic sie, ze smoke test przeszedl.
- Sprawdzic `build_profile_info.txt` i `build_profile_smoke.txt`.
