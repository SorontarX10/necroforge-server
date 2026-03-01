# Plan Wizualny 0.6.1+

## Cel i KPI

Cel: podniesc czytelnosc walki i spojnosci obrazu bez utraty plynnosci.

Twarde KPI:
- `>= 60 FPS stable` na docelowym srednim PC (1080p, preset domyslny).
- `1% low >= 45 FPS` w scenariuszu walki (MainScene, wysoka gestosc wrogow).
- Czytelnosc: gracz, zombie, elity, telegraph i trafienia sa rozpoznawalne w ruchu przy domyslnym fog/lighting.

## Baseline i pomiary

## Metodyka
1. Scena: `Assets/Scenes/MainScene.unity`.
2. Czas pomiaru: 3 minuty aktywnej walki.
3. Przypadki:
- baseline (obecny build),
- high-density (wysoki spawn),
- boss readability.
4. Narzedzia:
- Unity Profiler (CPU/GPU/Render/Memory),
- Frame Debugger (overdraw i kolejnosc post-process),
- liczniki runtime (czas klatki, 1% low).

## Raport bazowy (do uzupelnienia przy wdrozeniu)
- Sredni FPS:
- 1% low:
- Sredni frametime (ms):
- Najdrozsze etapy GPU:
- Najdrozsze etapy CPU:
- Najczestsze problemy czytelnosci:

## Milestone 0.6.1 (zamkniety)

Punkty milestone 0.6.1 zostaly wdrozone i usuniete z backlogu:
- runtime guardrails dla fog/exposure/post-process,
- normalizacja emissive (enemy/boss) z limitami intensywnosci i wygladzaniem,
- poprawa czytelnosci floating textow (clamp rozmiaru, jitter, stack offset, alfa),
- stabilizacja zachowania mgly pod czytelnosc sylwetek.

## Milestone 0.6.2 (zamkniety)

Punkty milestone 0.6.2 zostaly wdrozone i usuniete z backlogu:
- camera feel: wygładzenie wejścia kamery, pivot follow smoothing, minimal look-ahead i redukcja snappingu obrotu gracza.
- telegraph elit: anticipation/recovery w `HordeAISystem` z czytelnym markerem strefy zagrozenia.
- spojnosc rarity: ujednolicone tintowanie prefabow aura/banner/circle (`RelicVisualRarityTint`) i podpiecie pod standardy/kregi relicow.

## Milestone 0.7.0 (zamkniety)

Punkty milestone 0.7.0 zrealizowane i usuniete z backlogu:
- swiatlo srodowiskowe i art pass map: runtime guardrails dla ambient/reflection/fog color/directional lights dla lepszej separacji sylwetek i planow.
- final polish VFX + audio-visual sync: ramkowe kolejkowanie feedbacku trafien (hit-stop + shake) oraz synchronizacja hit SFX po aplikacji obrazen/floating text.
- profiling pass CPU/GPU pod finalny budzet: dodany runtime recorder podsumowania wydajnosci (`avg FPS`, `1% low`, `p95/p99`, CPU main/render, GC, draw/setpass) z eksportem JSONL do `persistentDataPath` i `results/perf`.

Otwarte punkty milestone 0.7.0: brak.

## Testy akceptacyjne (globalne)

1. MainScene readability smoke:
- widocznosc gracza i zombie w ruchu przez 5 minut walki.
2. TestScene relic pass:
- wszystkie aktywne efekty relicow czytelne i bez placeholderow.
3. Input/UI smoke:
- pelna obsluga KBM bez utraty responsywnosci UI.
4. Performance gate:
- KPI `60 FPS stable`, `1% low >= 45 FPS`.

## Ryzyka i mitigacje

1. Ryzyko: nadmiar VFX obnizy czytelnosc i FPS.
- Mitigacja: limity intensywnosci, budzet proc per frame, test 1% low po kazdym wiekszym VFX pass.

2. Ryzyko: tuning post-process pogorszy odbior na czesci monitorow.
- Mitigacja: porownanie presetow gamma/brightness i szybki fallback profile.

3. Ryzyko: poprawki lokalne bez spojnosci globalnej.
- Mitigacja: jedna tabela mapowania rarity -> kolor -> material i checklista przegladu calego katalogu relicow.
