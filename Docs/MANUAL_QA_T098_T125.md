# Manual QA Sheet: T-098 + T-125 + T-128

Data:  
Tester:
Commit SHA:
Build:

## T-098 Ustawienia PC: matrix test

Cel: potwierdzic brak soft-lockow UI i poprawna persystencje ustawien.

Wymagania:
- build demo uruchomiony lokalnie (`Builds/Demo/Necroforge_Demo.exe`)
- testujemy tylko:
  - rozdzielczosc: `1280x720`, `1920x1080`, `2560x1440` (lub najblizsze dostepne)
  - window mode: `Windowed`, `Borderless Fullscreen`
  - quality: `Low`, `Medium`, `High`

## Kryteria PASS (dla kazdej kombinacji)
- UI pozostaje responsywne.
- brak freeze/black screen po zmianie.
- po restarcie gry ustawienia pozostaja zapisane.

## Wyniki matrix (18 przypadkow)

| # | Rozdzielczosc | Window mode | Quality | Zastosowalo sie natychmiast (Y/N) | Persist po restarcie (Y/N) | Uwagi |
|---|---|---|---|---|---|---|
| 1 | 1280x720 | Windowed | Low |  |  |  |
| 2 | 1280x720 | Windowed | Medium |  |  |  |
| 3 | 1280x720 | Windowed | High |  |  |  |
| 4 | 1280x720 | Borderless | Low |  |  |  |
| 5 | 1280x720 | Borderless | Medium |  |  |  |
| 6 | 1280x720 | Borderless | High |  |  |  |
| 7 | 1920x1080 | Windowed | Low |  |  |  |
| 8 | 1920x1080 | Windowed | Medium |  |  |  |
| 9 | 1920x1080 | Windowed | High |  |  |  |
| 10 | 1920x1080 | Borderless | Low |  |  |  |
| 11 | 1920x1080 | Borderless | Medium |  |  |  |
| 12 | 1920x1080 | Borderless | High |  |  |  |
| 13 | 2560x1440 | Windowed | Low |  |  |  |
| 14 | 2560x1440 | Windowed | Medium |  |  |  |
| 15 | 2560x1440 | Windowed | High |  |  |  |
| 16 | 2560x1440 | Borderless | Low |  |  |  |
| 17 | 2560x1440 | Borderless | Medium |  |  |  |
| 18 | 2560x1440 | Borderless | High |  |  |  |

Wynik T-098: PASS / FAIL

## T-125 Legal links w buildzie demo

Cel: zweryfikowac widocznosc i dzialanie linkow legal/docs z menu glownego (hostowane URL subdomeny).

Sprawdz:
- Privacy
- EULA
- Third Party Licenses

## Kryteria PASS
- przyciski sa widoczne i klikalne.
- kazdy link otwiera sie poprawnie pod URL:
  - `/legal/privacy-policy.html`
  - `/legal/eula.html`
  - `/legal/third-party-licenses.html`
- brak bledow blokujacych UI po powrocie do gry.

## Wyniki

| Link | Widoczny (Y/N) | Otwiera sie poprawnie (Y/N) | Uwagi |
|---|---|---|---|
| Privacy |  |  |  |
| EULA |  |  |  |
| Third Party Licenses |  |  |  |

Wynik T-125: PASS / FAIL

## T-128 Legal URL checks: HTTPS + redirect + 404

Cel: potwierdzic, ze legal URL dzialaja poprawnie przez HTTPS oraz maja poprawne zachowanie redirect z HTTP.

Uruchom:

```powershell
.\Tools\leaderboard\legal_links_check.ps1 -BaseUrl "https://<twoj-host>"
```

## Kryteria PASS
- wszystkie 3 strony zwracaja 2xx po HTTPS.
- wejscie przez HTTP przekierowuje do HTTPS.
- brak 404 dla kazdego z 3 URL.

## Wyniki

| URL | HTTPS 2xx (Y/N) | HTTP -> HTTPS redirect (Y/N) | 404 brak (Y/N) | Uwagi |
|---|---|---|---|---|
| /legal/privacy-policy.html |  |  |  |  |
| /legal/eula.html |  |  |  |  |
| /legal/third-party-licenses.html |  |  |  |  |

Wynik T-128: PASS / FAIL

## Podsumowanie

- T-098: PASS / FAIL
- T-125: PASS / FAIL
- T-128: PASS / FAIL
- Blokery:
