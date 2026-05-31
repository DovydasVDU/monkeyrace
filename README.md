# MonkeyRacer

**ASP.NET Core MVC | SignalR | Entity Framework Core | SQL Server**

## Santrauka

MonkeyRacer projektas demonstruoja pilnavertę ASP.NET Core MVC web aplikaciją su realaus laiko multiplayer funkcionalumu.

### Pagrindiniai techniniai sprendimai

- MVC architektūra aiškiai atskiria atsakomybes tarp modelių, rodinių ir controller'ių
- SignalR suteikia efektyvų dvipusį ryšį multiplayer lenktynėms be polling
- Entity Framework Core abstrakuoja SQL ir leidžia dirbti su DB kaip C# objektais
- Cookie autentikacija su BCrypt slaptažodžių hash'avimu užtikrina saugumą
- Servisų sluoksnis (`RaceService`) perkelia verslo logiką iš controller'ių, sumažindamas kodo kartojimąsi
- In-memory state su thread-safe lock'ais leidžia greitus realaus laiko atnaujinimus

Projektas sėkmingai veikia su SQL Server duomenų baze ir palaiko kelių žaidėjų vienu metu lenktyniavimą realiu laiku.

---

# 1. Projekto apžvalga

MonkeyRacer – tai žiniatinklinė klaviatūros rinkimo greičio (spausdinimo) programa, sukurta ASP.NET Core MVC platformoje su .NET 3.1.

Projekto tikslas – realaus laiko daugiau žaidėjų lenktyniavimas, statistikos kaupimas ir rezultatų saugojimas SQL Server duomenų bazėje.

## Pagrindinės projekto galimybės

- **Solo žaidimas** – atsitiktiniai lietuviški tekstų fragmentai, WPM ir tikslumo skaičiavimas
- **Multiplayer žaidimas** – realaus laiko lenktynės per SignalR WebSocket
- **Vartotojų autentikacija** – registracija, prisijungimas, cookie sesija
- **Lyderių lenta** – Top 10 rezultatų per REST API
- **Statistikos peržiūra** – WPM, tikslumas, žaidimų istorija

---

# 2. MVC architektūros struktūra

Projektas remiasi klasikinės MVC (Model-View-Controller) architektūra, kur atsakomybės yra aiškiai atskirtos.

| Sluoksnis | Failai | Atsakomybė |
|-----------|---------|------------|
| Model | User.cs, Race.cs, RaceResult.cs, TextSnippet.cs | Duomenų struktūros, atspindinčios DB lenteles |
| View | Play.cshtml, MultiPlay.cshtml | Vartotojo sąsaja, HTML + JavaScript |
| Controller | RaceController.cs, AuthController.cs, ApiController.cs | Verslo logika, HTTP užklausų apdorojimas |
| Service | RaceService.cs | Pakartotinai naudojama logika (tekstų parinkimas, rezultatų saugojimas) |
| Hub | RaceHub.cs | SignalR WebSocket realaus laiko komunikacija |
| DbContext | AppDbContext.cs | EF Core ORM – ryšys su SQL Server DB |

## 2.1 Kaip veikia Controller'iai

Kiekvienas controller'is paveldi iš `Controller` bazinės klasės ir priima HTTP užklausas pagal maršruto šabloną, apibrėžtą `Startup.cs` faile:

```csharp
endpoints.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
```

Tai reiškia, kad URL `/Race/Play` nukreipiamas į `RaceController` klasės `Play()` metodą.

Dependency Injection mechanizmas automatiškai paduoda reikalingus servisus (`AppDbContext`, `RaceService`) per konstruktorių – taip controller'iams nereikia patiems kurti objektų.

## 2.2 Servisų sluoksnis (RaceService)

### GetRandomSnippetAsync – atsitiktinio teksto parinkimas

Metodas parenka tekstą iš DB, vengdamas paskutinių 3 žaidime naudotų fragmentų (pagal ID).

```csharp
query = query.Where(t => !lastIds.Contains(t.Id));
```

Tada apskaičiuojamas likusių tekstų skaičius, sugeneruojamas atsitiktinis indeksas ir parenkamas konkretus įrašas su:

```csharp
.Skip(index).FirstAsync()
```

Paskutiniai ID saugomi Session (solo žaidime) arba kambario objekte (multiplayer).

### SaveRaceResultAsync – rezultatų išsaugojimas

Metodas sukuria naują `RaceResult` objektą su visais statistikos duomenimis:

- WPM
- Tikslumas
- Laikas
- Klaidų skaičius

Objektas išsaugomas DB ir lenktynių `Race.Status` pakeičiamas į `"finished"`.

Metodas naudojamas tiek iš `RaceController` (solo), tiek iš `RaceHub` (multiplayer).

---

# 3. Duomenų bazė ir EF Core

Projektas naudoja Microsoft SQL Server duomenų bazę, pasiekiamą per Entity Framework Core ORM.

## 3.1 Duomenų modeliai

| Lentelė | Pagrindiniai laukai | Paskirtis |
|----------|-------------------|-----------|
| Users | Id, Username, Email, PasswordHash, CreatedAt | Vartotojų paskyros |
| TextSnippets | Id, Content, Language, WordCount | Spausdinimo tekstai |
| Races | Id, TextSnippetId, StartedAt, IsMultiplayer, Status | Kiekvienas žaidimo seansas |
| RaceResults | Id, UserId, RaceId, WPM, Accuracy, TimeSeconds, ErrorCount, CompletedAt | Žaidėjų rezultatai |

## 3.2 Duomenų gavimas iš DB

EF Core leidžia rašyti LINQ užklausas C# kalba, kurias automatiškai paverčia į SQL.

### Top 10 lyderių lenta

```csharp
var topResults = await _db.RaceResults
    .Include(r => r.User)
    .OrderByDescending(r => r.WPM)
    .ThenByDescending(r => r.Accuracy)
    .Take(10)
    .ToListAsync();
```

`.Include(r => r.User)` nurodo EF Core atlikti JOIN su Users lentele (eager loading), kad galėtume pasiekti vartotojo vardą.

`.ToListAsync()` vykdo asinchroninę SQL užklausą ir grąžina rezultatų sąrašą.

## 3.3 DB konfigūracija ir atsparumo nustatymai

```csharp
sqlOptions.EnableRetryOnFailure(
    maxRetryCount: 5,
    maxRetryDelay: TimeSpan.FromSeconds(30),
    errorNumbersToAdd: null);
```

Tai reiškia, kad laikinos DB klaidos bus automatiškai bandytos iki 5 kartų su 30 sekundžių pertrauka.

---

# 4. Vartotojų autentifikacija ir sesijos

## 4.1 Cookie autentikacija

Projektas naudoja ASP.NET Core integruotą Cookie autentikacijos mechanizmą vietoj JWT tokenų.

Registracijos arba prisijungimo metu `SignInUser()` metodas sukuria Claims sąrašą:

```csharp
var claims = new List<Claim> {
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new Claim(ClaimTypes.Name, user.Username)
};
```

### Cookie saugos nustatymai

- `HttpOnly = true` – JavaScript negali pasiekti cookie (XSS apsauga)
- `SecurePolicy = Always` – cookie siunčiamas tik per HTTPS
- `SameSite = Strict` – apsauga nuo CSRF atakų
- `ExpireTimeSpan = 7 dienos` – ilgalaikė sesija ("prisimink mane")

## 4.2 Slaptažodžių šifravimas

Slaptažodžiai niekada nesaugomi atvirai.

```csharp
user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
```

Tikrinant prisijungimą:

```csharp
BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
```

## 4.3 Sesija (Session)

Server-side sesijoje saugoma:

- UserId
- Username
- LastSnippetIds

Multiplayer kambaryje vartotojo vardas paimamas iš sesijos:

```csharp
HttpContext.Session.GetString("Username") ?? "Svečias"
```

## 4.4 Autorizacija

Rezultatų išsaugojimas solo žaidime apsaugotas:

```csharp
[HttpPost]
[Authorize]
public async Task<IActionResult> SaveResult([FromBody] RaceResultDto dto)
```

Neautorizuotų vartotojų užklausos automatiškai peradresuojamos į `/Auth/Login`.

---

# 5. SignalR – realaus laiko multiplayer

## 5.1 Kas yra SignalR

SignalR yra ASP.NET Core biblioteka, leidžianti palaikyti dvipusį realaus laiko ryšį tarp serverio ir klientų per WebSocket protokolą.

Hub registruojamas:

```csharp
endpoints.MapHub<RaceHub>("/racehub");
```

## 5.2 Kambarių sistema

```csharp
private static readonly Dictionary<string, RaceRoom> _rooms =
    new Dictionary<string, RaceRoom>();

private static readonly object _lock = new object();
```

`lock(_lock)` naudojamas apsaugoti nuo race condition situacijų.

## 5.3 Žaidimo eiga (SignalR žinučių srautas)

| Veiksmas | Client → Server | Server → Client |
|-----------|----------------|----------------|
| Prisijungimas | JoinRoom(roomId, username) | PlayerJoined, RoomState |
| Pasiruošimas | ToggleReady(roomId) | PlayerReadyStatus |
| Startas | StartRace(roomId) | RaceStarting |
| Progresas | UpdateProgress(...) | ProgressUpdate |
| Finišas | FinishRace(...) | PlayerFinished |
| Reset | ResetRoom(roomId) | RoomReset |
| Atsijungimas | OnDisconnectedAsync | PlayerLeft |

## 5.4 Host sistema

Pirmasis prisijungęs žaidėjas tampa hostu.

Jei hostas atsijungia:

```csharp
room.HostConnectionId = room.Players.First().ConnectionId;
```

## 5.5 Vietų skaičiavimas

```csharp
place = room.Players.Count(p => p.IsFinished) + 1;
```

Apsauga nuo pakartotinio finišo:

```csharp
if (player.IsFinished)
{
    return;
}
```

---

# 6. REST API (StatsApiController)

`ApiController.cs` eksponuoja du endpoint'us statistikos gavimui.

| Endpoint | Metodas | Aprašymas |
|-----------|----------|-----------|
| GET /api/stats/{username} | GetUserStats | Vartotojo statistika |
| GET /api/stats/top10 | GetTop10Races | Top 10 rezultatų |

Top10 endpoint'as naudoja dinaminę vietų priskyrimo logiką:

```csharp
.Select((race, index) => ...)
```

---

# 7. Duomenų srautai: Solo vs Multiplayer

## 7.1 Solo žaidimo srautas

1. Vartotojas atidaro `/Race/Play`
2. `RaceController.Play()` iš sesijos paima `LastSnippetIds`
3. `RaceService.GetRandomSnippetAsync()` parenka tekstą iš DB
4. Sukuriamas `Race` objektas DB
5. JavaScript skaičiuoja WPM ir tikslumą
6. AJAX POST į `/Race/SaveResult`
7. `RaceService.SaveRaceResultAsync()` išsaugo rezultatą

## 7.2 Multiplayer žaidimo srautas

1. Vartotojas prisijungia prie kambario
2. JavaScript prisijungia prie SignalR Hub
3. Kviečiamas `JoinRoom(roomId, username)`
4. Žaidėjai pažymi pasiruošimą
5. Hostas paleidžia lenktynes
6. Vyksta progreso atnaujinimai
7. Baigus siunčiamas `FinishRace()`

---

# 8. Startup.cs – programos konfigūracija

## ConfigureServices – servisų registracija

Pagrindiniai servisai:

- `AddControllersWithViews()`
- `AddSignalR()`
- `AddSession()`
- `AddDbContext<AppDbContext>()`
- `AddAuthentication(Cookie)`
- `AddScoped<RaceService>()`

## Configure – middleware konvejeris

```text
UseHttpsRedirection
→ UseStaticFiles
→ UseSession
→ UseRouting
→ UseAuthentication
→ UseAuthorization
→ UseEndpoints
```

`UseAuthentication` turi būti prieš `UseAuthorization`.

---

# 9. Santrauka

MonkeyRacer projektas demonstruoja pilnavertę ASP.NET Core MVC web aplikaciją su realaus laiko multiplayer funkcionalumu.

### Pagrindiniai techniniai sprendimai

- MVC architektūra aiškiai atskiria atsakomybes tarp modelių, rodinių ir controller'ių
- SignalR suteikia efektyvų dvipusį ryšį multiplayer lenktynėms be polling
- Entity Framework Core abstrakuoja SQL ir leidžia dirbti su DB kaip C# objektais
- Cookie autentikacija su BCrypt slaptažodžių hash'avimu užtikrina saugumą
- Servisų sluoksnis (`RaceService`) perkelia verslo logiką iš controller'ių
- In-memory state su thread-safe lock'ais leidžia greitus realaus laiko atnaujinimus

Projektas sėkmingai veikia su SQL Server duomenų baze ir palaiko kelių žaidėjų vienu metu lenktyniavimą realiu laiku.

---

**Technologijos:** ASP.NET Core MVC • SignalR • Entity Framework Core • SQL Server • Cookie Authentication • BCrypt
