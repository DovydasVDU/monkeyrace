<div align="center">

# MonkeyRace

**Spausdinimo lenktynės lietuvių kalba – realiu laiku, su perklausomis, klaidų analize ir progresu.**

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![ASP.NET Core MVC](https://img.shields.io/badge/ASP.NET_Core-MVC-512BD4)
![SignalR](https://img.shields.io/badge/SignalR-realaus_laiko-0A7BBB)
![EF Core + SQLite](https://img.shields.io/badge/EF_Core-SQLite-003B57)
![Tests](https://img.shields.io/badge/testai-xUnit-2E8B4F)

</div>

MonkeyRace – žaidimas, kuriame 2–4 žaidėjai lenktyniauja rašydami tikrus lietuviškus tekstus. Kiekvieno beždžionėlė bėga trasa
pagal tai, kiek teksto jau parašyta teisingai. Po lenktynių matai savo greičio grafiką, klaidas, kas ir kada pirmavo, gali
peržiūrėti visas lenktynes iš naujo, o už rezultatus gauni XP, lygius ir bananų, kuriuos išleidi parduotuvėje.

---

## Turinys

- [Funkcionalumas](#funkcionalumas)
- [Technologijos](#technologijos)
- [Paleidimas](#paleidimas)
- [Projekto struktūra](#projekto-struktūra)
- [Architektūra](#architektūra)
- [Priimti sprendimai ir kodėl](#priimti-sprendimai-ir-kodėl)
- [Saugumas ir privatumas](#saugumas-ir-privatumas)
- [Progreso taisyklės](#progreso-taisyklės)
- [API ir maršrutai](#api-ir-maršrutai)
- [Testai](#testai)
- [Žinomi apribojimai ir tolesni darbai](#žinomi-apribojimai-ir-tolesni-darbai)
- [Tekstų šaltiniai ir licencijos](#tekstų-šaltiniai-ir-licencijos)

---

## Funkcionalumas

### Žaidimo režimai

| Režimas | Kaip veikia |
|---|---|
| **Online lenktynės** (`/race/quick`) | Matchmaking į kambarį iki 4 žaidėjų. Lobby laukia 10 s, tuščias vietas užpildo botai, kurių greitis prisitaiko prie dalyvių. |
| **Su botais** (`/race/bots`) | Žaidėjas pats pasirenka botų lygius (lengvas / vidutinis / sunkus / prisitaikantis). |
| **Kambarys su draugais** (`/race/room/create`) | Privatus kambarys su kodu, lenktynes pradeda kūrėjas. |
| **Solo** (`/Race/Play`) | Rašai savo tempu, laikas pradeda bėgti nuo pirmo klavišo. |
| **Žodžių režimas** (`/race/words`, online – `/race/quick?seconds=`) | 15 s – 2 min. lenktynės su ~1000 dažniausių lietuviškų žodžių. |
| **Treniruoklis** (`/race/train`) | Tekstas iš žodžių, kuriuose dažniausiai klysti. Žodis dingsta iš sąrašo, kai jį 3 kartus iš eilės parašai be klaidų. |

### Rašymas

- Gyvai skaičiuojami **WPM, tikslumas ir laikas**.
- **Klaida matoma iš karto**: neteisinga raidė raudona su laukiama raide po apačia, o visas žodis ir rašymo laukas parausta, kol klaida ištaisoma.
- Žodis su klaida **neleidžia pereiti toliau** (tarpas atmetamas) – kaip TypeRacer.
- Rašymo lauke matosi **tik rašomas žodis**; jau parašytas tekstas matosi aukščiau.
- **Telefone** ilgas tekstas rodomas 4 eilučių lange, kuris pats slenka (rašoma eilutė visada antra), o starte teksto kortelė pakeliama virš klaviatūros.
- Online: Ctrl/Alt + Backspace ištrina visą žodį; įklijuoti tekstą draudžiama.

### Po lenktynių

- **Podiumas ir asmeninis rezultatas** su gautais XP ir bananais.
- **Lenktynių istorija** – lyderio juosta laike ir aplenkimų sąrašas.
- **Perklausa** – visas lenktynes galima peržiūrėti iš naujo (0,5×–2×), o ant teksto spalvomis matyti, kur kiekvienas žaidėjas buvo ir kuris pirmavo.
- **Mano greitis** – WPM grafikas per lenktynes.
- **Klaidos** – tekstas su pataisytomis ir likusiomis klaidomis.
- **Šio teksto lyderiai** – 10 geriausių online rezultatų būtent šiam tekstui, su perklausa ir data.
- **Apie tekstą** – autorius, kūrinys, šaltinis, licencija ir ypatybės (kabutės, skaičiai, daugtaškis…).

### Pamokos (`/pamokos`)

Klaviatūros kursas: 6 modulių, 29 pamokų žemėlapis nuo namų eilutės iki sakinių su didžiosiomis raidėmis.

- **Prisitaikantis variklis** (`Services/LessonEngine.cs`, grynos funkcijos): kiekvienas raundas sudaromas iš žodžių su dar neįvaldytais klavišais, dažniau kartojami silpni klavišai, ilgis auga ar mažėja pagal tikslumą. Tempas (ramiai / įprastai / greitai) keičia reikalaujamą tikslumą ir raundo ilgį.
- **Klaviatūra ir rankos** – rodomas kitas klavišas ir pirštas (spalvos pagal pirštą), užuominas galima išjungti. Lietuviškos raidės (ą č ę ė į š ų ū ž) rašomos kaip lietuviškoje klaviatūroje, o didžiosios – su Shift (pvz. Š = Shift + 6).
- **Pažanga**: 1–3 žvaigždutės (tikslumas, pamokoms su tikslu – ir WPM), XP ir bananai tik už naujai gautas žvaigždutes. Pirmos 3 pamokos žaidžiamos be paskyros (progresas naršyklėje, prisijungus sinchronizuojamas), likusios atsirakina paeiliui.
- Serveris be būsenos: klientas siunčia raundo statistiką, serveris grąžina kitą raundą; baigus pamoką rezultatas perskaičiuojamas serveryje ir tikrinamas pagal pamokos pradžios laiką.

### Progresas ir bendruomenė

- **XP ir 10 lygių**, **bananai** ir **parduotuvė** – kepurės, kailio spalvos ir trasų stiliai beždžionėlei.
- **Profilis**: statistika, klaidų analizė (kurias raides painioji su kuriomis, kuriuose žodžiuose klysti) ir paskutinės 20 lenktynių su perklausa.
- **Top 10** – geriausi visų laikų online rezultatai; užvedus ant vardo – žaidėjo kortelė (lygis, PB, vidutinis WPM, tikslumas).
- **Paskyros ištrynimas** pačiam, patvirtinant slaptažodžiu.

---

## Technologijos

| Sritis | Kas naudojama |
|---|---|
| Backend | ASP.NET Core MVC (.NET 10), Razor views |
| Realus laikas | SignalR (`/racehub`, WebSocket) |
| Duomenys | Entity Framework Core 10 + SQLite (WAL), EF migracijos |
| Autentikacija | Cookie auth, BCrypt slaptažodžių maišos, ASP.NET rate limiter |
| Frontend | Vanilla JS (be framework'ų), CSS kintamieji, inline SVG |
| Testai | xUnit, `WebApplicationFactory` integraciniai testai |
| Tekstų paruošimas | Python scraperiai (Vikipedija, Vikišaltiniai) |

---

## Paleidimas

Reikia: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/DovydasVDU/monkeyrace.git
cd monkeyrace
dotnet run
```

Atsidaryk https://localhost:5001. Daugiau nieko daryti nereikia:

- pirmo paleidimo metu `Database.Migrate()` sukuria SQLite duomenų bazę (`monkeyrace-dev.sqlite` vystymui, `monkeyrace.sqlite` produkcijai);
- `TextImporter` įkelia ~4 700 tekstų iš `Data/texts-lt.jsonl`. Kartoti saugu – esami tekstai neįkeliami iš naujo, tik atnaujinami, jei faile pasikeitė.

Botų nustatymai (`appsettings.json` → `Bots`): iki kiek žaidėjų pildyti, numatytasis greitis, WPM ribos ir tikslumo intervalas.

> Repozitorijoje yra du sprendimo failai, todėl komandoms, kurios dirba su sprendimu, nurodyk `monkeyrace.sln` (pvz. `dotnet test monkeyrace.sln`).

---

## Projekto struktūra

```
Controllers/        HTTP: Home, Auth, Race, Lessons (pamokos), User (profilis, parduotuvė, ištrynimas), StatsApi (/api/stats)
Hubs/RaceHub.cs     SignalR: JoinRace, SendProgress, StartFriendRace, LeaveRace
Services/
  MatchmakingService  kambariai, lobby, botai, progresas, finišas, rezultatų įrašymas (singleton)
  RaceService         tekstų parinkimas, rezultatai ir apdovanojimai, žaidėjo kortelė, parduotuvė
  RaceAnalyzer        laiko juosta, lyderiai, aplenkimai, perklausos JSON
  Progression         XP, lygių ir bananų taisyklės (grynos funkcijos)
  BotModels           botų greitis pagal žaidėjų lygį
  WordService         žodžių režimo tekstas, ShopCatalog – parduotuvės prekės
  LessonCatalog       pamokų turinys (moduliai, klavišai, tekstai)
  LessonEngine        prisitaikantis raundų sudarymas, žvaigždutės (grynos funkcijos)
  LessonWords         pamokų žodynas (Data/lesson-words.txt + words-lt.txt)
Data/               AppDbContext, TextImporter, texts-lt.jsonl, words-lt.txt, lesson-words.txt
Models/             EF modeliai ir view modeliai
Views/              Razor: Home, Lessons, Race (Play, MultiPlay, Replay), User, Shared (_Layout, _Navbar, _Footer, _Icons, _Monkey…)
wwwroot/            css (site, lessons), js (typing, replay, progress, login, signalr, lesson, lesson-keyboard), fonts, favicon
Migrations/         EF Core migracijos
tests/              xUnit testai
*.py                tekstų scraperiai ir konvertavimas į Data/texts-lt.jsonl
DESIGN.md           dizaino sistema: spalvos, šriftai, komponentai, JS priklausomybės
```

---

## Architektūra

### Online lenktynių eiga

```
Naršyklė                         Serveris
────────                         ────────
GET /race/quick  ───────────────► MatchmakingService: randa arba sukuria kambarį
                 ◄─── redirect ── /race/multiplay/{raceId}
SignalR JoinRace ───────────────► tikrina playerKey pagal sesiją
                 ◄─ RaceState ─── lobby 10 s, botai užpildo vietas
                 ◄─ Countdown ─── tekstas + serverio laikas (laikrodžių sinchronizacija)
                 ◄─ RaceStarted
SendProgress (kiekvienas klavišas) ─► progresas apkerpamas pagal serverio laikrodį
                 ◄─ Progress ──── visiems kambario žaidėjams (botai – kas 250 ms)
                 ◄─ PlayerFinished  vieta, WPM, laiko juosta – iškart po finišo
                                   visiems baigus: rezultatai, XP ir perklausa į DB
                 ◄─ RaceFinished ── rezultatai, analizė, apdovanojimai, teksto info
GET /api/stats/text/{id} ───────► šio teksto lyderiai
```

### Duomenų modelis

| Lentelė | Paskirtis |
|---|---|
| `Users` | Paskyra: vardas, BCrypt maiša, XP, bananai, užsidėtos prekės |
| `TextSnippets` | Tekstai su autoriumi, kūriniu, šaltiniu, licencija ir ypatybėmis |
| `Races` | Lenktynės: tekstas arba žodžių režimo trukmė, ar online, perklausos JSON |
| `RaceResults` | Žaidėjo rezultatas: WPM, tikslumas, klaidos, vieta, gauti XP ir bananai |
| `UserKeyStats` | Kiek kartų laukiant raidės X paspausta Y – klaidų analizei |
| `UserWordMistakes` | Žodžiai su klaidomis – treniruokliui |
| `UserItems` | Nupirktos parduotuvės prekės |
| `UserLessonProgress` | Pamokų žvaigždutės, geriausi tikslumas ir WPM (raktas: vartotojas + pamoka) |

---

## Priimti sprendimai ir kodėl

**Serveris – vienintelis tiesos šaltinis.** Naršykle aklai netikima: online progresas negali augti greičiau nei 250 WPM pagal *serverio* laikrodį nuo starto, o solo rezultatas priimamas tik vieną kartą vienam puslapio atidarymui, ir jo WPM tikrinamas pagal serverio išmatuotą laiką ir teksto ilgį. Apdovanojimų formulė apkerpa WPM iki 200.

**Matchmaking atmintyje, ne DB.** Kambariai, lobby ir progresas laikomi `MatchmakingService` (singleton) atmintyje su `lock`, o į DB rašomas tik galutinis rezultatas ir perklausa. Taip kiekvienas klavišas nekainuoja DB užklausos. Kaina – programa veikia vienoje instancijoje (žr. apribojimus).

**Botai, kad nereikėtų laukti.** Jei per 10 s neatsiranda varžovų, vietas užpildo botai. „Prisitaikančių“ botų greitis skaičiuojamas iš paskutinių 10 žmonių rezultatų su skirtingais koeficientais, todėl lenktynės būna įtemptos. Lenktynės, kur botus pasirinkai pats, į Top 10 neįskaitomos.

**Top 10 tik klasikinės online lenktynės.** Solo ir trumpo žodžių režimo WPM nepalyginami su ilgo teksto lenktynėmis (trumpas spurtas duoda daugiau), todėl lyderių lentelėse jų nėra.

**Perklausa – vienas JSON lenktynėms.** Kiekvieno žaidėjo progresas kas 250 ms įrašomas į `Races.ReplayJson`, o ne į atskirą lentelę: perklausa visada skaitoma kaip visuma, todėl taip paprasčiau ir greičiau. Top 10 ir profilio užklausos JSON nekrauna (projekcija). Ištrynus paskyrą, kitų žaidėjų perklausose vardas pakeičiamas į „Ištrintas žaidėjas“.

**Žodis su klaida užrakina tarpą.** Kaip TypeRacer: kol žodis neištaisytas, toliau nepereisi. Todėl progresas visada yra teisingai parašyto teksto ilgis, o rašymo lauke užtenka rodyti tik rašomą žodį.

**SQLite vietoj SQL Server.** Viena programa, vienas serveris – atskiras DB serveris nereikalingas. SQLite failas su WAL leidžia skaityti lygiagrečiai, o rašymų (rezultatai lenktynių pabaigoje) nedaug. DB nepasiekiama per tinklą – nėra atviro prievado.

**Tekstai kaip duomenų failas.** Python scraperiai paruošia `Data/texts-lt.jsonl`, o programa jį importuoja paleidimo metu. `TextImporter.Clean` sutvarko scraperio tarpus („žodis .“, „( a , b )“), o pataisytas tekstas atnaujinamas ir jau veikiančioje DB – užtenka pataisyti failą ir perkrauti.

**Jokių trečiųjų šalių.** Šriftai (Fredoka, Plus Jakarta Sans, JetBrains Mono) ir SignalR klientas laikomi `wwwroot`, ne Google Fonts ar CDN. Analitikos nėra, slapukai tik būtinieji – todėl slapukų sutikimo lango nereikia, o lankytojo IP nemato jokia kita įmonė.

**Dizaino sistema vienoje vietoje.** Visos spalvos – CSS kintamieji (`var(--…)`), ikonos ir iliustracijos – inline SVG (`_Icons`, `_Monkey`, `_Banana`), jokių emoji. Taisyklės ir JS naudojami ID/klasės aprašyti [`DESIGN.md`](DESIGN.md).

**Be frontend framework'o.** Puslapiai serverio generuojami (Razor), o interaktyvumą duoda nedideli JS failai. Bendra rašymo logika (`typing.js`), perklausa (`replay.js`) ir progresas (`progress.js`) dalinami tarp solo ir online.

---

## Saugumas ir privatumas

- **SQL injection** – visos užklausos per EF Core su parametrais, neapdoroto SQL nėra.
- **XSS** – vartotojo tekstas išvalomas prieš įterpiant į HTML, JSON į puslapį įdedamas su `<` → `<`, slapyvardis ribojamas `^[\p{L}\p{N}_]{3,20}$`.
- **Slaptažodžiai** – BCrypt maišos, bent 6 simboliai; prisijungimas ir registracija ribojami iki 30 užklausų per minutę iš IP (didesnė riba, nes mokykloje visa klasė gali būti už vieno IP).
- **Slaptažodžių spėliojimas** – po 8 nepavykusių prisijungimų slapyvardžiui 10 min. grąžinamas 429 (net teisingas slaptažodis tuo metu nepriimamas). Skaitliukas laikomas atmintyje.
- **Slapukas** – `HttpOnly`, `Secure`, `SameSite=Strict`, 7 dienos; paskyros ištrynimo forma su antiforgery žetonu. Kiekvienos užklausos metu tikrinama, ar vartotojas dar egzistuoja, todėl ištrintos paskyros slapukas nustoja galioti.
- **Šifravimo raktai** – slapukų raktai (Data Protection) saugomi diske (`App_Data/keys`, keičiama per `DataProtection:KeysPath`), todėl perkrovus ar atnaujinus svetainę žaidėjai neatsijungia. Aplanko diegiant netrinti ir nekomituoti (`.gitignore`).
- **Saugumo antraštės** – `Content-Security-Policy` (tik savo domenas, `frame-ancestors 'none'`, `object-src 'none'`, `form-action 'self'`), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`.
- **Pamokų dažnio riba** – `POST /pamokos/{id}/round` ir `/complete`: 60 per minutę prisijungusiam vartotojui, 300 svečiui (pagal IP).
- **HTTPS** ir HSTS, produkcijoje – klaidos puslapis vietoj stack trace.
- **Sukčiavimas** – serverio laikrodžiu paremtos ribos (žr. sprendimus), SignalR tikrina, kad `playerKey` priklauso sesijai.
- **BDAR** – [privatumo puslapis](Views/Home/Privacy.cshtml) (`/privatumas`): kokie duomenys saugomi, kokie slapukai, kaip ištrinti paskyrą.

---

## Progreso taisyklės

Visos taisyklės – `Services/Progression.cs` (grynos funkcijos, padengtos testais).

| | Solo | Online |
|---|---|---|
| XP | (10 + WPM/4) × tikslumas | (20 + WPM/2 + vietos bonusas 30/20/10) × tikslumas |
| Bananai | 1 | 2 + vietos bonusas 8/4/2 |

- Žodžių režime XP dauginamas iš `min(1, trukmė / 60 s)`.
- Tikslumas ≥ 95 % – +1 bananas; pirmos lenktynės – +50 XP ir +10 bananų.
- 10 lygių (0, 100, 250, 450, 700, 1000, 1400, 1900, 2500, 3200 XP); už kiekvieną naują lygį – 20 × lygis bananų.
- Svečiai gali žaisti, bet rezultatai nesaugomi – rodoma, kiek būtų gavę.

---

## API ir maršrutai

| Maršrutas | Paskirtis |
|---|---|
| `GET /api/stats/top10` | Top 10 online rezultatų |
| `GET /api/stats/{username}` | Žaidėjo kortelė: lygis, XP, PB, vidutinis WPM, tikslumas |
| `GET /api/stats/text/{id}` | Šio teksto lyderiai ir kiek kartų rašytas |
| `GET /race/replay/{resultId}` | Lenktynių perklausa |
| `POST /Race/SaveResult` | Solo rezultatas (vienkartinis, tikrinamas) |
| `POST /race/mistakes` | Raidžių ir žodžių klaidos profilio analizei |
| `POST /shop/equip/{itemId}` | Nupirkti / užsidėti prekę (kaina tik serveryje) |
| `GET /pamokos`, `GET /pamokos/{id}` | Pamokų žemėlapis ir pamoka (svečiams – tik pirmos 3) |
| `POST /pamokos/{id}/round` | Kito raundo parinkimas pagal ankstesnio statistiką (be būsenos) |
| `POST /pamokos/{id}/complete` | Pamokos rezultatas: serveris perskaičiuoja tikslumą, WPM, žvaigždutes ir apdovanojimus |
| `POST /pamokos/sync` | Svečio pamokų progreso sujungimas po prisijungimo (tik nemokamos pamokos, be XP) |
| SignalR `/racehub` | `JoinRace`, `SendProgress`, `StartFriendRace`, `LeaveRace` |

---

## Testai

```bash
dotnet test monkeyrace.sln
```

Testai, tarp jų:

- **Progresas** – XP, lygių ir bananų formulės.
- **Solo anti-cheat** (`WebApplicationFactory`) – per greitas rezultatas atmetamas, lygiagretūs išsaugojimai apdovanoja tik kartą.
- **Matchmaking** – vietos ir apdovanojimai, DNF, finišavęs ir uždaręs puslapį išlaiko rezultatą, lobby startuoja laiku, žodžių režimas, botų lenktynės neįskaitomos į Top 10, draugų kambarys.
- **Lenktynių analizė** – lyderiai, aplenkimai, perklausa.
- **Tekstų importas** – metaduomenys, tarpų valymas, pataisyto teksto atnaujinimas.
- **Šio teksto lyderiai** – tik to teksto online rezultatai, geriausi pirmi.
- **Pamokos** – katalogas (kaupiami klavišai, žodžių pakanka), variklis (silpni klavišai, lygio keitimas, ankstyvas pabaigimas, žvaigždutės, sakiniai, lietuviškos didžiosios), apdovanojimai ir HTTP kelias (`complete`, svečio sinchronizacija).
- **Slapukas ištrintam vartotojui** – po paskyros ištrynimo senas slapukas atmetamas.
- **Saugumas** – saugumo antraštės yra, o 8 nepavykę prisijungimai užrakina tik tą slapyvardį.
- **Paskyros ištrynimas** – neteisingas slaptažodis paskyrą palieka, teisingas ištrina viską ir anonimizuoja perklausas.

---

## Žinomi apribojimai ir tolesni darbai

- **Viena instancija.** Kambariai laikomi atmintyje, todėl programos negalima paleisti keliuose serveriuose, o perkrovimas nutraukia vykstančias lenktynes.
- **Progresas siunčiamas po kiekvieno klavišo.** Esant šimtams žaidėjų vienu metu verta siųsti paketais kas ~150–250 ms ir naudoti atskirą `lock` kiekvienam kambariui. Apkrovos testas dar neatliktas.
- **SignalR hub'as be dažnio ribojimo.** `RaceHub` tikrina `playerKey`, naudoja serverio laikrodį progresui ir apkerpa `typing` iki 30 simbolių, bet pranešimų dažnis neribojamas.
- **CSP leidžia `'unsafe-inline'`**, nes dalyje puslapių yra inline `<script>` ir `onclick`. Jis vis tiek blokuoja kitų domenų skriptus ir įdėjimą į svetimą svetainę; griežtam CSP (nonce) reikėtų iškelti visą inline kodą į failus. Įdiegus pažiūrėk naršyklės konsolę, ar nėra CSP pranešimų.
- **Prisijungimo užrakinimas laikomas atmintyje** (viena instancija) ir leidžia kitam žmogui 10 min. užrakinti svetimą slapyvardį. Keliems serveriams skaitiklį reikėtų laikyti DB.
- **Klientų IP už tarpinio serverio.** Dažnio ribos remiasi `RemoteIpAddress`. Hostingo pusėje patikrink, ar ten ne proxy IP; jei programa už patikimo proxy, įjunk `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (kitaip `X-Forwarded-For` galėtų suklastoti bet kas).
- **Slaptažodžio keitimo / atstatymo nėra** (ir nėra el. pašto), todėl pamiršusiam slaptažodį paskyra prarandama. Planuojama vėliau.
- **Vikišaltinių tekstų licencija** pažymėta `PD-PATIKRINTI` – reikia patvirtinti, kad visi kūriniai viešajame domene.

---

## Tekstų šaltiniai ir licencijos

- **Vikipedija** (~1 080 tekstų) – [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/); autorius ir nuoroda rodomi „Apie tekstą“.
- **Vikišaltiniai** (~3 660 tekstų) – lietuvių klasika (J. Biliūnas, Šatrijos Ragana, Lazdynų Pelėda, Vaižgantas), viešasis domenas.
- **Šriftai** – Fredoka, Plus Jakarta Sans, JetBrains Mono, [SIL Open Font License](wwwroot/fonts); **SignalR klientas** – MIT.

---

<div align="center">

Sukūrė **Dovydas Bielinskas** · [LinkedIn](https://www.linkedin.com/in/dovydas-bielinskas-844b16337/) · [dovydas.bielinskas@gmail.com](mailto:dovydas.bielinskas@gmail.com)

</div>
