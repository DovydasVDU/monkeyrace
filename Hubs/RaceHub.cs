// Hubs/RaceHub.cs
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class RaceHub : Hub
{
    private readonly AppDbContext _db;
    private readonly RaceService _raceService;

    // Laikome visus aktyvius kambarius atmintyje (kol veikia serveris)
    private static readonly Dictionary<string, RaceRoom> _rooms = new Dictionary<string, RaceRoom>();

    // Lock naudojamas apsaugoti nuo žinučių dublikatų - kadangi tunelyje gali ateiti kelios vienu metu, jas sustatome į eilę.
    private static readonly object _lock = new object();

    public RaceHub(AppDbContext db, RaceService raceService)
    {
        _db = db;
        _raceService = raceService;
    }


    public async Task JoinRoom(string roomId, string username)
    {
        // Pridedam connection į SignalR kambarį
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

        // Bandome pasiimti UserId iš cookie claims
        int? userId = null;
        var userIdClaim = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (int.TryParse(userIdClaim, out var parsedId))
        {
            userId = parsedId;
        }

        // Sukuriam player objektą
        var player = new RoomPlayer
        {
            ConnectionId = Context.ConnectionId,
            Username = username,
            UserId = userId,
            Progress = 0,
            IsFinished = false,
            IsReady = false,
            JoinedAt = DateTime.UtcNow
        };

        RaceRoom room; // inicijuojamas kambarys

        lock (_lock)
        {
            // Jei kambario nėra – sukuriam naują
            if (!_rooms.ContainsKey(roomId))
            {
                _rooms[roomId] = new RaceRoom
                {
                    RoomId = roomId,
                    HostConnectionId = Context.ConnectionId
                };
            }

            room = _rooms[roomId]; // priskiriam jam id

            // Pridedam player į kambari
            // Pirmiausia patikriname, ar tas pats žaidėjas jau yra tame kambaryje
            if (userId != null && room.Players.Any(p => p.UserId == userId))
            {
                // Tai reiškia, kad tas pats prisijungęs user bando prisijungti antrą kartą per kitą tab'ą
                Clients.Caller.SendAsync("Error", "Jūs jau esate šiame kambaryje kitame lange.");
                return; // nutraukiame vykdymą – žaidėjas nepridedamas
            } 
            else
            {
                room.Players.Add(player);
            }

        }

        // Informuojam kitus žaidėjus
        await Clients.OthersInGroup(roomId).SendAsync("PlayerJoined", new
        {
            connectionId = Context.ConnectionId,
            username = username,
            isReady = false
        });

        // Nusiunčiam pilną kambario state tam naujam žaidėjui
        await Clients.Caller.SendAsync("RoomState", new
        {
            hostConnectionId = room.HostConnectionId,
            isStarted = room.IsStarted,
            players = room.Players.Select(p => new
            {
                connectionId = p.ConnectionId,
                username = p.Username,
                progress = p.Progress,
                wpm = p.CurrentWPM,
                word = p.CurrentWord,
                isReady = p.IsReady
            })
        });
    }

    // ready mygtukas. Jeigu visi žaidėjai įjungę ready, host gali pradėti lenktynes
    public async Task ToggleReady(string roomId)
    {
        bool isReady = false;

        lock (_lock)
        {
            if (_rooms.TryGetValue(roomId, out var room))
            {
                var player = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);

                if (player != null)
                {
                    player.IsReady = !player.IsReady;
                    isReady = player.IsReady;
                }
            }
        }
        // jeigu user paspaudė ready, tai siunčiam kitiems žaidėjams
        await Clients.Group(roomId).SendAsync("PlayerReadyStatus", new
        {
            connectionId = Context.ConnectionId,
            isReady
        });
    }

    public async Task StartRace(string roomId)
    {
        string textSnippet = "";
        Race race = null;

        lock (_lock)
        {
            // TryGetValue patikrina, ar roomId egzistuoja RaceRoom žodyne _rooms 
            if (_rooms.TryGetValue(roomId, out var room))
            {
                // Tik host gali startinti
                if (room.HostConnectionId != Context.ConnectionId) return;

                // Visi turi būti ready
                if (!room.Players.All(p => p.IsReady)) return;

                room.IsStarted = true;
            }
        }
        if (_rooms.TryGetValue(roomId, out var validRoom))
        {
            // Paimam tekstą iš RaceService
            var snippet = await _raceService.GetRandomSnippetAsync(new List<int>());

            validRoom.SnippetContent = snippet.Content;

            // Sukuriam race įrašą DB
            race = new Race
            {
                TextSnippetId = snippet.Id,
                StartedAt = DateTime.UtcNow,
                IsMultiplayer = true
            };

            _db.Races.Add(race);
            await _db.SaveChangesAsync();

            // Išsaugom raceId kambaryje
            validRoom.RaceId = race.Id;

            textSnippet = snippet.Content;
        }

        // Pradedam lenktynes klientuose
        if (!string.IsNullOrEmpty(textSnippet) && race != null)
        {
            await Clients.Group(roomId).SendAsync("RaceStarting", new
            {
                text = textSnippet,
                raceId = race.Id
            });
        }
    }
    // siunčiami duomenys kitiems žaidėjams, kad atnaujintų progress barą, wpm ir dabartinį žodį
     
    public async Task UpdateProgress(string roomId, int progressPercent, int currentWpm, string currentWord)
    {
        lock (_lock)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return;

            var player = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);

            if (player != null)
            {
                player.Progress = progressPercent;
                player.CurrentWPM = currentWpm;
                player.CurrentWord = currentWord;
            }
        }
        // kiekvienas žaidėjas siunčia visems .group
        await Clients.Group(roomId).SendAsync("ProgressUpdate", new
        {
            connectionId = Context.ConnectionId,
            progress = progressPercent,
            wpm = currentWpm,
            word = currentWord
        });
    }


    public async Task FinishRace(string roomId, int wpm, double accuracy, int timeSeconds, int errorCount)
    {
        
        int place = 1;
        // kai race pasibaigia, reikia išvalyti kambarį
        RaceRoom room = null;
        RoomPlayer player = null;

        lock (_lock)
        {
            if (_rooms.TryGetValue(roomId, out room))
            {
                // Apskaičiuojam vietą (kiek jau baigė prieš tave)
                place = room.Players.Count(p => p.IsFinished) + 1;

                player = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);

                if (player != null)
                {
                    // jeigu pakartotinai ateina IsFnished, tai mes jį atmetame
                    if (player.IsFinished) return;

                    player.IsFinished = true;
                    player.Progress = 100;
                    player.WPM = wpm;
                    player.Place = place;
                }
            }
        }

        // Išsaugom rezultatą DB (NE lock viduje)
        if (room != null && player != null && player.UserId != null)
        {
            await _raceService.SaveRaceResultAsync(player.UserId.Value, room.RaceId, wpm, accuracy, timeSeconds, errorCount);
        }

        // Informuojam visus žaidėjus
        await Clients.Group(roomId).SendAsync("PlayerFinished", new
        {
            connectionId = Context.ConnectionId,
            place,
            wpm
        });
    }

    // grįžti į lobby mygtukas. Jis išvalo žaidėjo progressą, ready statusą ir kitus duomenis, kad jis galėtų pasiruošti naujoms lenktynėms
    // reset tai palieka visus žaidėjus, tačiau išvalo tik pačių lenktynių duomenis, kambarys lieka
    public async Task ResetRoom(string roomId)
    {
        lock (_lock)
        {
            if (_rooms.TryGetValue(roomId, out var room))
            {
                if (room.HostConnectionId != Context.ConnectionId) return;

                room.IsStarted = false;

                foreach (var p in room.Players)
                {
                    p.IsReady = false;
                    p.Progress = 0;
                    p.IsFinished = false;
                    p.WPM = 0;
                    p.CurrentWPM = 0;
                    p.CurrentWord = "";
                    p.Place = 0;
                }
            }
        }

        await Clients.Group(roomId).SendAsync("RoomReset");
    }
    // kai atsijungia žaidėjas
    public override async Task OnDisconnectedAsync(Exception exception)
    {
        string roomToNotify = null;
        string newHostId = null;

        lock (_lock)
        {
            foreach (var kvp in _rooms)
            {
                var room = kvp.Value;
                var player = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId); // tikrinamas šio prisijungimo id su įrašytais ids

                if (player != null)
                {
                    room.Players.Remove(player);
                    roomToNotify = room.RoomId; // dabartinis kambarys, kuriam siųsim žinutę

                    // Jei tuščias kambarys – ištrinam
                    if (room.Players.Count == 0)
                    {
                        _rooms.Remove(room.RoomId);
                    }
                    // Jei host išėjo – paskiriam naują
                    else if (room.HostConnectionId == Context.ConnectionId)
                    {
                        room.HostConnectionId = room.Players.First().ConnectionId;
                        newHostId = room.HostConnectionId;
                    }

                    break;
                }
            }
        }

        if (roomToNotify != null)
        {
            await Clients.Group(roomToNotify).SendAsync("PlayerLeft", new
            {
                connectionId = Context.ConnectionId,
                newHostId
            });
        }

        await base.OnDisconnectedAsync(exception);
    }
}

// duomenų struktūros, kurios laiko info apie kambarius ir žaidėjus atmintyje
public class RaceRoom
{
    public string RoomId { get; set; }
    public string HostConnectionId { get; set; }
    public List<RoomPlayer> Players { get; set; } = new List<RoomPlayer>();
    public bool IsStarted { get; set; }
    public string SnippetContent { get; set; }
    public int RaceId { get; set; }
}

public class RoomPlayer
{
    public string ConnectionId { get; set; }
    public string Username { get; set; }

    // int? – nes gali būti neprisijungęs user, neturėti id
    public int? UserId { get; set; }

    public int Progress { get; set; }
    public bool IsReady { get; set; }
    public bool IsFinished { get; set; }
    public int WPM { get; set; }
    public int CurrentWPM { get; set; }
    public string CurrentWord { get; set; }
    public int Place { get; set; }
    public DateTime JoinedAt { get; set; }
}


