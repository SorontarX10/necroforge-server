# Plan Funkcji Do Wdrozenia Przed Wydaniem Steam Demo

Status: Draft  
Data: 2026-03-02  
Zakres: Demo + fundament pod pelna wersje (run-based loop)

## 1. Ustalenia produktowe

- Demo i pelna wersja: model run-based loop (brak metaprogression jako wymogu release blocker).
- Cheaty/debug: dostepne tylko dla dev buildow.
- Telemetria: tylko dla devow (lokalne logi diagnostyczne, brak produkcyjnego trackingu gracza).
- Gamepad support: poza zakresem obecnego wydania demo.
- Priorytet krytyczny: integralnosc leaderboardu online (anticheat skupiony na ochronie rankingu).

## 2. Cele wydania demo

- Stabilny, powtarzalny build demo do dystrybucji na Steam.
- Brak widocznych opcji developerskich dla gracza.
- Dzialajacy leaderboard online z podstawowym anticheat.
- Przewidywalny pipeline release + QA gate przed uploadem buildu.

## 3. Backlog funkcji (priorytety)

| ID | Funkcja | Priorytet | Efekt biznesowy | Szacunek |
|---|---|---|---|---|
| F-01 | Build Profiles + Feature Flags | P0 | Jednoznaczne oddzielenie Demo/Dev/Release | 2-3 dni |
| F-02 | Zabezpieczenie cheatow (dev-only) | P0 | Brak cheatow w buildzie dla graczy | 1-2 dni |
| F-03 | Telemetria tylko dev | P0 | Brak ryzyka privacy/consent w demo | 1-2 dni |
| F-04 | Online Leaderboard (MVP) | P0 | Podstawowa rywalizacja i retencja | 4-6 dni |
| F-05 | Anticheat leaderboardu (MVP) | P0 | Ochrona rankingu przed prostymi exploitami | 4-7 dni |
| F-06 | Naprawa lifecycle scen (Loading/Game) | P1 | Poprawne metryki i mniej edge-case bugow | 1-2 dni |
| F-07 | Ustawienia PC minimum release | P1 | Lepszy UX i mniej skarg po premierze | 2-3 dni |
| F-08 | Integracja Steam (bootstrap) | P1 | Gotowosc do publikacji i API platformowe | 2-4 dni |
| F-09 | CI/CD + Release checklist | P1 | Mniej regresji i szybsze wydania | 2-3 dni |
| F-10 | Legal + docs release | P1 | Gotowosc formalna strony i buildu | 1-2 dni |
| F-11 | Domkniecie TODO gameplay/UI | P2 | Polerka i mniej "unfinished" sygnalow | 1-2 dni |

## 4. Szczegolowy plan funkcji

## F-01 Build Profiles + Feature Flags (P0)

Cel: centralne sterowanie tym, co jest wlaczone w Dev, QA, Demo i Release.

Zakres wdrozenia:
- Dodac `BuildProfile` (np. ScriptableObject lub statyczny config) z polami:
  - `isDevelopmentToolsEnabled`
  - `isLocalTelemetryEnabled`
  - `isOnlineLeaderboardEnabled`
  - `isAnticheatStrictMode`
- Dodac compile symbols na Standalone:
  - `BUILD_DEMO`
  - `BUILD_DEVTOOLS`
  - `BUILD_INTERNAL_QA`
- Dodac prosty validator uruchamiany przed buildem:
  - fail, jesli `BUILD_DEMO` i jednoczesnie `BUILD_DEVTOOLS`.
  - fail, jesli `BUILD_DEMO` i telemetry nie jest `OFF`.

Kryteria akceptacji:
- Da sie zbudowac 2 rozne artefakty (Dev i Demo) z innym zachowaniem funkcji.
- W logu startowym build wypisuje aktywny profil i flagi.

Testy:
- EditMode: test mapowania profilu -> flagi runtime.
- Build smoke: skrypt, ktory odrzuca niedozwolone kombinacje.

## F-02 Zabezpieczenie cheatow (dev-only) (P0)

Cel: opcje typu `God Mode` nie moga byc widoczne ani aktywowalne w Demo/Release.

Zakres wdrozenia:
- Przebudowac `OptionsMenuController`:
  - usunac dynamiczne tworzenie `GodMode` toggle dla buildow nie-dev.
  - przeniesc cheaty do osobnego panelu debug.
- W `GameSettings`:
  - `SetGodMode` aktywne tylko gdy `BuildProfile.isDevelopmentToolsEnabled == true`.
  - ignorowac `opt_godmode` z `PlayerPrefs` w Demo/Release.
- Dodac runtime guard:
  - jesli build nie-dev i wykryto `GodMode=true`, wymusic `false` i warning do logu.

Kryteria akceptacji:
- W Demo nie ma zadnego UI ani hotkeya do cheatow.
- Modyfikacja `PlayerPrefs` nie wlacza GodMode w Demo.

Testy:
- PlayMode: odczyt `PlayerPrefs opt_godmode=1` nie aktywuje cheatow w Demo.
- UI smoke: brak obiektu `GodMode` w menu opcji dla Demo.

## F-03 Telemetria tylko dev (P0)

Cel: telemetry i diagnostyka zapisuja dane tylko w buildach dev/internal.

Zakres wdrozenia:
- Dla:
  - `GameplayTelemetryRecorder`
  - `RuntimePerformanceSummary`
  - `RuntimeHitchDiagnostics`
- Wprowadzic warunek bootstrap:
  - nie tworzyc singletonu w Demo/Release.
- Dodac `TelemetryMode`:
  - `OFF` (Demo/Release)
  - `DEV_LOCAL` (Dev/Internal)
- Usunac wszelkie automatyczne mirroringi do katalogow projektowych poza dev.

Kryteria akceptacji:
- Demo nie tworzy plikow telemetry/perf/hitch w `persistentDataPath`.
- Dev build zachowuje obecna diagnostyke lokalna.

Testy:
- PlayMode: asercja braku plikow telemetry po 5-10 min gry w Demo.
- Dev regression: logi nadal powstaja.

## F-04 Online Leaderboard (MVP) (P0)

Cel: dostarczyc globalny ranking run-based z paginacja i filtrowaniem.

Zakres funkcjonalny:
- Typ rankingu MVP:
  - `global_all_time` (score)
  - `global_weekly` (score)
- Pola rekordu:
  - `player_id` (SteamID lub wewnetrzne)
  - `display_name`
  - `score`
  - `run_duration_sec`
  - `kills`
  - `build_version`
  - `created_at`
  - `validation_state`
- API backend:
  - `POST /runs/start` (tworzy sesje runu, zwraca nonce)
  - `POST /runs/submit` (wysyla wynik + dane walidacyjne)
  - `GET /leaderboard?season=...&page=...`
  - `GET /leaderboard/me`
- Unity client:
  - submit po zakonczeniu runu.
  - ekran rankingu z top N + pozycja gracza.
  - fallback: "Leaderboard chwilowo niedostepny".

Kryteria akceptacji:
- Wyniki pojawiaja sie online w ciagu <= 5s po submit.
- Brak crashy przy braku polaczenia.

Testy:
- Integracyjne API: submit + odczyt pozycji.
- PlayMode: scenariusz online/offline.

## F-05 Anticheat leaderboardu (MVP) (P0)

Cel: utrudnic falszowanie wynikow i chronic ranking.

Zakres (MVP, server-assisted):
- Threat model:
  - edycja pamieci (score/kills/time)
  - speedhack/time-scale abuse
  - reczne wywolanie submit API
  - replay submit tego samego runu
- Mechanizmy:
  - sesja runu z nonce z backendu (`/runs/start`).
  - hash-chain eventow runu po stronie klienta.
  - podpis payloadu runu (HMAC kluczem sesji jednorazowej).
  - serwerowa walidacja progow:
    - min/max score per minute
    - kill rate sanity
    - minimalny czas runu do submitu
    - zgodnosc build_version
  - anti-replay:
    - unikalny `run_id`, jednorazowy submit.
  - rate limiting:
    - na konto i IP.
  - flagowanie rekordow:
    - `accepted`, `shadow_banned`, `rejected`, `manual_review`.

Kryteria akceptacji:
- Prosty forged submit bez sesji runu jest odrzucany.
- Duplicate submit tego samego `run_id` jest odrzucany.
- Podejrzane wyniki nie trafiaja do publicznego topu.

Testy:
- Integracyjne: brute-force payload mutation.
- Security smoke: replay attack / stale nonce / invalid signature.

## F-06 Naprawa lifecycle scen (Loading/Game) (P1)

Cel: poprawny aktywny scene context i czystsza nawigacja.

Zakres wdrozenia:
- Po zaladowaniu `Game`:
  - ustawic `Game` jako scene aktywna.
  - opcjonalnie unload `Loading` po fade out.
- Zweryfikowac flow:
  - MainMenu -> Loading -> Game
  - GameOver -> Loading -> Game
  - Pause -> MainMenu

Kryteria akceptacji:
- Runtime metrics raportuja scene `Game` podczas rozgrywki.
- Brak podwojnych singletonow po kilku restartach runu.

Testy:
- PlayMode smoke przez 5 kolejnych restartow runu.

## F-07 Ustawienia PC minimum release (P1)

Cel: podniesc jakosc UX bez rozbijania scope.

Zakres MVP:
- Dodac opcje:
  - rozdzielczosc
  - window mode (windowed/fullscreen borderless)
  - v-sync on/off
  - fps cap (30/60/120/uncapped)
  - quality preset (Low/Medium/High)
- Naprawic defaulty:
  - sensowna startowa rozdzielczosc (native lub 1920x1080 fallback)
  - `resizableWindow = true` dla PC

Kryteria akceptacji:
- Zmiany opcji stosuja sie natychmiast i persistuja.
- Brak soft-lockow UI po zmianie display mode.

Testy:
- Manual matrix: 3 rozdzielczosci x 2 window modes x 2 quality preset.

## F-08 Integracja Steam (bootstrap) (P1)

Cel: gotowosc techniczna buildu pod publikacje demo.

Zakres:
- Dodac warstwe `IPlatformServices`:
  - `GetPlayerId()`
  - `GetPlayerName()`
  - `OpenOverlayToLeaderboard()`
- Implementacja `SteamPlatformServices` + fallback `NullPlatformServices`.
- Inicjalizacja platformy na starcie gry.
- Przechwycenie awarii inicjalizacji (fallback bez crasha).

Kryteria akceptacji:
- Build uruchamia sie poprawnie ze Steam i bez Steam (fallback).
- Leaderboard UI dziala przez wspolny interfejs platformowy.

Testy:
- Smoke lokalny z i bez klienta Steam.

## F-09 CI/CD + Release checklist (P1)

Cel: powtarzalny release process dla Demo.

Zakres:
- Dodac workflow:
  - build demo
  - smoke test scen
  - artifact upload
- Dodac release checklist markdown:
  - techniczna
  - gameplay QA
  - store metadata
  - legal
- Utrzymac obecny perf gate i dodac gate "no devtools in demo".

Kryteria akceptacji:
- Kazdy release demo przechodzi przez ten sam pipeline.
- Build z wlaczonym devtools nie moze przejsc jako demo artifact.

## F-10 Legal + docs release (P1)

Cel: domkniecie formalne i komunikacyjne wydania.

Zakres:
- Dodac pliki:
  - `Docs/PRIVACY.md` (telemetry dev-only, brak produkcyjnego trackingu)
  - `Docs/EULA.md`
  - `Docs/THIRD_PARTY_LICENSES.md`
- Dodac w menu:
  - linki do privacy/eula (otwierane lokalnie lub URL).

Kryteria akceptacji:
- Dokumenty istnieja i sa podlinkowane z menu.

## F-11 Domkniecie TODO gameplay/UI (P2)

Cel: usunac najbardziej widoczne sygnaly niedokonczonych funkcji.

Zakres:
- Uzupelnic TODO:
  - `HealthBarBinder`
  - `StaminaBarBinder`
  - `EnhancerPickup` (VFX/SFX)

Kryteria akceptacji:
- Brak runtime TODO-ow dotykajacych gracza w kluczowym loopie.

## 5. Kolejnosc wdrozenia (rekomendowana)

1. F-01 Build Profiles + flags  
2. F-02 Cheaty dev-only  
3. F-03 Telemetria dev-only  
4. F-06 Lifecycle scen  
5. F-04 Leaderboard online MVP  
6. F-05 Anticheat leaderboard MVP  
7. F-08 Integracja Steam bootstrap  
8. F-07 Ustawienia PC minimum  
9. F-09 CI/CD + checklist  
10. F-10 Legal/docs  
11. F-11 TODO polish

## 6. Harmonogram (propozycja od 2026-03-02)

## Tydzien 1 (2026-03-02 -> 2026-03-08)
- F-01, F-02, F-03, F-06
- Wynik: bezpieczny build demo bez dev features + poprawny flow scen

## Tydzien 2 (2026-03-09 -> 2026-03-15)
- F-04, F-05 (MVP backend + klient + walidacja)
- Wynik: dzialajacy online leaderboard z anticheat MVP

## Tydzien 3 (2026-03-16 -> 2026-03-22)
- F-08, F-07, F-09
- Wynik: integracja platformy + domkniete ustawienia + release pipeline

## Tydzien 4 (2026-03-23 -> 2026-03-29)
- F-10, F-11 + hardening i regression pass
- Wynik: release candidate demo

## 7. Definition of Done (Demo Candidate)

- Build Demo:
  - brak cheat UI i brak aktywnego GodMode.
  - brak telemetry output poza dev buildami.
- Core flow:
  - MainMenu -> Loading -> Game dziala stabilnie przez 10 restartow runu.
- Leaderboard:
  - submit/refresh/ranking dzialaja, bledy sie degraduja lagodnie.
- Anticheat:
  - forged submit + replay submit odrzucane.
  - podejrzane rekordy nie sa publiczne.
- Ops:
  - checklist release odhaczona.
  - docs legalne obecne.

## 8. Ryzyka i mitigacje

- Ryzyko: anticheat MVP za slaby wobec zaawansowanych cheatow.
  - Mitigacja: scoped goal = ochrona leaderboardu, nie pelna ochrona klienta.
- Ryzyko: opoznienie backendu leaderboardu.
  - Mitigacja: dostarczyc API MVP (global all-time) przed weekly/friends.
- Ryzyko: regresje przez flagi buildowe.
  - Mitigacja: testy profili + gate CI "demo cannot enable devtools".

## 9. Po demo (next)

- Sezony leaderboardu + reward cosmetics.
- Rozszerzony anticheat (anomaly model + manual moderation UI).
- Friends leaderboard (jesli scope platformowy bedzie potrzebny).

## 10. Operacyjna Lista Taskow (do usuwania po ukonczeniu)

Zasada pracy:
- Ta sekcja jest "single source of truth" na najblizsze wdrozenia.
- Po ukonczeniu tasku usuwamy linie z taskiem z tej listy.
- Jesli task jest za duzy, dzielimy go na mniejsze i dopiero wtedy realizujemy.

### F-01 Build Profiles + Feature Flags (P0)

Zakonczone 2026-03-03.

### F-02 Zabezpieczenie cheatow (dev-only) (P0)

Zakonczone 2026-03-03.

### F-03 Telemetria tylko dev (P0)

Zakonczone 2026-03-03.

### F-04 Online Leaderboard (MVP) (P0)

- [ ] T-051 Dodac testy integracyjne API: start -> submit -> odczyt rankingu.

Zakonczone 2026-03-07:
- T-049 Dodac UI rankingu: top N, moja pozycja, stan ladowania, stan bledu.
- T-050 Dodac fallback UX offline: komunikat i przycisk "Sprobuj ponownie".

### F-05 Anticheat leaderboardu (MVP) (P0)

- [ ] T-062 Dodac hash-chain eventow runu po stronie klienta.
- [ ] T-069 Dodac testy security smoke: stale nonce, invalid signature, replay submit.
- [ ] T-070 Dodac panel/komendy administracyjne do recznego review flagowanych runow.

Zakonczone 2026-03-07:
- T-060 Spisac finalny threat model (score spoof, speedhack, replay, API abuse).
- T-061 Dodac sesyjny nonce i TTL dla runu (wymagane do submitu).
- T-063 Dodac podpis payloadu runu (HMAC oparty o klucz sesji jednorazowej).
- T-064 Dodac walidacje server-side: score/min, kill-rate, min runtime, build_version.
- T-065 Dodac anti-replay: jeden `run_id` = max jeden accepted submit.
- T-066 Dodac rate limiting per konto i per IP dla `runs/start` i `runs/submit`.
- T-067 Dodac stany moderacji wyniku: `accepted`, `shadow_banned`, `rejected`, `manual_review`.
- T-068 Dodac "soft quarantine" podejrzanych wynikow (nie trafiaja do publicznego topu).

### F-06 Naprawa lifecycle scen (Loading/Game) (P1)

- [ ] T-080 Ustawic `Game` jako scene aktywna po `LoadSceneAsync(..., Additive)`.
- [ ] T-081 Dodac unload `Loading` po zakonczeniu fade i gotowosci `Game`.
- [ ] T-082 Zweryfikowac, ze singletony `DontDestroyOnLoad` nie duplikuja sie po restarcie runu.
- [ ] T-083 Dodac test flow: MainMenu -> Loading -> Game -> MainMenu -> ponownie Game.
- [ ] T-084 Dodac test 10 restartow runu bez leakow i bez utraty input/audio.
- [ ] T-085 Potwierdzic, ze runtime summary raportuje scene `Game` podczas rozgrywki.

### F-07 Ustawienia PC minimum release (P1)

- [ ] T-090 Dodac dropdown rozdzielczosci oparty o `Screen.resolutions`.
- [ ] T-091 Dodac wybor `Windowed / Borderless Fullscreen`.
- [ ] T-092 Dodac toggle VSync.
- [ ] T-093 Dodac FPS cap (30/60/120/uncapped).
- [ ] T-094 Dodac quality preset selector (Low/Medium/High).
- [ ] T-095 Dodac trwale zapisywanie i ladowanie nowych opcji.
- [ ] T-096 Ustawic sensowne defaulty dla pierwszego startu.
- [ ] T-097 Ustawic `resizableWindow = true` dla targetu PC.
- [ ] T-098 Dodac test manualny matrix (rozdzielczosc x window mode x quality).

### F-08 Integracja Steam (bootstrap) (P1)

- [ ] T-100 Dodac abstrakcje `IPlatformServices` do projektu.
- [ ] T-101 Zaimplementowac `NullPlatformServices` (fallback bez Steam).
- [ ] T-102 Dodac implementacje Steam (`SteamPlatformServices`) z init/shutdown.
- [ ] T-103 Dodac bezpieczny fallback, gdy Steam init nie powiedzie sie.
- [ ] T-104 Podlaczyc `GetPlayerId` i `GetPlayerName` do leaderboard submit.
- [ ] T-105 Dodac `OpenOverlayToLeaderboard` i podpiac przycisk w UI.
- [ ] T-106 Dodac smoke test uruchomienia z aktywnym i nieaktywnym klientem Steam.

### F-09 CI/CD + Release checklist (P1)

- [ ] T-110 Dodac workflow CI: build demo artefaktu.
- [ ] T-111 Dodac workflow CI: smoke test uruchomieniowy scen.
- [ ] T-112 Dodac workflow CI: gate "demo build cannot include devtools".
- [ ] T-113 Dodac workflow CI: gate "telemetry mode must be OFF in demo".
- [ ] T-114 Zintegrowac obecny perf gate jako krok wymagany przed release.
- [ ] T-115 Dodac upload artefaktow build + logow testowych.
- [ ] T-116 Przygotowac `Docs/RELEASE_CHECKLIST_STEAM_DEMO.md`.
- [ ] T-117 Dodac sekcje rollback procedure i hotfix checklist do release checklist.

### F-10 Legal + docs release (P1)

- [ ] T-120 Przygotowac `Docs/PRIVACY.md` (stan faktyczny: telemetry dev-only).
- [ ] T-121 Przygotowac `Docs/EULA.md`.
- [ ] T-122 Przygotowac `Docs/THIRD_PARTY_LICENSES.md`.
- [ ] T-123 Dodac linki Privacy/EULA w menu glownym.
- [ ] T-124 Dodac fallback otwierania lokalnego pliku lub URL w zaleznosci od platformy.
- [ ] T-125 Zweryfikowac widocznosc i dzialanie linkow w buildzie demo.

### F-11 Domkniecie TODO gameplay/UI (P2)

- [ ] T-130 Dokonczyc implementacje `HealthBarBinder` (realne powiazanie z UI fill/slider).
- [ ] T-131 Dokonczyc implementacje `StaminaBarBinder` (realne powiazanie z UI fill/slider).
- [ ] T-132 Dodac brakujace VFX/SFX w `EnhancerPickup`.
- [ ] T-133 Dodac test/regresje dla pickupow enhancerow po zmianie.
- [ ] T-134 Usunac lub zamknac TODO komentarze powiazane z tymi elementami.

### Cross-cutting release hardening

- [ ] T-140 Dodac globalny `VERSION_LOCK` dla demo (zgodnosc klient/backend leaderboard).
- [ ] T-141 Dodac monitorowanie bledow HTTP leaderboardu w runtime logu (bez telemetry produkcyjnej).
- [ ] T-142 Dodac timeout policy i retry budget, zeby uniknac freeze UI przy slabym laczu.
- [ ] T-143 Dodac "graceful degradation": brak leaderboardu nie psuje core loop run-based.
- [ ] T-144 Przeprowadzic finalny dry-run release z checklisty i zarchiwizowac wynik w Docs.
