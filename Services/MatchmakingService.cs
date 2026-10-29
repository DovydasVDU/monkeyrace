using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed class MatchmakingService
{
    private static readonly string[] BotNames =
        { "Bambukas", "Koko", "Mango", "Pupa" };
    private static readonly TimeSpan LobbyDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReconnectWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FinishedRoomLifetime = TimeSpan.FromMinutes(5);
    // Lenktynių laikas pagal teksto ilgį: lėčiausias numatytas žaidėjas (15 WPM) spėja baigti, bet ne trumpiau nei 2 min.
    private const double SlowestWpm = 15;
    private static TimeSpan RaceTimeoutFor(string text) =>
        TimeSpan.FromMinutes(Math.Max(2, text.Length / 5d / SlowestWpm));
    private readonly object _sync = new object();
    private readonly Dictionary<string, RaceRoom> _rooms = new Dictionary<string, RaceRoom>();
    private readonly IHubContext<RaceHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MatchmakingService> _logger;
    private readonly BotSkillProvider _botSkills;
    private readonly IOptions<BotOptions> _botOptions;

    public MatchmakingService(
        IHubContext<RaceHub> hub,
        IServiceScopeFactory scopeFactory,
        ILogger<MatchmakingService> logger,
        BotSkillProvider botSkills,
        IOptions<BotOptions> botOptions)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _botSkills = botSkills;
        _botOptions = botOptions;
    }

    private void RemoveExpiredReservations()
    {
        foreach (var room in _rooms.Values.ToList())
        {
            room.Players.RemoveAll(player =>
                player.ConnectionId == null &&
                player.DisconnectedAtUtc.HasValue &&
                DateTime.UtcNow - player.DisconnectedAtUtc.Value > ReconnectWindow);
            if (room.Players.Count == 0 && room.Status == RaceRoomStatus.Lobby)
                _rooms.Remove(room.RaceId);
        }
    }

    private RaceRoom FindActiveRoom(string playerKey)
    {
        RemoveExpiredReservations();
        // finišavęs (ar DNF) žaidėjas laukti kitų neprivalo: „Žaisti dar kartą“ veda į naujas lenktynes
        var existing = _rooms.Values.FirstOrDefault(room =>
            room.Status != RaceRoomStatus.Finished &&
            room.Players.Any(player => player.PlayerKey == playerKey &&
                !player.IsFinished && !player.IsDnf));
        if (existing != null)
            _logger.LogInformation(
                "Matchmaking room={RoomId} player={PlayerKey} decision=restore-active",
                existing.RaceId, playerKey);
        return existing;
    }

    // Privatus kambarys tik su botais: niekas kitas neprisijungia, countdown prasideda vos žaidėjas prisijungia
    public string CreateBotRace(string playerKey, string username, int? userId, PlayerCard card,
        BotLevel[] levels)
    {
        lock (_sync)
        {
            var existing = FindActiveRoom(playerKey);
            if (existing != null)
                return existing.RaceId;

            var room = new RaceRoom
            {
                RaceId = Guid.NewGuid().ToString("N"),
                BotLevels = levels,
                LobbyDeadlineUtc = DateTime.UtcNow.Add(LobbyDuration)
            };
            room.Players.Add(new RacePlayer
            {
                PlayerKey = playerKey,
                Username = username,
                UserId = userId,
                Card = card
            });
            _rooms.Add(room.RaceId, room);
            // jei žaidėjas taip ir neprisijungs, lobby laikmatis kambarį išvalys
            _ = StartLobbyTimerAsync(room.RaceId);
            return room.RaceId;
        }
    }

    // Kambarys su draugais: be painių simbolių (0/O, 1/I), kad kodą būtų lengva padiktuoti
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const int CodeLength = 5;
    // per tiek laiko rezervavęs žaidėjas turi atidaryti lenktynių puslapį, kitaip išmetamas iš lobby
    private static readonly TimeSpan JoinWindow = TimeSpan.FromSeconds(30);

    public string CodeOf(string raceId)
    {
        lock (_sync)
            return _rooms.TryGetValue(raceId, out var room) ? room.Code : null;
    }

    public static bool IsValidCode(string code) =>
        code?.Length == CodeLength && code.All(CodeAlphabet.Contains);

    public (string RaceId, string Error) CreateFriendRoom(
        string playerKey, string username, int? userId, PlayerCard card)
    {
        lock (_sync)
        {
            var active = LeaveOtherLobby(playerKey, null);
            if (active != null)
                return (active, null);
            string code;
            do
                code = new string(Enumerable.Range(0, CodeLength)
                    .Select(_ => CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)]).ToArray());
            while (_rooms.Values.Any(room => room.Code == code));
            return (AddToFriendRoom(NewFriendRoom(code), playerKey, username, userId, card), null);
        }
    }

    // Klaidos: NotFound, Started, Full. Pasibaigusio kambario kodu sukuriamas naujas lobby (revanšas).
    public (string RaceId, string Error) JoinFriendRoom(
        string code, string playerKey, string username, int? userId, PlayerCard card)
    {
        lock (_sync)
        {
            var active = FindActiveRoom(playerKey);
            if (active != null && (active.Status != RaceRoomStatus.Lobby || active.Code == code))
                return (active.RaceId, null);
            var sameCode = _rooms.Values.Where(room => room.Code == code).ToList();
            if (sameCode.Count == 0)
                return (null, "NotFound");
            var room = sameCode.FirstOrDefault(item => item.Status == RaceRoomStatus.Lobby);
            // vykstančiose lenktynėse nedalyvavęs laukia jų pabaigos; dalyvavęs (jau finišavęs) kuria revanšo lobby
            if (room?.CountdownScheduled == true || (room == null && sameCode.Any(item =>
                    item.Status != RaceRoomStatus.Finished &&
                    item.Players.All(player => player.PlayerKey != playerKey))))
                return (null, "Started");
            if (room?.Players.Count >= 4)
                return (null, "Full");
            // iš kito lobby išimam tik tada, kai prisijungti tikrai pavyks; naujas kambarys kuriamas po to,
            // nes FindActiveRoom (RemoveExpiredReservations) ištrintų dar tuščią lobby
            LeaveOtherLobby(playerKey, code);
            return (AddToFriendRoom(room ?? NewFriendRoom(code), playerKey, username, userId, card), null);
        }
    }

    // Grąžina kambarį, į kurį žaidėją reikia grąžinti (vykstančios lenktynės ar tas pats kambarys);
    // iš kito lobby išima, nes žaidėjas pasirinko draugų kambarį
    private string LeaveOtherLobby(string playerKey, string code)
    {
        var active = FindActiveRoom(playerKey);
        if (active == null)
            return null;
        if (active.Status != RaceRoomStatus.Lobby || (code != null && active.Code == code))
            return active.RaceId;
        active.Players.RemoveAll(player => player.PlayerKey == playerKey);
        if (active.Players.Count == 0)
            _rooms.Remove(active.RaceId);
        else
            _ = BroadcastStateAsync(active);
        return null;
    }

    private RaceRoom NewFriendRoom(string code)
    {
        var room = new RaceRoom { RaceId = Guid.NewGuid().ToString("N"), Code = code };
        _rooms.Add(room.RaceId, room);
        return room;
    }

    private string AddToFriendRoom(RaceRoom room, string playerKey, string username,
        int? userId, PlayerCard card)
    {
        room.Players.Add(new RacePlayer
        {
            PlayerKey = playerKey,
            Username = username,
            UserId = userId,
            Card = card
        });
        _ = PurgeFriendLobbyLaterAsync(room.RaceId, JoinWindow + TimeSpan.FromSeconds(1));
        _logger.LogInformation(
            "Matchmaking room={RoomId} code={Code} player={PlayerKey} decision=friend-reserved",
            room.RaceId, room.Code, playerKey);
        return room.RaceId;
    }

    // Draugų lobby neturi laikmačio: išmetami neatėję (JoinWindow) ir atsijungę (ReconnectWindow)
    private async Task PurgeFriendLobbyLaterAsync(string raceId, TimeSpan delay)
    {
        await Task.Delay(delay);
        RaceRoom room;
        lock (_sync)
        {
            if (!_rooms.TryGetValue(raceId, out room) || room.Status != RaceRoomStatus.Lobby)
                return;
            var removed = room.Players.RemoveAll(player => player.ConnectionId == null &&
                (player.DisconnectedAtUtc.HasValue
                    ? DateTime.UtcNow - player.DisconnectedAtUtc.Value > ReconnectWindow
                    : DateTime.UtcNow - player.ReservedAtUtc > JoinWindow));
            if (room.Players.Count == 0)
            {
                _rooms.Remove(raceId);
                return;
            }
            if (removed == 0)
                return;
        }
        await BroadcastStateAsync(room);
    }

    // null – pradėta; kitaip klaidos tekstas šeimininkui
    public string StartFriendRace(string raceId, string playerKey)
    {
        lock (_sync)
        {
            if (!_rooms.TryGetValue(raceId, out var room) || room.Code == null ||
                room.Status != RaceRoomStatus.Lobby || room.CountdownScheduled)
                return "Lenktynės jau prasidėjo.";
            if (room.Players.FirstOrDefault()?.PlayerKey != playerKey)
                return "Pradėti gali tik kambario šeimininkas.";
            if (room.Players.Count(player => player.ConnectionId != null) < 2)
                return "Reikia bent 2 prisijungusių žaidėjų.";
            // kas šiuo metu neprisijungęs, lenktynėse nedalyvauja (kitaip gautų DNF)
            room.Players.RemoveAll(player => player.ConnectionId == null);
            room.CountdownScheduled = true;
        }
        // nelaukiam countdown pabaigos, kad šeimininko užklausa grįžtų iškart
        _ = StartCountdownAsync(raceId);
        return null;
    }

    // durationSeconds > 0: žodžių režimas (WordService), 0: klasikinis tekstas iki finišo
    public string JoinOrCreate(string playerKey, string username, int? userId, PlayerCard card,
        int durationSeconds = 0)
    {
        lock (_sync)
        {
            var existing = FindActiveRoom(playerKey);
            if (existing != null)
                return existing.RaceId;

            var room = _rooms.Values.FirstOrDefault(candidate =>
                candidate.Status == RaceRoomStatus.Lobby &&
                candidate.BotLevels == null && candidate.Code == null &&
                candidate.DurationSeconds == durationSeconds &&
                candidate.Players.Count < 4 &&
                candidate.LobbyDeadlineUtc - DateTime.UtcNow >= TimeSpan.FromSeconds(1));
            if (room == null)
            {
                room = new RaceRoom
                {
                    RaceId = Guid.NewGuid().ToString("N"),
                    DurationSeconds = durationSeconds,
                    LobbyDeadlineUtc = DateTime.UtcNow.Add(LobbyDuration)
                };
                _rooms.Add(room.RaceId, room);
                _ = StartLobbyTimerAsync(room.RaceId);
            }

            room.Players.Add(new RacePlayer
            {
                PlayerKey = playerKey,
                Username = username,
                UserId = userId,
                Card = card
            });

            _logger.LogInformation(
                "Matchmaking room={RoomId} player={PlayerKey} decision=reserved status={Status}",
                room.RaceId, playerKey, room.Status);
            return room.RaceId;
        }
    }

    public async Task<RaceStateResult> JoinRaceAsync(
        string raceId,
        string playerKey,
        string tabId,
        string connectionId)
    {
        RaceRoom room;
        RacePlayer player;
        var startFullRoom = false;
        var replacedConnectionId = (string)null;
        var sameTabReconnect = false;
        lock (_sync)
        {
            if (!_rooms.TryGetValue(raceId, out room))
            {
                _logger.LogInformation(
                    "Matchmaking room={RoomId} player={PlayerKey} decision=reject-not-found",
                    raceId, playerKey);
                return RaceStateResult.Failure("RaceNotFound");
            }

            player = room.Players.FirstOrDefault(item => item.PlayerKey == playerKey);
            if (player == null)
            {
                _logger.LogInformation(
                    "Matchmaking room={RoomId} player={PlayerKey} decision=reject-not-member status={Status}",
                    raceId, playerKey, room.Status);
                return RaceStateResult.Failure("RaceUnavailable");
            }
            if (room.Status == RaceRoomStatus.Finished)
            {
                _logger.LogInformation(
                    "Matchmaking room={RoomId} player={PlayerKey} decision=reject-finished",
                    raceId, playerKey);
                return RaceStateResult.Failure("RaceFinished");
            }

            if (!string.IsNullOrEmpty(player.ConnectionId) &&
                player.ConnectionId != connectionId &&
                player.TabId != tabId)
            {
                _logger.LogInformation(
                    "Matchmaking room={RoomId} player={PlayerKey} decision=reject-duplicate-connection",
                    raceId, playerKey);
                return RaceStateResult.Failure("AlreadyConnected");
            }
            if (!string.IsNullOrEmpty(player.ConnectionId) &&
                player.ConnectionId != connectionId &&
                player.TabId == tabId)
            {
                replacedConnectionId = player.ConnectionId;
                sameTabReconnect = true;
            }
            if (player.ConnectionId == null &&
                player.DisconnectedAtUtc.HasValue &&
                DateTime.UtcNow - player.DisconnectedAtUtc.Value > ReconnectWindow)
            {
                _logger.LogInformation(
                    "Matchmaking room={RoomId} player={PlayerKey} decision=reject-reconnect-expired",
                    raceId, playerKey);
                return RaceStateResult.Failure("RaceUnavailable");
            }

            player.ConnectionId = connectionId;
            player.TabId = tabId;
            player.DisconnectedAtUtc = null;
            _logger.LogInformation(
                "Matchmaking room={RoomId} player={PlayerKey} decision={Decision} status={Status}",
                raceId, playerKey,
                sameTabReconnect ? "restore-same-tab" : "restore-member",
                room.Status);
            startFullRoom = room.Status == RaceRoomStatus.Lobby &&
                !room.CountdownScheduled &&
                (room.BotLevels != null || (room.Code == null && room.Players.Count == 4));
            if (startFullRoom)
                room.CountdownScheduled = true;
        }

        await _hub.Groups.AddToGroupAsync(connectionId, room.GroupName);
        if (!string.IsNullOrEmpty(replacedConnectionId))
            await _hub.Groups.RemoveFromGroupAsync(
                replacedConnectionId, room.GroupName);
        await _hub.Clients.Group(room.GroupName).SendAsync("PlayerJoined",
            new { player = ToPlayerState(player) });
        // draugų lobby visiems reikia naujo šeimininko ir žaidėjų sąrašo
        if (room.Code != null)
            await BroadcastStateAsync(room);
        if (startFullRoom)
            _ = StartCountdownAsync(room.RaceId);
        return RaceStateResult.Success(CreateState(room));
    }

    public async Task LeaveAsync(string raceId, string playerKey, string connectionId)
    {
        RaceRoom room;
        var removeRoom = false;
        lock (_sync)
        {
            if (!_rooms.TryGetValue(raceId, out room))
                return;

            var player = room.Players.FirstOrDefault(item =>
                item.PlayerKey == playerKey);
            if (player == null)
                return;

            if (room.Status == RaceRoomStatus.Lobby ||
                room.Status == RaceRoomStatus.Countdown)
            {
                room.Players.Remove(player);
                if (room.Players.Count == 0)
                    _rooms.Remove(raceId);
            }
            else if (room.Status == RaceRoomStatus.Racing)
            {
                player.ConnectionId = null;
                player.DisconnectedAtUtc = DateTime.UtcNow;
                // finišavusio rezultatas lieka ir įrašomas lenktynių pabaigoje
                if (!player.IsFinished)
                    player.IsDnf = true;
                removeRoom = !room.Players.Any(item =>
                    !item.IsBot && (item.IsFinished ||
                        (item.PlayerKey != playerKey && item.ConnectionId != null)));
            }
            if (removeRoom)
                _rooms.Remove(raceId);
        }

        await _hub.Groups.RemoveFromGroupAsync(connectionId, room.GroupName);
        if (removeRoom)
            return;
        await BroadcastStateAsync(room);
        if (room.Status == RaceRoomStatus.Racing)
            await FinishRoomIfCompleteAsync(room);
    }

    public async Task DisconnectAsync(string connectionId)
    {
        RaceRoom room = null;
        RacePlayer player = null;
        lock (_sync)
        {
            foreach (var candidate in _rooms.Values)
            {
                var found = candidate.Players.FirstOrDefault(item =>
                    item.ConnectionId == connectionId);
                if (found == null)
                    continue;
                room = candidate;
                player = found;
                break;
            }

            if (room == null)
                return;

            if (room.Status == RaceRoomStatus.Lobby ||
                room.Status == RaceRoomStatus.Countdown)
            {
                // Dar galima grįžti (pvz. perkrovus puslapį); kas negrįš iki starto, gaus DNF
                player.ConnectionId = null;
                player.DisconnectedAtUtc = DateTime.UtcNow;
                if (room.Code != null)
                    _ = PurgeFriendLobbyLaterAsync(room.RaceId, ReconnectWindow + TimeSpan.FromSeconds(1));
            }
            else if (room.Status == RaceRoomStatus.Racing)
            {
                player.ConnectionId = null;
                player.DisconnectedAtUtc = DateTime.UtcNow;
                // finišavęs gali uždaryti puslapį nelaukdamas kitų – rezultatas neprarandamas
                if (!player.IsFinished)
                    player.IsDnf = true;
            }
        }

        await BroadcastStateAsync(room);
        if (room.Status == RaceRoomStatus.Racing)
            await FinishRoomIfCompleteAsync(room);
    }

    public async Task SendProgressAsync(
        string raceId, string playerKey, string connectionId,
        int correctChars, int totalKeystrokes, int wrongKeystrokes, string typing = null)
    {
        RaceRoom room;
        RacePlayer player;
        lock (_sync)
        {
            if (!_rooms.TryGetValue(raceId, out room) ||
                room.Status != RaceRoomStatus.Racing)
                return;

            player = room.Players.FirstOrDefault(item =>
                item.PlayerKey == playerKey && item.ConnectionId == connectionId);
            if (player == null || player.IsFinished || player.IsDnf)
                return;

            // Progresas negali augti greičiau nei RaceService.MaxWpm pagal serverio laikrodį
            var maxCharsByTime = (int)((DateTime.UtcNow - room.StartAtUtc).TotalMinutes *
                RaceService.MaxWpm * 5) + 5;
            var nextCorrectChars = Math.Max(0,
                Math.Min(Math.Min(correctChars, maxCharsByTime), room.Text.Length));
            if (nextCorrectChars > player.CorrectChars)
            {
                var reachedAtMs = Math.Max(0,
                    (int)(DateTime.UtcNow - room.StartAtUtc).TotalMilliseconds);
                for (var value = player.CorrectChars + 1;
                     value <= nextCorrectChars; value++)
                {
                    if (!player.FirstReachedAtMs.ContainsKey(value))
                        player.FirstReachedAtMs[value] = reachedAtMs;
                }
            }
            player.CorrectChars = nextCorrectChars;
            var clampedTotal = Math.Max(0, totalKeystrokes);
            var clampedWrong = Math.Max(0, Math.Min(wrongKeystrokes, clampedTotal));
            if (clampedTotal != totalKeystrokes ||
                clampedWrong != wrongKeystrokes ||
                clampedTotal < nextCorrectChars)
            {
                _logger.LogWarning(
                    "Race progress clamped room={RoomId} player={PlayerKey} correctChars={CorrectChars} totalKeystrokes={TotalKeystrokes} wrongKeystrokes={WrongKeystrokes}",
                    raceId, playerKey, nextCorrectChars, totalKeystrokes,
                    wrongKeystrokes);
            }
            clampedTotal = Math.Max(clampedTotal, nextCorrectChars);
            clampedWrong = Math.Min(clampedWrong, clampedTotal);
            player.TotalKeystrokes = Math.Max(player.TotalKeystrokes, clampedTotal);
            player.ErrorCount = Math.Max(player.ErrorCount, clampedWrong);
            if (player.ErrorCount > player.TotalKeystrokes)
                player.ErrorCount = player.TotalKeystrokes;
        }

        var elapsedSeconds = Math.Max(0.1,
            (DateTime.UtcNow - room.StartAtUtc).TotalSeconds);
        var wpm = (int)Math.Round(player.CorrectChars / 5d / (elapsedSeconds / 60d));
        var accuracy = AccuracyOf(player);

        if (player.CorrectChars >= room.Text.Length)
        {
            await FinishPlayerAsync(room, player, wpm, accuracy, elapsedSeconds);
            return;
        }

        await _hub.Clients.Group(room.GroupName).SendAsync("Progress", new
        {
            playerKey = player.PlayerKey,
            username = player.Username,
            correctChars = player.CorrectChars,
            totalKeystrokes = player.TotalKeystrokes,
            wrongKeystrokes = player.ErrorCount,
            progress = room.Text.Length == 0
                ? 0
                : player.CorrectChars * 100 / room.Text.Length,
            wpm,
            accuracy,
            // žodžių režime varžovai mato rašomą žodį; ilgis ribojamas, nes ateina iš naršyklės
            typing = (typing ?? "").Substring(0, Math.Min(30, (typing ?? "").Length))
            ,isBot = false
        });
    }

    private async Task StartLobbyTimerAsync(string raceId)
    {
        await Task.Delay(LobbyDuration);
        var shouldStart = false;
        lock (_sync)
        {
            if (_rooms.TryGetValue(raceId, out var room) &&
                room.Status == RaceRoomStatus.Lobby &&
                !room.CountdownScheduled)
            {
                room.Players.RemoveAll(item =>
                    item.ConnectionId == null &&
                    item.DisconnectedAtUtc.HasValue &&
                    DateTime.UtcNow - item.DisconnectedAtUtc.Value > ReconnectWindow);
                if (room.Players.Count(item =>
                        item.ConnectionId != null && !item.IsBot) == 0)
                {
                    _rooms.Remove(raceId);
                    return;
                }
                room.CountdownScheduled = true;
                shouldStart = true;
            }
        }

        if (shouldStart)
            await StartCountdownAsync(raceId);
    }

    private async Task StartCountdownAsync(string raceId)
    {
        RaceRoom room;
        TextSnippet snippet;
        lock (_sync)
        {
            if (!_rooms.TryGetValue(raceId, out room) ||
                room.Status != RaceRoomStatus.Lobby ||
                room.Players.Count(item =>
                    item.ConnectionId != null && !item.IsBot) == 0)
                return;
        }

        using (var scope = _scopeFactory.CreateScope())
        {
            Race race;
            // fone paleista užduotis: be catch DB klaida dingtų, o žaidėjai lauktų lobby amžinai
            try
            {
                var raceService = scope.ServiceProvider.GetRequiredService<RaceService>();
                snippet = room.DurationSeconds > 0
                    ? null
                    : await raceService.GetRandomSnippetAsync(new List<int>());
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                race = new Race
                {
                    TextSnippetId = snippet?.Id,
                    DurationSeconds = room.DurationSeconds > 0 ? room.DurationSeconds : null,
                    StartedAt = DateTime.UtcNow,
                    IsMultiplayer = room.BotLevels == null
                };
                db.Races.Add(race);
                await db.SaveChangesAsync();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Matchmaking room={RoomId} decision=countdown-failed", raceId);
                lock (_sync)
                    _rooms.Remove(raceId);
                await _hub.Clients.Group(room.GroupName).SendAsync("RaceUnavailable", "RaceUnavailable");
                return;
            }

            lock (_sync)
            {
                if (!_rooms.TryGetValue(raceId, out room) ||
                    room.Status != RaceRoomStatus.Lobby)
                    return;
                room.DatabaseRaceId = race.Id;
                room.Text = snippet?.Content ?? WordService.Generate(room.DurationSeconds);
                room.Snippet = snippet;
                room.StartAtUtc = DateTime.UtcNow.AddSeconds(3);
                var botOptions = _botOptions.Value;
                var humans = room.Players.Where(player => !player.IsBot).ToList();
                var desiredPlayers = humans.Count == 1
                    ? 4
                    : Math.Min(4, Math.Max(2, botOptions.FillToPlayers));
                var botsToAdd = room.BotLevels?.Length ?? (botOptions.Enabled && room.Code == null
                    ? Math.Max(0, desiredPlayers - humans.Count)
                    : 0);
                for (var botIndex = 0; botIndex < botsToAdd; botIndex++)
                {
                    var botKey = "bot:" + raceId + ":" + botIndex;
                    room.Players.Add(new RacePlayer
                    {
                        PlayerKey = botKey,
                        Username = BotNames[botIndex % BotNames.Length],
                        IsBot = true,
                        BotLevel = room.BotLevels?[botIndex],
                        ColorIndex = room.Players.Count
                    });
                }
                room.Status = RaceRoomStatus.Countdown;
            }
        }

        List<RacePlayer> bots;
        List<RacePlayer> humansForSkills;
        lock (_sync)
        {
            room = _rooms[raceId];
            bots = room.Players.Where(player => player.IsBot).ToList();
            humansForSkills = room.Players.Where(player => !player.IsBot).ToList();
        }
        for (var index = 0; index < bots.Count; index++)
        {
            var level = bots[index].BotLevel ?? BotLevel.Adaptive;
            var random = new Random(StableSeed(raceId, index));
            double targetWpm, accuracyMin, accuracyMax;
            if (level == BotLevel.Adaptive)
            {
                targetWpm = await _botSkills.GetTargetWpmAsync(
                    humansForSkills, index);
                var options = _botOptions.Value;
                accuracyMin = options.AccuracyRange?.Length > 0
                    ? options.AccuracyRange[0] : .92;
                accuracyMax = options.AccuracyRange?.Length > 1
                    ? options.AccuracyRange[1] : .98;
            }
            else
            {
                (targetWpm, accuracyMin, accuracyMax) = BotSkillProvider.Preset(level);
                // ±5 %, kad du vienodo lygio botai nevažiuotų greta
                targetWpm *= .95 + random.NextDouble() * .1;
            }
            var accuracy = accuracyMin +
                random.NextDouble() * Math.Max(0, accuracyMax - accuracyMin);
            bots[index].BotPlan = BotPlan.Create(
                room.Text, targetWpm, accuracy, StableSeed(raceId, index));
            bots[index].TargetWpm = targetWpm;
            bots[index].ErrorCount = bots[index].BotPlan.ErrorCount;
            bots[index].TotalKeystrokes = room.Text.Length + bots[index].ErrorCount;
            _logger.LogInformation(
                "Matchmaking room={RoomId} bots={BotCount} bot={BotName} targetWpm={TargetWpm}",
                raceId, bots.Count, bots[index].Username, targetWpm);
        }

        await _hub.Clients.Group(room.GroupName).SendAsync("Countdown", new
        {
            startAtUtc = room.StartAtUtc,
            serverNowUtc = DateTime.UtcNow,
            text = room.Text
        });
        foreach (var bot in bots)
            await _hub.Clients.Group(room.GroupName).SendAsync("PlayerJoined",
                new { player = ToPlayerState(bot) });
        await Task.Delay(Math.Max(0, (int)(room.StartAtUtc - DateTime.UtcNow).TotalMilliseconds));

        lock (_sync)
        {
            if (!_rooms.TryGetValue(raceId, out room) ||
                room.Status != RaceRoomStatus.Countdown)
                return;
            room.Status = RaceRoomStatus.Racing;
            // Atsijungę iki starto žaidėjai nebelaukiami, kitaip lenktynės lauktų iki laiko pabaigos
            foreach (var ghost in room.Players.Where(item =>
                !item.IsBot && item.ConnectionId == null))
                ghost.IsDnf = true;
        }

        await _hub.Clients.Group(room.GroupName).SendAsync("RaceStarted", new
        {
            startAtUtc = room.StartAtUtc,
            serverNowUtc = DateTime.UtcNow
        });
        _ = RecordTimelineAsync(raceId);
        _ = FinishOnTimeoutAsync(raceId, room.DurationSeconds > 0
            ? TimeSpan.FromSeconds(room.DurationSeconds)
            : RaceTimeoutFor(room.Text));
    }

    private async Task RecordTimelineAsync(string raceId)
    {
        while (true)
        {
            await Task.Delay(250);
            RaceRoom room;
            var botFinished = new List<RacePlayer>();
            var botProgress = new List<RacePlayer>();
            lock (_sync)
            {
                if (!_rooms.TryGetValue(raceId, out room) ||
                    room.Status != RaceRoomStatus.Racing)
                    return;

                var elapsedMs = Math.Max(0,
                    (int)(DateTime.UtcNow - room.StartAtUtc).TotalMilliseconds);
                foreach (var bot in room.Players.Where(player =>
                    player.IsBot && !player.IsFinished && player.BotPlan != null))
                {
                    bot.CorrectChars = bot.BotPlan.CorrectCharsAt(elapsedMs);
                    botProgress.Add(bot);
                    if (elapsedMs >= bot.BotPlan.FinishMs ||
                        bot.CorrectChars >= room.Text.Length)
                        botFinished.Add(bot);
                }
                var tick = new RaceTimelineTick
                {
                    AtMs = elapsedMs,
                    Values = room.Players.Where(player => !player.IsDnf).ToDictionary(
                        player => player.PlayerKey,
                        player => player.IsFinished && room.DurationSeconds == 0
                            ? room.Text.Length
                            : player.CorrectChars)
                };
                room.Timeline.Add(tick);
                foreach (var player in room.Players.Where(player => !player.IsDnf))
                {
                    if (!tick.Values.TryGetValue(player.PlayerKey, out var value))
                        continue;
                    if (!player.FirstReachedAtMs.ContainsKey(value))
                        player.FirstReachedAtMs[value] = elapsedMs;
                }
            }
            foreach (var bot in botProgress)
            {
                await _hub.Clients.Group(room.GroupName).SendAsync("Progress",
                    ToProgressState(room, bot, bot.CorrectChars,
                        bot.BotPlan.Accuracy * 100));
            }
            foreach (var bot in botFinished.Where(bot => !bot.IsFinished).ToList())
                await FinishPlayerAsync(room, bot, (int)Math.Round(bot.TargetWpm),
                    bot.BotPlan.Accuracy * 100, bot.BotPlan.FinishMs / 1000d);
        }
    }

    private async Task FinishOnTimeoutAsync(string raceId, TimeSpan timeout)
    {
        await Task.Delay(timeout);
        RaceRoom room;
        lock (_sync)
        {
            if (!_rooms.TryGetValue(raceId, out room) ||
                room.Status != RaceRoomStatus.Racing)
                return;
            foreach (var player in room.Players.Where(item =>
                !item.IsFinished && !item.IsDnf))
            {
                if (room.DurationSeconds == 0)
                {
                    player.IsDnf = true;
                    continue;
                }
                // žodžių režime laikas baigiasi visiems kartu: rezultatas – kiek spėjo parašyti
                if (player.IsBot && player.BotPlan != null)
                    player.CorrectChars = player.BotPlan.CorrectCharsAt(room.DurationSeconds * 1000);
                player.IsFinished = true;
                player.Wpm = (int)Math.Round(player.CorrectChars / 5d / (room.DurationSeconds / 60d));
                player.Accuracy = player.IsBot && player.BotPlan != null
                    ? player.BotPlan.Accuracy * 100
                    : AccuracyOf(player);
                player.TimeSeconds = room.DurationSeconds;
                player.FinishAtMs = room.DurationSeconds * 1000;
            }
        }
        await FinishRoomAsync(room);
    }

    private async Task FinishPlayerAsync(
        RaceRoom room, RacePlayer player, int wpm,
        double accuracy, double elapsedSeconds)
    {
        object timeline = null;
        lock (_sync)
        {
            if (player.IsFinished)
                return;
            player.IsFinished = true;
            player.Wpm = wpm;
            player.Accuracy = accuracy;
            player.TimeSeconds = Math.Max(1, (int)Math.Round(elapsedSeconds));
            player.FinishAtMs = Math.Max(0, (int)Math.Round(elapsedSeconds * 1000));
            player.Place = room.Players.Count(item =>
                !item.IsDnf &&
                (item.IsFinished
                    ? item.FinishAtMs
                    : item.IsBot && item.BotPlan != null
                        ? item.BotPlan.FinishMs
                        : int.MaxValue) <= player.FinishAtMs);
            // finišavęs žmogus iškart mato savo greičio grafiką; lock, nes RecordTimelineAsync pildo Timeline
            if (!player.IsBot)
                timeline = RaceAnalyzer.CreateTimeline(room);
        }
        if (!player.IsBot)
            _botSkills.RememberGuestWpm(player.PlayerKey, player.Wpm);
        await _hub.Clients.Group(room.GroupName).SendAsync("PlayerFinished",
            new
            {
                playerKey = player.PlayerKey,
                username = player.Username,
                place = player.Place,
                wpm = player.Wpm,
                accuracy = player.Accuracy,
                timeSeconds = player.TimeSeconds,
                errorCount = player.ErrorCount
                ,isBot = player.IsBot
                ,timeline
                ,textInfo = player.IsBot ? null : TextInfoOf(room)
            });
        await FinishRoomIfCompleteAsync(room);
    }

    private async Task FinishRoomIfCompleteAsync(RaceRoom room)
    {
        var finishBots = false;
        lock (_sync)
        {
            if (room.Completing)
                return;
            var humans = room.Players.Where(player => !player.IsBot).ToList();
            if (humans.Count == 0)
            {
                _rooms.Remove(room.RaceId);
                return;
            }
            if (humans.Any(player => !player.IsFinished && !player.IsDnf))
                return;
            room.Completing = true;
            // žodžių režime botų finišo nelaukiame: jie rikiuojami pagal tai, kiek spėjo parašyti
            finishBots = room.DurationSeconds == 0 &&
                room.Players.Any(player => player.IsBot && !player.IsFinished);
        }
        if (finishBots)
        {
            await FinishBotsAsync(room);
            return;
        }
        await FinishRoomAsync(room);
    }

    private async Task FinishBotsAsync(RaceRoom room)
    {
        foreach (var bot in room.Players.Where(player =>
            player.IsBot && !player.IsFinished).OrderBy(player =>
                player.BotPlan?.FinishMs ?? 0))
        {
            var elapsedMs = (DateTime.UtcNow - room.StartAtUtc).TotalMilliseconds;
            var waitMs = (bot.BotPlan?.FinishMs ?? 0) - elapsedMs;
            if (waitMs > 0)
                await Task.Delay((int)Math.Min(waitMs, RaceTimeoutFor(room.Text).TotalMilliseconds));
            if (bot.IsFinished)
                continue;
            await FinishPlayerAsync(room, bot, (int)Math.Round(bot.TargetWpm),
                (bot.BotPlan?.Accuracy ?? .95) * 100,
                (bot.BotPlan?.FinishMs ?? 0) / 1000d);
        }
        await FinishRoomAsync(room);
    }

    private async Task FinishRoomAsync(RaceRoom room)
    {
        lock (_sync)
        {
            if (room.Status == RaceRoomStatus.Finished)
                return;
            var ordered = room.Players
                .Where(player => !player.IsDnf)
                .OrderBy(player => room.DurationSeconds > 0
                    ? -player.CorrectChars
                    : player.FinishAtMs == 0 ? int.MaxValue : player.FinishAtMs)
                .ToList();
            for (var index = 0; index < ordered.Count; index++)
                ordered[index].Place = index + 1;
            RaceAnalyzer.AddTimelineTick(room, Math.Max(0,
                (int)(DateTime.UtcNow - room.StartAtUtc).TotalMilliseconds));
            room.Status = RaceRoomStatus.Finished;
        }

        using (var scope = _scopeFactory.CreateScope())
        {
            var raceService = scope.ServiceProvider.GetRequiredService<RaceService>();
            // lenktynės su botais skaičiuojamos kaip solo: be vietos premijos ir ne į online Top 10
            var mode = room.BotLevels == null ? RaceMode.Multiplayer : RaceMode.Solo;
            int? place(RacePlayer player) => mode == RaceMode.Solo ? null : player.Place;
            // DNF nerašome: 0 WPM įrašai gadintų vidurkius ir botų greičio parinkimą
            // Botai neapdovanojami ir nerašomi; svečiai gauna tik hipotetinį apdovanojimą rodymui
            foreach (var player in room.Players.Where(item =>
                !item.IsBot && !item.IsDnf && !item.ResultSaved))
            {
                player.ResultSaved = true;
                // vieno žaidėjo DB klaida neturi palikti kitų be RaceFinished
                try
                {
                    player.Reward = player.UserId.HasValue
                        ? await raceService.SaveRaceResultAsync(
                            player.UserId.Value, room.DatabaseRaceId,
                            mode, place(player),
                            player.Wpm, player.Accuracy, player.TimeSeconds,
                            player.ErrorCount)
                        : RaceService.GuestReward(mode, place(player),
                            player.Wpm, player.Accuracy,
                            room.DurationSeconds > 0 ? room.DurationSeconds : null);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Matchmaking room={RoomId} player={PlayerKey} decision=save-failed",
                        room.RaceId, player.PlayerKey);
                }
            }

            // žodžių režime finišo nėra, todėl replay nerodomas
            if (room.DurationSeconds == 0)
            {
                try
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var race = await db.Races.FindAsync(room.DatabaseRaceId);
                    if (race != null)
                    {
                        // numatytasis koduotojas pakeičia < > & į <..., todėl JSON saugu įdėti į <script>
                        race.ReplayJson = JsonSerializer.Serialize(RaceAnalyzer.CreateReplay(room),
                            new JsonSerializerOptions(JsonSerializerDefaults.Web));
                        await db.SaveChangesAsync();
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Matchmaking room={RoomId} decision=replay-save-failed", room.RaceId);
                }
            }
        }

        await _hub.Clients.Group(room.GroupName).SendAsync("RaceFinished", new
        {
            players = room.Players
                .OrderBy(player => player.IsDnf)
                .ThenBy(player => player.Place == 0 ? int.MaxValue : player.Place)
                .Select(ToResultState),
            timeline = RaceAnalyzer.CreateTimeline(room),
            analysis = RaceAnalyzer.CreateAnalysis(room),
            textLength = room.Text.Length,
            durationSeconds = room.DurationSeconds,
            textInfo = TextInfoOf(room)
        });
        _ = RemoveFinishedRoomAsync(room.RaceId);
    }

    // „Apie tekstą“; null žodžių režime
    private static object TextInfoOf(RaceRoom room) => room.Snippet == null ? null : new
    {
        id = room.Snippet.Id, // /api/stats/text/{id} – šio teksto lyderiai
        author = room.Snippet.Author,
        work = room.Snippet.Work,
        sourceUrl = room.Snippet.SourceUrl,
        license = room.Snippet.LicenseLabel,
        flags = room.Snippet.FlagLabels
    };

    private async Task RemoveFinishedRoomAsync(string raceId)
    {
        await Task.Delay(FinishedRoomLifetime);
        lock (_sync)
        {
            _rooms.Remove(raceId);
        }
    }

    private async Task BroadcastStateAsync(RaceRoom room)
    {
        if (room == null)
            return;
        await _hub.Clients.Group(room.GroupName).SendAsync(
            "RaceState", CreateState(room));
    }

    private RaceState CreateState(RaceRoom room)
    {
        return new RaceState
        {
            RaceId = room.RaceId,
            Status = room.Status.ToString(),
            LobbyDeadlineUtc = room.LobbyDeadlineUtc,
            StartAtUtc = room.StartAtUtc,
            ServerNowUtc = DateTime.UtcNow,
            DurationSeconds = room.DurationSeconds,
            Text = room.Status >= RaceRoomStatus.Countdown ? room.Text : null,
            Code = room.Code,
            HostKey = room.Code == null ? null : room.Players.FirstOrDefault()?.PlayerKey,
            Players = room.Players.Select(ToPlayerState).ToList()
        };
    }

    private static RacePlayerState ToPlayerState(RacePlayer player)
    {
        return new RacePlayerState
        {
            PlayerKey = player.PlayerKey,
            Username = player.Username,
            CorrectChars = player.CorrectChars,
            IsFinished = player.IsFinished,
            IsDnf = player.IsDnf,
            Wpm = player.Wpm
            ,IsBot = player.IsBot
            ,Card = player.Card?.ForOthers()
            ,BotWpm = player.IsBot ? (int)Math.Round(player.TargetWpm) : 0
            ,BotLevel = player.BotLevel?.ToString()
        };
    }

    private static RaceResultState ToResultState(RacePlayer player)
    {
        return new RaceResultState
        {
            PlayerKey = player.PlayerKey,
            Username = player.Username,
            Place = player.IsDnf ? 0 : player.Place,
            Wpm = player.Wpm,
            Accuracy = player.Accuracy,
            IsDnf = player.IsDnf,
            ErrorCount = player.ErrorCount,
            TimeSeconds = player.TimeSeconds
            ,IsBot = player.IsBot
            ,Reward = player.Reward
        };
    }

    private static object ToProgressState(
        RaceRoom room, RacePlayer player, int correctChars, double accuracy)
    {
        var elapsedSeconds = Math.Max(.1,
            (DateTime.UtcNow - room.StartAtUtc).TotalSeconds);
        var wpm = (int)Math.Round(correctChars / 5d / (elapsedSeconds / 60d));
        return new
        {
            playerKey = player.PlayerKey,
            username = player.Username,
            correctChars,
            progress = room.Text.Length == 0
                ? 0
                : correctChars * 100 / room.Text.Length,
            wpm,
            accuracy,
            totalKeystrokes = player.TotalKeystrokes,
            wrongKeystrokes = player.ErrorCount,
            typing = TypedPartOfWord(room.Text, correctChars),
            isBot = true
        };
    }

    // Kurią žodžio dalį botas jau "parašė": nuo paskutinio tarpo iki correctChars
    private static string TypedPartOfWord(string text, int correctChars)
    {
        correctChars = Math.Min(correctChars, text.Length);
        var start = correctChars == 0 ? 0 : text.LastIndexOf(' ', correctChars - 1) + 1;
        return text.Substring(start, correctChars - start);
    }

    private static double AccuracyOf(RacePlayer player) =>
        player.TotalKeystrokes == 0
            ? 100d
            : (player.TotalKeystrokes - player.ErrorCount) * 100d / player.TotalKeystrokes;

    private static int StableSeed(string raceId, int botIndex)
    {
        unchecked
        {
            var hash = 17;
            foreach (var character in raceId)
                hash = hash * 31 + character;
            return hash * 31 + botIndex;
        }
    }
}

public sealed class RaceStateResult
{
    public bool IsSuccess { get; private set; }
    public string Error { get; private set; }
    public RaceState State { get; private set; }

    public static RaceStateResult Success(RaceState state)
        => new RaceStateResult { IsSuccess = true, State = state };

    public static RaceStateResult Failure(string error)
        => new RaceStateResult { Error = error };
}

public sealed class RaceState
{
    public string RaceId { get; set; }
    public string Status { get; set; }
    public DateTime LobbyDeadlineUtc { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime ServerNowUtc { get; set; }
    public int DurationSeconds { get; set; }
    public string Text { get; set; }
    // tik draugų kambariui; šeimininkas – pirmas likęs žaidėjas
    public string Code { get; set; }
    public string HostKey { get; set; }
    public List<RacePlayerState> Players { get; set; } = new List<RacePlayerState>();
}

public sealed class RacePlayerState
{
    public string PlayerKey { get; set; }
    public string Username { get; set; }
    public int CorrectChars { get; set; }
    public bool IsFinished { get; set; }
    public bool IsDnf { get; set; }
    public bool IsBot { get; set; }
    public int Wpm { get; set; }
    // null svečiams ir botams
    public PublicPlayerCard Card { get; set; }
    public int BotWpm { get; set; }
    // null žmonėms ir matchmaking botams
    public string BotLevel { get; set; }
}

public sealed class RaceResultState
{
    public string PlayerKey { get; set; }
    public string Username { get; set; }
    public int Place { get; set; }
    public int Wpm { get; set; }
    public double Accuracy { get; set; }
    public int ErrorCount { get; set; }
    public int TimeSeconds { get; set; }
    public bool IsDnf { get; set; }
    public bool IsBot { get; set; }
    // null botams ir DNF; svečiui Saved = false
    public RaceReward Reward { get; set; }
}

public sealed class RaceRoom
{
    public string RaceId { get; set; }
    public int DatabaseRaceId { get; set; }
    // 0 = klasikinės lenktynės, kitaip žodžių režimo trukmė
    public int DurationSeconds { get; set; }
    // teksto kilmė „Apie tekstą“ skilčiai; null žodžių režime
    public TextSnippet Snippet { get; set; }
    public RaceRoomStatus Status { get; set; } = RaceRoomStatus.Lobby;
    public DateTime LobbyDeadlineUtc { get; set; }
    public DateTime StartAtUtc { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool CountdownScheduled { get; set; }
    // ne null – privačios lenktynės su botais (vienas botas kiekvienam lygiui)
    public BotLevel[] BotLevels { get; set; }
    // ne null – kambarys su draugais (prisijungiama kodu, pradeda šeimininkas)
    public string Code { get; set; }
    public bool Completing { get; set; }
    public List<RacePlayer> Players { get; set; } = new List<RacePlayer>();
    public List<RaceTimelineTick> Timeline { get; set; } =
        new List<RaceTimelineTick>();
    public string GroupName => "race:" + RaceId;
}

public sealed class RacePlayer
{
    public string PlayerKey { get; set; }
    public string Username { get; set; }
    public int? UserId { get; set; }
    public string ConnectionId { get; set; }
    public string TabId { get; set; }
    public DateTime? DisconnectedAtUtc { get; set; }
    public DateTime ReservedAtUtc { get; set; } = DateTime.UtcNow;
    public int CorrectChars { get; set; }
    public int ErrorCount { get; set; }
    public int TotalKeystrokes { get; set; }
    public int Wpm { get; set; }
    public double Accuracy { get; set; }
    public int TimeSeconds { get; set; }
    public int Place { get; set; }
    public int FinishAtMs { get; set; }
    public bool IsFinished { get; set; }
    public bool IsDnf { get; set; }
    public bool ResultSaved { get; set; }
    public bool IsBot { get; set; }
    public int ColorIndex { get; set; }
    public double TargetWpm { get; set; }
    public BotLevel? BotLevel { get; set; }
    public BotPlan BotPlan { get; set; }
    public RaceReward Reward { get; set; }
    public PlayerCard Card { get; set; }
    public Dictionary<int, int> FirstReachedAtMs { get; set; } =
        new Dictionary<int, int>();
}

public sealed class RaceTimelineTick
{
    public int AtMs { get; set; }
    public Dictionary<string, int> Values { get; set; } =
        new Dictionary<string, int>();
}

public sealed class RaceLeaderSegment
{
    public string PlayerId { get; set; }
    public int FromMs { get; set; }
    public int ToMs { get; set; }
}

public sealed class RaceOvertake
{
    public int AtMs { get; set; }
    public string PasserId { get; set; }
    public string PassedId { get; set; }
}

public sealed class RaceAnalysis
{
    public List<RaceLeaderSegment> LeaderSegments { get; set; } =
        new List<RaceLeaderSegment>();
    public List<RaceOvertake> Overtakes { get; set; } =
        new List<RaceOvertake>();
    public Dictionary<string, int> LeaderTimeMs { get; set; } =
        new Dictionary<string, int>();
    public int LeaderChanges { get; set; }
    public int TotalMs { get; set; }
}

public enum RaceRoomStatus
{
    Lobby,
    Countdown,
    Racing,
    Finished
}
