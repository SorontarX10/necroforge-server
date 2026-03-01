# Plan Wizualny 0.6.1+

## Cel i KPI

Cel: podnieść czytelność walki i spójność obrazu bez utraty płynności.

Twarde KPI:
- `>= 60 FPS stable` na docelowym średnim PC (1080p, preset domyślny).
- `1% low >= 45 FPS` w scenariuszu walki (MainScene, wysoka gęstość wrogów).
- Czytelność: gracz, zombie, elity, telegraph i trafienia są rozpoznawalne w ruchu przy domyślnym fog/lighting.

## Baseline i pomiary

## Metodyka
1. Scena: `Assets/Scenes/MainScene.unity`.
2. Czas pomiaru: 3 minuty aktywnej walki.
3. Przypadki:
- baseline (obecny build),
- high-density (wysoki spawn),
- boss readability.
4. Narzędzia:
- Unity Profiler (CPU/GPU/Render/Memory),
- Frame Debugger (overdraw i kolejność post-process),
- liczniki runtime (czas klatki, 1% low).

## Raport bazowy (do uzupełnienia przy wdrożeniu)
- Średni FPS:
- 1% low:
- Średni frametime (ms):
- Najdroższe etapy GPU:
- Najdroższe etapy CPU:
- Najczęstsze problemy czytelności:

## Milestone 0.6.1 (czytelność + stabilność obrazu)

## 1. Czytelność postaci i wrogów (lighting/fog/exposure)
- Opis: ustabilizować ekspozycję i kontrast sylwetek gracza/zombie, aby nie ginęły w mroku ani nie przepalały się przy emissive.
- Systemy/pliki:
- `Assets/Scenes/MainScene.unity`
- `Assets/Settings/PC_RPAsset.asset`
- global volumes/post-process profile używane przez MainScene
- DoD:
- gracz i zombie widoczni na tle terenu w 3 zakresach odległości: blisko/średnio/daleko,
- brak „czarnych sylwetek” przy aktywnym fog.
- Test:
- szybki test manualny MainScene + zrzuty porównawcze,
- 60 s obrotu kamery w obszarach ciemnych/jasnych.
- Metryka sukcesu:
- brak krytycznych utrat sylwetki w 95% testowanych kadrów.

## 2. Spójność materiałów i emissive
- Opis: wyrównać intensywność emissive (oczy, aury, efekty statusów), aby nie dominowały kadru i nie migały.
- Systemy/pliki:
- materiały VFX przeciwników i efektów reliców,
- skrypty emissive/runtime VFX (np. kontrolery oczu/aur).
- DoD:
- brak skoków jasności przy spawn/trigger,
- kolory emissive zgodne z rarity i czytelne, ale nie prześwietlone.
- Test:
- sekwencja spawn/kill elit + boss,
- porównanie włącz/wyłącz post-process.
- Metryka sukcesu:
- maksymalna luminancja emissive nie powoduje clippingu HDR w standardowym ujęciu kamery.

## 3. Normalizacja kontrastu VFX obrażeń/statusów
- Opis: ujednolicić grubość, kolor i czas życia hit VFX/floating text, by komunikaty były widoczne i nie zasłaniały walki.
- Systemy/pliki:
- system floating text,
- prefab VFX obrażeń/statusów,
- relewantne skrypty relic runtime.
- DoD:
- trafienia normal/crit/elite/boss rozróżnialne wizualnie,
- teksty nie nakładają się masowo w centrum ekranu.
- Test:
- test 100 trafień w krótkim czasie,
- walidacja legendarnych efektów proc.
- Metryka sukcesu:
- >= 90% komunikatów trafień czytelnych na nagraniu 1080p bez stop-klatki.

## 4. Stabilność post-process (bez black crush i przepaleń)
- Opis: dostroić bloom/tonemapping/contrast tak, żeby obraz nie wpadał w skrajności.
- Systemy/pliki:
- profile post-process/volumes scen gameplay.
- DoD:
- brak „black crush” postaci/wrogów,
- brak przepalonych białych plam przy efektach.
- Test:
- przejście przez scenę w 3 porach ekspozycji (ciemno, neutralnie, jasno),
- kontrola histogramu kadrów testowych.
- Metryka sukcesu:
- wartości cieni i świateł mieszczą się w założonych widełkach histogramu testowego.

## Milestone 0.6.2 (ruch + telegraph + kolorystyka)

## 1. Camera feel i płynność animacji
- Opis: dopracować damping, response i timing animacji ataku względem okna obrażeń.
- Systemy/pliki:
- kontrolery kamery,
- animatory player/enemy,
- eventy uderzeń.
- DoD:
- brak snappingu kierunku,
- brak opóźnień percepcyjnych między animacją, VFX i damage.
- Test:
- test ruchu 360 + chain ataków,
- test porównawczy frame-by-frame.
- Metryka sukcesu:
- różnica event hit vs wizualny slash <= 1 frame.

## 2. Telegraph ataków elit/bossa
- Opis: dodać czytelny anticipation/recovery i jednoznaczne sygnały obszaru zagrożenia.
- Systemy/pliki:
- kontrolery AI elit/bossa,
- markery telegraph i VFX ostrzegawcze.
- DoD:
- ataki ciężkie są przewidywalne bez czytania UI,
- recovery state jest widoczny po ataku.
- Test:
- 20 prób uniku na każdy typ ciężkiego ataku.
- Metryka sukcesu:
- wzrost skuteczności uniku przez graczy testowych o min. 20% vs baseline.

## 3. Spójność kolorów rarity (aury/sztandary/kręgi)
- Opis: wszystkie stałe efekty reliców mają konsekwentne mapowanie kolorów rarity.
- Systemy/pliki:
- skrypty relic effect runtime,
- materiały i prefaby aura/banner/circle.
- DoD:
- brak białych placeholderów,
- kolor efektu odpowiada rarity we wszystkich przypadkach.
- Test:
- przegląd całego katalogu reliców w `TestScene`.
- Metryka sukcesu:
- 100% efektów rarity przechodzi checklistę zgodności kolorystycznej.

## Milestone 0.7.0 (świat + final polish + profiling)

## 1. Światło środowiskowe i art pass map
- Opis: drugi pass środowiska (kontrast planów, gradacja terenu, tło i mgła).
- Systemy/pliki:
- chunk terrain materiały i oświetlenie scen.
- DoD:
- lepsza separacja planów (foreground/mid/background),
- wyższa czytelność nawigacji.
- Test:
- test eksploracji i orientacji przestrzennej.
- Metryka sukcesu:
- skrócenie czasu odnalezienia celów mapy w testach UX.

## 2. Final polish VFX + audio-visual sync
- Opis: finalne domknięcie timingów i intensywności efektów (hit-stop, shake, trails, impact).
- Systemy/pliki:
- combat VFX/SFX i kontrolery kamery.
- DoD:
- każdy ciężki hit ma czytelny, spójny feedback.
- Test:
- test regresji trafień normal/crit/elite/boss.
- Metryka sukcesu:
- brak krytycznych rozjazdów audio/VFX/hit event.

## 3. Profiling pass CPU/GPU pod finalny budżet
- Opis: końcowa optymalizacja pod KPI 60/45.
- Systemy/pliki:
- najdroższe systemy wskazane profilerem.
- DoD:
- KPI wydajności spełnione na buildzie release candidate.
- Test:
- powtórzenie pełnej metodyki baseline.
- Metryka sukcesu:
- spełnione wszystkie KPI wydajnościowe.

## Testy akceptacyjne (globalne)

1. MainScene readability smoke:
- widoczność gracza i zombie w ruchu przez 5 minut walki.
2. TestScene relic pass:
- wszystkie aktywne efekty reliców czytelne i bez placeholderów.
3. Input/UI smoke:
- pełna obsługa KBM bez utraty responsywności UI.
4. Performance gate:
- KPI `60 FPS stable`, `1% low >= 45 FPS`.

## Ryzyka i mitigacje

1. Ryzyko: nadmiar VFX obniży czytelność i FPS.
- Mitigacja: limity intensywności, budżet proc per frame, test 1% low po każdym większym VFX pass.

2. Ryzyko: tuning post-process pogorszy odbiór na części monitorów.
- Mitigacja: porównanie presetów gamma/brightness i szybki fallback profile.

3. Ryzyko: poprawki lokalne bez spójności globalnej.
- Mitigacja: jedna tabela mapowania rarity -> kolor -> materiał i checklista przeglądu całego katalogu reliców.

