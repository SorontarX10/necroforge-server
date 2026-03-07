# Leaderboard Threat Model (MVP)

Data: 2026-03-07
Zakres: demo Steam, ochrona rankingu online (nie pelna ochrona klienta).

## 1. Co chcemy chronic

- Publiczny ranking (uczciwa kolejnosc wynikow).
- Stabilnosc API (brak latwego spamu i DoS na endpointach runow).
- Integralnosc jednego runu (brak replay tego samego run_id).

## 2. Co zakladamy (MVP)

- Klient gry nie jest zaufany.
- Atakujacy moze modyfikowac pamiec procesu, wysylac reczne requesty i analizowac ruch.
- Celem MVP jest utrudnienie i szybkie filtrowanie prostych oszustw.

## 3. Scenariusze ataku i obrona

## A1: Forged submit bez sesji runu

- Ryzyko: ktos wysyla `POST /runs/submit` bez legalnego `run_id`.
- Obrona:
  - `run_id` musi istniec i byc otwarty.
  - `player_id` musi pasowac do sesji runu.
  - brak pasowania -> request odrzucony.

## A2: Replay submit tego samego runu

- Ryzyko: wielokrotne nabicie rankingu tym samym wynikiem.
- Obrona:
  - sesja ma `submitted_at`.
  - drugi submit tego samego `run_id` zwraca konflikt (409).

## A3: Podmiana payloadu (score/time/kills)

- Ryzyko: falszowanie danych w locie.
- Obrona:
  - klient podpisuje canonical payload HMAC kluczem sesji.
  - backend sprawdza podpis i stale-czasowe porownanie.
  - niepoprawny podpis -> wynik `rejected`.

## A4: Speedhack / nienaturalny wynik

- Ryzyko: legalnie podpisany, ale absurdalny wynik.
- Obrona:
  - walidacje server-side:
    - min runtime
    - score/min
    - kills/min
    - version lock
  - podejrzane runy trafiaja do `manual_review` lub `shadow_banned`.
  - tylko `accepted` jest widoczne publicznie.

## A5: Spam API

- Ryzyko: zalanie endpointow requestami.
- Obrona:
  - limit per IP (`runs/start`, `runs/submit`).
  - limit per player/minute po stronie bazy.

## 4. Luki, ktore zostaja w MVP

- Brak kernel-level anti-cheat (to nie jest scope demo).
- Brak hash-chain eventow po stronie klienta.
- Brak panelu moderatora do recznej obslugi flag.

## 5. Co dalej po demo

1. Dodac hash-chain eventow runu i walidacje po stronie serwera.
2. Dodac admin API/UI do review `manual_review`.
3. Dodac automatyczne reguly reputacji gracza/IP (progressive throttling).
