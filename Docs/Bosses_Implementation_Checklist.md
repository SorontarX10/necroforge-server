# Bosses Implementation Checklist

## 0. Założenia
- [ ] Nie dodajemy nowych animacji (używamy tylko animacji chodzenia).
- [ ] Czytelność ataków i stanów bossa robimy przez telegraphy, VFX, SFX, kolorystykę i timing.

## 1. Jak przygotować prefaby (najważniejsze)
- [ ] Utwórz 4 dedykowane prefaby bossów: `Boss_Zombie`, `Boss_Quick`, `Boss_Tank`, `Boss_Dog` (najlepiej jako warianty istniejących enemy prefabów).
- [ ] Na każdym boss-prefabie dodaj komponent `BossEnemyController` (jeśli go nie ma).
- [ ] Na każdym boss-prefabie dodaj komponent `BossHealthBarUI` (żeby móc ustawić font/override z Inspectora).
- [ ] W scenie na obiekcie z `BossEncounterController` ustaw listę `bossPrefabsOverride` na te 4 boss-prefaby.

Dlaczego tak:
- Bez tego komponenty bossów będą dodawane runtime i nie ustawisz wygodnie referencji do assetów per prefab.

## 2. Checklist assetów i podpięć

### 2.1 VFX wejścia/teleportu/enrage
- [ ] `spawnVfxPrefab` (`BossEnemyController`): efekt wejścia bossa.
- [ ] `teleportVfxPrefab` (`BossEnemyController`): efekt pojawienia po teleportacji.
- [ ] `enrageVfxPrefab` (`BossEnemyController`): efekt aktywacji enrage.

Jak dodać:
- Otwórz prefab bossa -> `BossEnemyController` -> sekcja `Presentation (Optional Assets)` -> przypisz prefaby.

Jak powinno wyglądać:
- Spawn: szybki „burst” (0.3-0.8s), czytelny na tle mapy.
- Teleport: punktowy „blink” + krótki ślad.
- Enrage: wyraźny, agresywny akcent (np. czerwony impuls).

### 2.2 Dźwięki (SFX)
- [ ] `spawnSfx`
- [ ] `teleportSfx`
- [ ] `enrageSfx`
- [ ] `quickDashWarningSfx`
- [ ] `poisonWarningSfx`
- [ ] `rootWarningSfx`
- [ ] `dogAuraGrowlSfx`

Jak dodać:
- Ten sam komponent `BossEnemyController`, ta sama sekcja `Presentation (Optional Assets)`.

Jak powinno wyglądać:
- Ostrzeżenia (`Warning`) krótkie, wyraźne i różne tonalnie:
  - Dash: ostry, szybki.
  - Poison: „toksyczny”, bardziej miękki.
  - Root: cięższy, „klik/zakucie”.
- `dogAuraGrowlSfx`: nisko, klimatycznie, nie za głośno.

### 2.3 Telegraph marker (przed skillami)
- [ ] `telegraphMarkerPrefab` (opcjonalnie; jak brak, jest fallback cylinder).
- [ ] `telegraphMaterialOverride` (zalecane, żeby telegraph miał spójny shader i kolor).

Jak dodać:
- `BossEnemyController` -> `Presentation (Optional Assets)`.

Jak powinno wyglądać:
- Płaski znacznik na ziemi, wyraźny obrys, brak „ciężkich” particle.
- Czas życia krótki, czytelny:
  - Dash: szybki telegraph.
  - Poison/Root: minimalnie dłuższy.

### 2.4 Aura archetypów
- [ ] `auraPrefabOverride` (opcjonalnie; jak brak, jest fallback aura cylinder).
- [ ] `auraMaterialOverride` (zalecane).

Jak dodać:
- `BossEnemyController` -> `Presentation (Optional Assets)`.

Jak powinno wyglądać:
- Zombie: toksyczna zieleń.
- Quick: chłodny, szybki „smear”/światło.
- Tank: cięższa, ciemniejsza aura.
- Dog: nerwowa, drgająca, agresywna.

### 2.5 Font napisu „charging”
- [ ] `chargingFontOverride` na `BossHealthBarUI` ustaw na font Necroforge (opcjonalnie, bo jest auto-detekcja).

Jak dodać:
- Na prefabie bossa -> komponent `BossHealthBarUI` -> `Charging Label` -> `chargingFontOverride`.

Jak powinno wyglądać:
- Krótki, czytelny napis `charging`, wyraźny kontrast, bez dodatkowych ozdobników.

## 3. Checklist tuningu gameplay

### 3.1 Teleport i uczciwość walki
- [ ] `teleportCheckInterval`: 0.08-0.15
- [ ] `postTeleportGraceDuration`: 0.45-0.7
- [ ] `teleportTriggerDistance`: zależnie od mapy (zwykle 22-32)

Cel:
- Teleport ma „wracać bossa do walki”, ale bez tanich hitów natychmiast po blinku.

### 3.2 Fazy HP (75/50/25)
- [ ] Ustaw progi: `phase2Threshold`, `phase3Threshold`, `phase4Threshold`.
- [ ] Ustaw mnożniki faz (charge/attack/cooldown/move).

Cel:
- Faza 1: czytelne tempo.
- Faza 2-3: rosnące napięcie.
- Faza 4: najbardziej agresywna, ale nadal czytelna.

### 3.3 Enrage <30%
- [ ] `enrageThreshold`
- [ ] `enrageDamageMultiplier`
- [ ] `enrageMoveSpeedMultiplier`
- [ ] `enrageSkillCooldownMultiplier`
- [ ] `enrageEyeIntensityMultiplier`

Cel:
- Enrage ma być „wow”, ale nie ma łamać czytelności walki.

### 3.4 Telegraphy
- [ ] Dash: `quickDashTelegraphDuration` i `quickDashTelegraphRadius`.
- [ ] Poison: `poisonTelegraphDuration` i `poisonTelegraphRadius`.
- [ ] Root: `rootTelegraphDuration` i `rootTelegraphRadius`.

Cel:
- Gracz ma widzieć i rozumieć zagrożenie, a nie „zgadywać”.

## 4. Checklist jakości wizualnej
- [ ] VFX nie zasłaniają całej sylwetki bossa.
- [ ] Telegraph jest widoczny na jasnym i ciemnym podłożu.
- [ ] Aura nie „migocze” agresywnie (brak stroboskopu).
- [ ] SFX warning nie zagłuszają reszty miksu.
- [ ] Czerwone oczy bossa są widoczne, ale nie przepalone.

## 5. Checklist testów w grze
- [ ] Boss pojawia się poprawnie w 5 i 10 minucie.
- [ ] Losuje się różny archetyp bossa.
- [ ] W fazie charging boss stoi i obraca się do gracza.
- [ ] W fazie attacking boss wraca do normalnej agresji.
- [ ] Teleport działa, gdy gracz ucieknie za daleko.
- [ ] Po teleportacji boss nie zadaje obrażeń przez `postTeleportGraceDuration`.
- [ ] Quick dash ma telegraph przed ruchem.
- [ ] Poison/root mają telegraph przed efektem.
- [ ] Enrage aktywuje się przy niskim HP i zmienia tempo/siłę.
- [ ] Pasek HP bossa i napis `charging` działają poprawnie.

## 6. Mapa pól (szybka referencja)
- `Assets/Scripts/Bosses/BossEnemyController.cs`
  - `Teleport`
  - `Attack Cycle`
  - `Phases (HP %)`
  - `Enrage`
  - `Telegraphs`
  - `Presentation (Optional Assets)`
  - `Presentation Fallback`
- `Assets/Scripts/Bosses/BossHealthBarUI.cs`
  - `HUD Template`
  - `Charging Label`
