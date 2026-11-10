# Raport: braki projektu (Client.Main, C#/MonoGame) względem oryginalnego klienta SourceMain5.2 (C++)

Data: 2026-11-01

## Metoda i zastrzeżenia

Analiza statyczna: struktura katalogów, liczba plików/linii, grep po słowach kluczowych, porównanie list pakietów
(`Receive*` w `WSclient.cpp` vs `[PacketHandler]` w `Networking/`). **Nie** uruchamiałem klienta ani nie sprawdzałem
działania w grze. Liczby to rząd wielkości, nie dokładny audyt. Pozycje oznaczone „(zweryfikować)" wymagają
sprawdzenia w runtime lub głębszego czytania kodu.

Skala:
- Oryginał: 605 plików w `SourceMain5.2/source`, ~283 tys. linii samych `.cpp`, 262 funkcje `Receive*` w `WSclient.cpp`.
- Port: 1303 pliki `.cs` w `Client.Main` (z czego 900 w `Objects/`, 135 w `Controls/`, 112 w `Worlds/`), 100 rejestracji `[PacketHandler]`.

Port celuje w dane Season 6/20 + serwer OpenMU, więc część braków to świadome zawężenie zakresu (np. Siege, GameGuard,
launcher). Poniżej oznaczam, co jest realną luką funkcjonalną.

## Priorytety (skrót)

| # | Obszar | Waga | Skala luki |
|---|--------|------|-----------|
| 1 | Logika eventów i map specjalnych (pliki `GM*.cpp`) | Wysoka | ~40 plików / ~45 tys. linii oryginału, w porcie głównie stuby światów |
| 2 | Wizualia skilli i efekty (particle/joint) | Wysoka | ~40 zarejestrowanych efektów skilli vs ~171 ID skilli w oryginale |
| 3 | Okna UI (gildia, znajomi/listy, MuHelper, Gens, Lucky, Harmony/Socket, eventowe HUD-y) | Wysoka | ~100 plików `NewUI*` vs ~50 klas kontrolek w porcie |
| 4 | Pokrycie pakietów sieciowych | Średnia/Wysoka | 100 handlerów vs 262 `Receive*` |
| 5 | Audio (muzyka map, formaty) | Średnia | BGM ustawione w 18 z 112 klas światów |
| 6 | Zachowania potworów/AI i specjalne animacje | Średnia | `MonsterObject.cs` 678 linii vs `ZzzAI.cpp` + akcje w `ZzzCharacter.cpp` (15 tys. linii) |
| 7 | Znane TODO w kodzie | Niska/Średnia | ~10 konkretnych miejsc |

---

## 1. Logika eventów i map specjalnych

Oryginał ma osobny moduł `GM*.cpp` na każdą mapę/event, z własnymi obiektami, skryptami, bossami i interakcjami:

`GM_Kanturu_1st/2nd/3rd` (do 1.9k linii każdy), `GM_Raklion` (2.9k), `GMCrywolf1st/2nd` (2.7k), `GMBattleCastle` (2.1k),
`GMEmpireGuardian1-4` (2.9k / 1.3k / 1.4k / 1.6k), `GMHellas` (2.3k), `GMDoppelGanger1-4`, `GMAida`, `GMKarutan1`,
`GMSwampOfQuiet` (1.5k), `GMNewTown`, `GMSantaTown`, `GM_PK_Field`, `GMHuntingGround`, `GMUnitedMarketPlace`,
`GMDuelArena`, `GM3rdChangeUp`, `w_CursedTemple` (1.2k), `NewBloodCastleSystem`, `NewChaosCastleSystem`.

Stan w porcie:
- Klasy światów są cienkie (większość 45-80 linii): wczytanie terenu, punkt spawnu, czasem pogoda
  (`Worlds/RaklionWorld.cs`, `Worlds/KanturuWorld.cs`). 31 z 112 klas nadpisuje `CreateMapTileObjects`.
- `Objects/Worlds/Kanturu` = 1 plik, `Raklion` = 1 plik (pogoda), `Crywolf` = 1 plik (pogoda), `ImperialGuardian` = 1 plik (pogoda).
  Brak mechaniki: wieże/bramy Kanturu, mapa Maya/Nightmare, ołtarze i NPC Crywolf, boss Kundun/Selupan w Raklion itp.
- **Cursed Temple**: tylko `CursedTempleEnterControl` (okno wejścia) + wzmianka w `PlayerObject`. Brak HUD-u meczu,
  relikwii, wyniku (oryginał: `NewUICursedTempleSystem` 1.4k linii, `w_CursedTemple`).
- **Empire Guardian / Imperial Guardian**: okno wejścia i pogoda. Brak timera, NPC i logiki etapów.
- **Doppelganger**: okno wejścia + światy. Brak ramki/okna postępu (`NewUIDoppelGangerFrame/Window`).
- **Battle Castle / Siege**: praktycznie brak (5 trafień, tylko ceny/pomocnicze). Pakiety `BC*`, `Gate*`, `Catapult*`,
  `Crown*` bez handlerów, brak UI (`NewUICastleWindow`, `NewUISiegeWar*`, `NewUICatapultWindow`, `NewUIGateSwitchWindow`).
  To świadome pominięcie jest prawdopodobne (OpenMU), ale to największa pojedyncza funkcja bez pokrycia.
- Obecne i działające (porównywalnie z oryginałem lub lepiej): Blood Castle (wejście/czas/wynik + 8 światów),
  Devil Square (wejście/odliczanie), Chaos Castle (światy), Duel (HUD + sieć), Illusion Temple (światy).

## 2. Skille, efekty, cząsteczki

- Oryginał: ~171 różnych `AT_SKILL_*`; `ZzzEffect.cpp` 19.5 tys. linii (≈391 różnych modeli `MODEL_*` w efektach),
  `ZzzEffectParticle.cpp` 9.3 tys., `ZzzEffectJoint.cpp` 7.3 tys., `ZzzEffectMagicSkill.cpp`, `ZzzEffectBlurSpark`, `ZzzEffectFireLeave`.
- Port: ~40 skilli ma zarejestrowany `[SkillVisualEffect]` (27 plików w `Objects/Effects/Skills`), 3 pliki w `Objects/Effects/Joints`,
  jeden katalog `Particles/Effects`. Efekty są dobrze ustrukturyzowane (rejestr + atrybut), ale pokrycie to rząd ~25-30% skilli.
- Brakujące/niepewne (zweryfikować per skill): większość skilli Dark Lorda (Fire Burst jest, brak m.in. Electric Spike, Chaotic Diseier,
  Earthquake jest), Summonera (Drain Life, Sleep, Chain Lightning, Berserker, Weakness, Innovation, Explosion), Rage Fightera
  (Dragon Roar/Lion, Chain Drive, Dark Side, Killing Blow, Upper Cut, Phoenix Shot – częściowo), skille master-level (cała gałąź mastery),
  skille potworów/bossów (Kundun, Selupan, Nightmare, Balgass itp.), buff-aury (Fenrir, wing aura częściowo).
- Brak odpowiednika: `ZzzEffectNoUse`, `ZzzEffectPointer`, `ZzzEffectBlurSpark`, `ZzzEffectFireLeave` jako osobnych systemów (część przeniesiona do `WeaponTrailEffect` itp. – zweryfikować).

## 3. UI

Oryginał: ~100 plików `NewUI*` (`NewUIMuHelper` 3k, `NewUICustomMessageBox` 7.7k, `NewUIMainFrameWindow` 2.7k, `NewUIMyInventory` 2.5k itd.).
Port ma ~50 kontrolek w `Controls/UI/Game` (inventory, character, chat, party, trade, vault, NPC shop, player shop, chaos mix,
minimap, move command, skille, master skill tree, buffy, pause/options, quest dialog, eventowe okna wejścia).

Brak lub bardzo niepełne:
- **Gildia**: `GuildManager` jest, ale brak okien (`NewUIGuildInfoWindow`, `NewUIGuildMakeWindow`, `UIGuildMaster`, `UIGuildInfo`).
  Brak nazwy/znaczka gildii nad graczem: `ScopeHandler.cs:2500,2527` – TODO „update/clear guild info in _scopeManager".
- **Znajomi / listy / czat prywatny**: `FriendManager` + pakiety, brak okna (`NewUIFriendWindow`, chat room, letter).
- **MuHelper (auto-grind)**: 0 trafień w kodzie. Oryginał: `NewUIMuHelper` 3 tys. linii + pakiety konfiguracji.
- **Gens**: ranking i wybór (`NewUIGensRanking`) – tylko obsługa czatu i wiadomości.
- **Lucky Item / Lucky Coin** (`NewUILuckyItemWnd`, `NewUIExchangeLuckyCoin`, `NewUIRegistrationLuckyCoin`) – brak.
- **Jewel of Harmony, Socket, Gamble, Mix inventory** – logika jest w `ChaosMixControl`/`MixRecipeDatabase`, brak dedykowanych okien
  (`UIJewelHarmony`, `SocketSystem`, `GambleSystem`). `CharacterState.cs:2091` TODO: mapowanie socket → Seed Sphere.
- **Eventowe HUD-y**: Cursed Temple, Kanturu, Crywolf, Empire Guardian, Doppelganger, Battle Soccer, Siege – brak (patrz pkt 1).
- **Pozostałe okna**: Help, Command, QuickCommand, HotKey, WindowMenu, PetInfo, SetItemExplanation, NPC dialog/NPC quest
  (jest `QuestDialogControl`, ale bez `NewUIMyQuestInfoWindow` / `QuestProgress`), Gateman, UnitedMarketPlace, In-Game Shop/cash shop
  (`GIPetManager`, `InGameShop` w oryginale – brak), Duel watch (zweryfikować), Observer.
- **Skróty klawiszowe** (`GameSceneHotkeys.cs`): I, V, C, M, T, E, O, P, S, D, L, R, F2/F4/F5/F6 – brak pełnej mapy z oryginału
  (Alt+/Ctrl+ kombinacje, HotKey window, makra `Macro.txt` – zweryfikować).

## 4. Sieć

- Oryginał: 262 `Receive*`; port: 100 rejestracji `[PacketHandler]` (część obsługuje wiele podkodów).
- Pokryte: login, lista postaci, widoczność obiektów, walka, ekwipunek, handel, sklep, magazyn, party, gildia (częściowo), pojedynki, osobisty sklep,
  quest legacy + progress, BC/DS/CC, Gens (częściowo), pet, buffy, master level, chat.
- Bez handlerów (z listy `Receive*`, zweryfikować przed implementacją): `BC*` / `Gate*` / `Catapult*` / `Crown*` / `Castle*` (Siege),
  `Crywolf*`, `Kanturu3rd*`, `Raklion*`, `CursedTemple*`, `LetterSendResult/Letter*`, `CreateChatRoomResult`, `EventChip*`/`MutoNumber`,
  `ScratchResult`, `CheckSumRequest`, `Darkside`, `MatchGameCommand`, `SoccerTime/Goal` (częściowo), `MapInfoResult`, `PreviewPort`, `CrywolfBenefit*`.
- `InventoryHandler.cs:732` – „Inventory handling not implemented for version {Version}" (inne wersje protokołu poza docelową).
- `PacketRouter.cs:143` – TODO reset stanu klienta przy rozłączeniu.

## 5. Audio

- Aktualny stan i pozostałe zadania: `docs/gap-plan/11-audio.md`. Tabela `WorldAudioDatabase` uzupełnia domyślną muzykę i ambient, a część eventów przełącza muzykę według stanu.
- `SoundController` obsługuje `.wav`, `.mp3` i `.ogg` (NVorbis), crossfade i dźwięk z tłumieniem; działanie w grze i na docelowych platformach wymaga weryfikacji.
- Audyt `tools/sound_audit.py --strict` sprawdza ścieżki portu względem danych, ale nie potwierdza, że dźwięki są wyzwalane we właściwych sytuacjach. Nadal należy porównać pozostałe dźwięki otoczenia, kroków, UI i potworów z oryginałem.
- Ponowny audyt: 171 kandydatów, 0 błędów ścieżek portu. Częściowo rozszerzono obsługę specjalnych ataków Imperial Guardian w `0x69`; syntetyczne testy nie potwierdzają formatu pakietów serwera ani odsłuchu. Szczegóły, klasyfikację i ograniczenia zawiera plan audio.
- Po uzupełnieniu dźwięku aktualizacji przedmiotu `F3:14` audyt wykazał 170 kandydatów i 0 błędów ścieżek portu; wynik nie jest liczbą potwierdzonych braków.
- Po naprawie pomijania wielokrotnych rejestracji i dodaniu Forsakera aktualny wynik to 168 kandydatów i 0 błędów ścieżek portu; wyniki przed poprawką narzędzia nie są w pełni porównywalne.
- Po dodaniu Gallii i Quarter Mastera bieżący audyt wskazuje 166 kandydatów oraz 0 błędów ścieżek portu; testy nie zastępują odsłuchu ani weryfikacji pakietów na serwerze.
- Po zarejestrowaniu Cavalry Captaina bieżący audyt wskazuje 162 kandydatów i 0 błędów ścieżek portu.
- Zarejestrowano warianty duchów Illusion Temple i dźwięki ich stanów; bieżący audyt wskazuje 156 kandydatów i 0 błędów ścieżek portu. Czas wyzwalania pozostaje przybliżeniem wymagającym testów w grze.
- Zarejestrowano również istniejące klasy Lunar Rabbit i Fire Flame Ghost z częścią dźwięków; bieżący audyt wskazuje 149 kandydatów i 0 błędów ścieżek portu. Efekty i zachowanie w grze wymagają dalszej weryfikacji.
- Dodano dźwięk ataku pułapek Blood Castle; bieżący audyt wskazuje 148 kandydatów i 0 błędów ścieżek portu. Dźwięki Rage Fightera i zapadania Chaos Castle wymagają powiązania z brakującą lub niezweryfikowaną mechaniką.
- Poprawiono brakującą ścieżkę modelu Nightmare oraz część dźwięku teleportacji Kanturu; bieżący audyt wskazuje 147 kandydatów i 0 błędów ścieżek portu. Model Maya i reżyseria eventu wymagają dalszego sprawdzenia.
- Na podstawie dostarczonego `monsterskill.bmd` powiązano cztery skille Nightmare z jego akcjami i dźwiękami w `0x69`. Testy dekodują model i dane, ale nie zastępują testu w świecie lub odsłuchu.
- Poprawiono klasyfikację trzeciego i czwartego ataku potworów w kontrolerze animacji; test danych potwierdził dostępność mapowanych akcji w 12 modelach Imperial Guardian. Uruchomienie w świecie nadal wymaga weryfikacji.
- Poprawiono odczyt flag typu i zmiennej długości efektów w rekordach widoczności NPC Season 6, co chroni mapowania kolejnych postaci w pakiecie; syntetyczne testy nie zastępują weryfikacji z serwerem.
- System ambientu obiektów wybiera najbliższe źródło danego typu zamiast zależeć od kolejności obiektów; działanie przestrzenne pozostaje do odsłuchu w grze.
- Dodano dźwięki wyników listy mixu `0x31`; audyt wskazuje 145 kandydatów i 0 błędów ścieżek portu. Stan obiektów zapadającego się Chaos Castle nadal wymaga implementacji.
- Zarejestrowano istniejące modele Gaiona i Jerinta oraz część ich akcji/dźwięków; audyt wskazuje 143 kandydatów i 0 błędów ścieżek portu. Stan berserkera i działanie w grze pozostają do weryfikacji.

## 6. Potwory, NPC, gracze

- 406 klas potworów i 60 NPC – pokrycie modeli bardzo dobre. Luka jest w zachowaniu: `MonsterObject.cs` (678 linii) i
  `WalkerObject.cs` (887) vs `ZzzAI.cpp` + obsługa akcji/skilli potworów w `ZzzCharacter.cpp` (15 tys. linii). Brak wielu
  specjalnych ataków, faz bossów i animacji śmierci per potwór (część zrobiona jako dedykowane efekty).
- `PlayerActionMapper.cs:241-243` – TODO dla Scepter/Book (Summoner: animacje ataku).
- `BloodCastleHandler.cs:87` – TODO: quest item na modelu postaci (EtcPart).
- Brak odpowiednika: boidy/zwierzyna ambientowa na wszystkich mapach (`GOBoid` 1.9k linii; jest `AmbientBoid`/`AmbientFlockSystem` w `Events`), `CharInfoBalloon`, `PersonalShopTitleImp` (zweryfikować).
- Pety i konie: Dark Horse / Raven / Fenrir są; `w_PetProcess`/`w_PetAction*` – zweryfikować zachowania (Demon/Unicorn/Round/Stand).

## 7. Grafika i infrastruktura

- Port ma nowocześniejszy pipeline (shadow map, instancing, dynamic lights) – to przewaga, nie luka.
- Różnice do sprawdzenia: `CSWaterTerrain` (woda/odbicia), `ShadowVolume`, `CameraWalk` (CWS) w scenie logowania
  (`Worlds/LoadWorld.cs:32` TODO: „fix CameraAnglePosition load"), `SideHair`, `TextureScript` (jest `Graphics/TextureScript.cs`).
- Celowo poza zakresem (prawdopodobnie): `GameGuard`/`ProtectSysKey`, `LauncherHelper`, `GCCertification`, `CBTMessageBox`, `CGFx*` (Scaleform),
  `MovieScene`, `CreditWin`.

## Przewaga portu względem SourceMain5.2

- Dodatkowe światy S6-S20 (Acheron, Debenter, Nixies Lake, Deep Dungeon 1-5, Lost Tower, Kubera Mine itp.).
- Wiele platform (Windows DX/GL, Linux, macOS, Android, iOS), nowoczesny HUD, DPI/UI scaling, kamera, diagnostyka, telemetria.
- Testy integracyjne (PlayerShop) i narzędzia (`MuDiagnostics.Web`, `Client.Editor`).

## Rekomendowana kolejność prac

1. **UI gildii i znajomych** + nazwa gildii nad graczem (najbardziej widoczne w codziennej grze, sieć już jest).
2. **Wizualia skilli** dla klas, które port już wspiera (Summoner, Rage Fighter, master skille) – największy efekt „feel".
3. **Muzyka i ambient** dla pozostałych światów (tanie, duży efekt) + dekoder OGG.
4. **MuHelper** (jeśli serwer wspiera) – duża funkcja QoL.
5. **Eventy**: Cursed Temple HUD, Doppelganger/Empire Guardian okna postępu, Kanturu/Raklion/Crywolf mechanika po kolei.
6. **Siege (Battle Castle)** – tylko jeśli serwer docelowy go obsługuje.
7. Domknięcie TODO z sekcji 4-6.

## Co zweryfikować przed planowaniem prac

- Uruchomić klienta na kilku mapach eventowych i sprawdzić, co faktycznie brakuje (raport jest statyczny).
- Zestawić pakiety: lista `Receive*` z `WSclient.cpp` vs. rzeczywiście wysyłane przez serwer OpenMU (wiele z nich OpenMU nie używa).
- Pełna lista skilli bez `[SkillVisualEffect]`: porównać `SkillDatabase` z rejestrem efektów.
