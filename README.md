


MonkeyRacer
Techninė projekto ataskaita
ASP.NET Core MVC | SignalR | Entity Framework Core | SQL Server


 Santrauka
MonkeyRacer projektas demonstruoja pilnavertę ASP.NET Core MVC web aplikaciją su realaus laiko multiplayer funkcionalumu. Pagrindiniai techniniai sprendimai:

•	MVC architektūra aiškiai atskiria atsakomybes tarp modelių, rodinių ir controller'ių
•	SignalR suteikia efektyvų dvipusį ryšį multiplayer lenktynėms be polling
•	Entity Framework Core abstrakuoja SQL ir leidžia dirbti su DB kaip C# objektais
•	Cookie autentikacija su BCrypt slaptažodžių hash'avimu užtikrina saugumą
•	Servisų sluoksnis (RaceService) perkelia verslo logiką iš controller'ių, sumažindamas kodo kartojimąsi
•	In-memory state su thread-safe lock'ais leidžia greitus realaus laiko atnaujinimus

Projektas sėkmingai veikia su SQL Server duomenų baze ir palaiko kelių žaidėjų vienu metu lenktyniavimą realiu laiku.

2026 m.
 
1. Projekto apžvalga
MonkeyRacer – tai žiniatinklinė klaviatūros rinkimo greičio (spausdinimo) programa, sukurta ASP.NET Core MVC platformoje su .NET 3.1. Projekto tikslas – realaus laiko daugiau žaidėjų lenktyniavimas, statistikos kaupimas ir rezultatų saugojimas SQL Server duomenų bazėje.

Pagrindinės projekto galimybės:
•	Solo žaidimas – atsitiktiniai lietuviški tekstų fragmentai, WPM ir tikslumo skaičiavimas
•	Multiplayer žaidimas – realaus laiko lenktynės per SignalR WebSocket
•	Vartotojų autentikacija – registracija, prisijungimas, cookie sesija
•	Lyderių lenta – Top 10 rezultatų per REST API
•	Statistikos peržiūra – WPM, tikslumas, žaidimų istorija

2. MVC architektūros struktūra
Projektas remiasi klasikinės MVC (Model-View-Controller) architektūra, kur atsakomybės yra aiškiai atskirtos:

Sluoksnis	Failai	Atsakomybė
Model	User.cs, Race.cs, RaceResult.cs, TextSnippet.cs	Duomenų struktūros, atspindinčios DB lenteles
View	Play.cshtml, MultiPlay.cshtml	Vartotojo sąsaja, HTML + JavaScript
Controller	RaceController.cs, AuthController.cs, ApiController.cs	Verslo logika, HTTP užklausų apdorojimas
Service	RaceService.cs	Pakartotinai naudojama logika (tekstų parinkimas, rezultatų saugojimas)
Hub	RaceHub.cs	SignalR WebSocket realaus laiko komunikacija
DbContext	AppDbContext.cs	EF Core ORM – ryšys su SQL Server DB

2.1 Kaip veikia Controller'iai
Kiekvienas controller'is paveldi iš Controller bazinės klasės ir priima HTTP užklausas pagal maršruto šabloną, apibrėžtą Startup.cs faile:

endpoints.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

Tai reiškia, kad URL /Race/Play nukreipiamas į RaceController klasės Play() metodą. Dependency Injection mechanizmas automatiškai paduoda reikalingus servisus (AppDbContext, RaceService) per konstruktorių – taip controller'iams nereikia patiems kurti objektų.

2.2 Servisų sluoksnis (RaceService)
RaceService atsakingas už dvi pagrindines operacijas, kurios naudojamos tiek solo, tiek multiplayer žaidime:

GetRandomSnippetAsync – atsitiktinio teksto parinkimas
Metodas parenka tekstą iš DB, vengdamas paskutinių 3 žaidime naudotų fragmentų (pagal ID). Tai pasiekiama filtracija per LINQ:
query = query.Where(t => !lastIds.Contains(t.Id));
Tada apskaičiuojamas likusių tekstų skaičius, sugeneruojamas atsitiktinis indeksas ir parenkamas konkretus įrašas su .Skip(index).FirstAsync(). Paskutiniai ID saugomi Session (solo žaidime) arba kambario objekte (multiplayer).

SaveRaceResultAsync – rezultatų išsaugojimas
Metodas sukuria naują RaceResult objektą su visais statistikos duomenimis (WPM, tikslumas, laikas, klaidų skaičius) ir jį išsaugo DB. Tuo pačiu metu pakeičia lenktynių Race.Status į "finished". Metodas naudojamas tiek iš RaceController (solo), tiek iš RaceHub (multiplayer).

3. Duomenų bazė ir EF Core
Projektas naudoja Microsoft SQL Server duomenų bazę, pasiekiamą per Entity Framework Core ORM. Visi DB modeliai aprašyti User.cs faile, o AppDbContext.cs apibrėžia lenteles ir pradinius duomenis.

3.1 Duomenų modeliai
Lentelė	Pagrindiniai laukai	Paskirtis
Users	Id, Username, Email, PasswordHash, CreatedAt	Vartotojų paskyros
TextSnippets	Id, Content, Language, WordCount	Spausdinimo tekstai
Races	Id, TextSnippetId, StartedAt, IsMultiplayer, Status	Kiekvienas žaidimo seansas
RaceResults	Id, UserId, RaceId, WPM, Accuracy, TimeSeconds, ErrorCount, CompletedAt	Žaidėjų rezultatai

3.2 Duomenų gavimas iš DB
EF Core leidžia rašyti LINQ užklausas C# kalba, kurias automatiškai paverčia į SQL. Pavyzdžiui, Top 10 lyderių lenta ApiController.cs:
var topResults = await _db.RaceResults
    .Include(r => r.User)
    .OrderByDescending(r => r.WPM)
    .ThenByDescending(r => r.Accuracy)
    .Take(10)
    .ToListAsync();

.Include(r => r.User) nurodo EF Core atlikti JOIN su Users lentele (eager loading), kad galėtume pasiekti vartotojo vardą. .ToListAsync() vykdo asinchroninę SQL užklausą ir grąžina rezultatų sąrašą.

3.3 DB konfigūracija ir atsparumo nustatymai
Startup.cs registruojamas AppDbContext su SQL Server prisijungimu ir automatinio kartojimo (retry) politika:
sqlOptions.EnableRetryOnFailure(
    maxRetryCount: 5,
    maxRetryDelay: TimeSpan.FromSeconds(30),
    errorNumbersToAdd: null);

Tai reiškia, kad laikinos DB klaidos (pvz., ryšio pertraukimas) bus automatiškai bandytos iki 5 kartų su 30 sekundžių pertrauka – svarbu realaus laiko multiplayer scenarijui.
4. Vartotojų autentifikacija ir sesijos
4.1 Cookie autentikacija
Projektas naudoja ASP.NET Core integruotą Cookie autentikacijos mechanizmą vietoj JWT tokenų. Tai pasirinkta dėl paprastumo – cookie automatiškai tvarkomi naršyklėje ir siunčiami su kiekviena užklausa.

Registracijos arba prisijungimo metu SignInUser() metodas sukuria Claims sąrašą (vartotojo ID ir vardo), sujungia juos į ClaimsPrincipal ir išduoda HTTP-Only cookie:
var claims = new List<Claim> {
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new Claim(ClaimTypes.Name, user.Username)
};

Cookie konfigūruotas su saugos parametrais:
•	HttpOnly = true – JavaScript negali pasiekti cookie (XSS apsauga)
•	SecurePolicy = Always – cookie siunčiamas tik per HTTPS
•	SameSite = Strict – apsauga nuo CSRF atakų
•	ExpireTimeSpan = 7 dienos – ilgalaikė sesija ("prisimink mane")

4.2 Slaptažodžių šifravimas
Slaptažodžiai niekada nesaugomi atvirai. Naudojama BCrypt biblioteka, kuri automatiškai generuoja salt ir sukuria saugų hash:
user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
// Tikrinant prisijungimą:
BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)

4.3 Sesija (Session)
Be cookie, kai kurie duomenys saugomi server-side sesijoje:
•	UserId – prisijungusio vartotojo ID (naudojamas navigacijai)
•	Username – rodomas multiplayer kambaryje ("Svečias" jei neprisijungęs)
•	LastSnippetIds – paskutiniai 3 teksto fragmentų ID (solo žaidime, kad nesikartotų)

Multiplayer kambaryje vartotojo vardas paimamas iš sesijos: HttpContext.Session.GetString("Username") ?? "Svečias" – taip leidžiama žaisti ir neprisijungusiems.

4.4 Autorizacija
Resultatų išsaugojimas solo žaidime apsaugotas [Authorize] atributu – tik prisijungę vartotojai gali siųsti rezultatus:
[HttpPost]
[Authorize]
public async Task<IActionResult> SaveResult([FromBody] RaceResultDto dto)

Neautorizuotų vartotojų užklausos automatiškai peradresuojamos į /Auth/Login, kaip nurodyta Startup.cs konfigūracijoje.

5. SignalR – realaus laiko multiplayer
5.1 Kas yra SignalR
SignalR yra ASP.NET Core biblioteka, leidžianti palaikyti dvipusį realaus laiko ryšį tarp serverio ir klientų per WebSocket protokolą. Vietoj tradicinių HTTP užklausų (kur klientas klausia – serveris atsako), SignalR leidžia serveriui pačiam siųsti žinutes klientams.

Hub registruojamas Startup.cs:
endpoints.MapHub<RaceHub>("/racehub");

5.2 Kambarių sistema
RaceHub palaiko statinius in-memory žodynus kambariams ir žaidėjams:
private static readonly Dictionary<string, RaceRoom> _rooms = new Dictionary<string, RaceRoom>();
private static readonly object _lock = new object();

lock(_lock) naudojamas apsaugoti nuo race condition – kai keli žaidėjai vienu metu bando keisti tą patį kambario būseną (pvz., prisijungti tuo pačiu metu). Tai kritiškai svarbu, nes SignalR apdoroja kelias jungtis lygiagrečiai.

5.3 Žaidimo eiga (SignalR žinučių srautas)
Veiksmas	Client → Server	Server → Client
Prisijungimas	JoinRoom(roomId, username)	PlayerJoined, RoomState
Pasiruošimas	ToggleReady(roomId)	PlayerReadyStatus (visiems)
Startas	StartRace(roomId)	RaceStarting + tekstas (visiems)
Progreso atnaujinimas	UpdateProgress(roomId, %, wpm, word)	ProgressUpdate (visiems)
Finišas	FinishRace(roomId, wpm, accuracy, time, errors)	PlayerFinished + vieta (visiems)
Reset	ResetRoom(roomId)	RoomReset (visiems)
Atsijungimas	(automatinis OnDisconnectedAsync)	PlayerLeft + naujas host ID

5.4 Host sistema
Pirmasis į kambarį prisijungęs žaidėjas automatiškai tampa hostu (HostConnectionId). Tik hostas gali pradėti lenktynes ir atstatyti kambarį. Jei hostas atsijungia, host teisės automatiškai perduodamos kitam žaidėjui:
room.HostConnectionId = room.Players.First().ConnectionId;

5.5 Vietų skaičiavimas
Finišo metu žaidėjo vieta apskaičiuojama pagal tai, kiek žaidėjų jau baigė prieš jį:
place = room.Players.Count(p => p.IsFinished) + 1;
Apsauga nuo pakartotino finišo: jei player.IsFinished jau true, naujas FinishRace iškvietimas ignoruojamas – taip išvengiama dvigubo rezultato įrašymo.

6. REST API (StatsApiController)
ApiController.cs eksponuoja du endpoint'us statistikos gavimui:

Endpoint	Metodas	Aprašymas
GET /api/stats/{username}	GetUserStats	Vartotojo statistika: iš viso žaidimų, geriausias WPM, vidutinis tikslumas
GET /api/stats/top10	GetTop10Races	Top 10 geriausių rezultatų iš visų žaidėjų

Top10 endpoint'as naudoja dinaminę vietų priskyrimo logiką – rezultatai rūšiuojami duomenų bazėje, o .Select((race, index) => ...) suteikia vietą (1–10) jau C# kode, o ne DB lygmenyje.

7. Duomenų srautai: Solo vs Multiplayer
7.1 Solo žaidimo srautas
•	Vartotojas atidaro /Race/Play
•	RaceController.Play() iš sesijos paima LastSnippetIds
•	RaceService.GetRandomSnippetAsync() parenka tekstą iš DB
•	Sukuriamas Race objektas DB, jo Id perduodamas per ViewBag.RaceId
•	JavaScript vartotojo sąsajoje fiksuoja WPM ir tikslumą
•	Baigus – AJAX POST į /Race/SaveResult su rezultatų DTO
•	RaceController.SaveResult() iš Claims paima UserId ir kviečia RaceService.SaveRaceResultAsync()

7.2 Multiplayer žaidimo srautas
•	Vartotojas sukuria/prisijungia prie kambario per /Race/MultiPlay?roomId=...
•	JavaScript prisijungia prie SignalR Hub per connection.start()
•	Kviečia JoinRoom(roomId, username) – serveris sukuria RaceRoom atmintyje
•	Visi žaidėjai pažymi IsReady = true per ToggleReady()
•	Hostas kviečia StartRace() – serveris parenka tekstą ir transliuoja visiems per RaceStarting
•	Spausdinant – UpdateProgress() siunčiamas kas tam tikrą laiko tarpą, visiems matomas progresas
•	Baigus – FinishRace() praneša visiems apie žaidėjo baigimą, bet duomenų nesaugo.

8. Startup.cs – programos konfigūracija
Startup.cs yra centrinis konfigūracijos failas, susidedantis iš dviejų metodų:

ConfigureServices – servisų registracija
Registruoja visus priklausomuosius servisus į DI konteinerį. Svarbiausi:
•	AddControllersWithViews() – MVC palaikymas
•	AddSignalR() – WebSocket hub'ai
•	AddSession() – server-side sesija
•	AddDbContext<AppDbContext>() – EF Core su SQL Server
•	AddAuthentication(Cookie) – cookie autentikacija
•	AddScoped<RaceService>() – servisas, sukuriamas vienai HTTP užklausai

Configure – middleware konvejeris
Apibrėžia, kokia tvarka apdorojamos HTTP užklausos. Tvarka kritiškai svarbi:
UseHttpsRedirection → UseStaticFiles → UseSession
→ UseRouting → UseAuthentication → UseAuthorization → UseEndpoints

UseAuthentication privalo eiti prieš UseAuthorization – pirma nustatoma tapatybė (kas esi?), tada tikrinamos teisės (ką gali?).

9. Santrauka
MonkeyRacer projektas demonstruoja pilnavertę ASP.NET Core MVC web aplikaciją su realaus laiko multiplayer funkcionalumu. Pagrindiniai techniniai sprendimai:

•	MVC architektūra aiškiai atskiria atsakomybes tarp modelių, rodinių ir controller'ių
•	SignalR suteikia efektyvų dvipusį ryšį multiplayer lenktynėms be polling
•	Entity Framework Core abstrakuoja SQL ir leidžia dirbti su DB kaip C# objektais
•	Cookie autentikacija su BCrypt slaptažodžių hash'avimu užtikrina saugumą
•	Servisų sluoksnis (RaceService) perkelia verslo logiką iš controller'ių, sumažindamas kodo kartojimąsi
•	In-memory state su thread-safe lock'ais leidžia greitus realaus laiko atnaujinimus

Projektas sėkmingai veikia su SQL Server duomenų baze ir palaiko kelių žaidėjų vienu metu lenktyniavimą realiu laiku.
