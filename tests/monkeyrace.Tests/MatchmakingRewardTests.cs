using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Visos lenktynės per tikrą MatchmakingService ir SQLite DB. Lėtas (~20 s per testą):
// tikri lobby (10 s) ir atgalinio skaičiavimo (3 s) laikmačiai.
[Trait("Category", "Integration")]
public sealed class MatchmakingRewardTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"monkeyrace-mm-{Guid.NewGuid():N}.sqlite");
    private readonly ServiceProvider _services;
    private readonly FakeHub _hub = new FakeHub();
    private readonly MatchmakingService _matchmaking;
    private readonly string _text;

    public MatchmakingRewardTests()
    {
        var collection = new ServiceCollection();
        collection.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={_path}"));
        collection.AddScoped<RaceService>();
        _services = collection.BuildServiceProvider();

        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.Migrate();
            // vienas trumpas tekstas, kad lenktynės truktų kelias sekundes
            db.TextSnippets.RemoveRange(db.TextSnippets.Where(t => t.Id != 25));
            db.SaveChanges();
            _text = db.TextSnippets.Single().Content;
        }

        // Botai visada 120 WPM: greitesni už lėtą žmogų, lėtesni už greitą
        var botOptions = Options.Create(new BotOptions
        {
            FillToPlayers = 4, WpmMin = 120, WpmMax = 120, DefaultWpm = 120
        });
        var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();
        _matchmaking = new MatchmakingService(_hub, scopeFactory,
            NullLogger<MatchmakingService>.Instance,
            new BotSkillProvider(scopeFactory, botOptions), botOptions);
    }

    private int CreateUser(string name)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User { Username = name, PasswordHash = "x" };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    private async Task<string> Join(string key, string name, int? userId)
    {
        var raceId = _matchmaking.JoinOrCreate(key, name, userId, null);
        var result = await _matchmaking.JoinRaceAsync(raceId, key, name, "tab-" + key, "conn-" + key);
        Assert.True(result.IsSuccess, result.Error);
        return raceId;
    }

    // Siunčia progresą nurodytu greičiu, kol parašo visą tekstą
    private async Task Type(string raceId, string key, double charsPerSecond, int stopAtChars = int.MaxValue)
    {
        var started = DateTime.UtcNow;
        var chars = 0;
        while (chars < _text.Length && chars < stopAtChars)
        {
            await Task.Delay(200);
            chars = Math.Min(_text.Length, (int)((DateTime.UtcNow - started).TotalSeconds * charsPerSecond));
            await _matchmaking.SendProgressAsync(raceId, key, "conn-" + key, chars, chars, 0);
        }
    }

    private static JsonElement PlayerResult(JsonElement finished, string key) =>
        finished.GetProperty("players").EnumerateArray()
            .Single(player => player.GetProperty("playerKey").GetString() == key);

    private (int Xp, int Bananas, int Results) UserState(int userId)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.Users.Single(u => u.Id == userId);
        return (user.Xp, user.Bananas, db.RaceResults.Count(r => r.UserId == userId));
    }

    [Fact]
    public async Task FirstPlace_GetsMoreThanFourth_BotsGetNothing_SecondSaveAddsNothing()
    {
        var fastId = CreateUser("greitas");
        var slowId = CreateUser("letas");
        var raceId = await Join("user:" + fastId, "greitas", fastId);
        Assert.Equal(raceId, await Join("user:" + slowId, "letas", slowId));

        await _hub.WaitFor("RaceStarted", TimeSpan.FromSeconds(30));
        // greitas 18 simb./s (~216 WPM, žemiau serverio 250 WPM ribos), botai 10 simb./s, lėtas 6 simb./s
        await Task.WhenAll(
            Type(raceId, "user:" + fastId, 18),
            Type(raceId, "user:" + slowId, 6));
        var finished = await _hub.WaitFor("RaceFinished", TimeSpan.FromSeconds(60));

        var fast = PlayerResult(finished, "user:" + fastId);
        var slow = PlayerResult(finished, "user:" + slowId);
        Assert.Equal(1, fast.GetProperty("place").GetInt32());
        Assert.Equal(4, slow.GetProperty("place").GetInt32());
        var fastReward = fast.GetProperty("reward");
        var slowReward = slow.GetProperty("reward");
        Assert.True(fastReward.GetProperty("saved").GetBoolean());
        Assert.True(fastReward.GetProperty("xpGained").GetInt32() > slowReward.GetProperty("xpGained").GetInt32());
        Assert.True(fastReward.GetProperty("bananasGained").GetInt32() > slowReward.GetProperty("bananasGained").GetInt32());

        foreach (var bot in finished.GetProperty("players").EnumerateArray()
                     .Where(player => player.GetProperty("isBot").GetBoolean()))
            Assert.Equal(JsonValueKind.Null, bot.GetProperty("reward").ValueKind);

        // XP ir bananai išsaugoti DB (išlieka po puslapio perkrovimo)
        var before = UserState(fastId);
        Assert.Equal(fastReward.GetProperty("xp").GetInt32(), before.Xp);
        Assert.Equal(fastReward.GetProperty("bananas").GetInt32(), before.Bananas);

        // Antras to paties rezultato išsaugojimas nieko neprideda
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dbRaceId = db.RaceResults.Single(r => r.UserId == fastId).RaceId;
            var again = await scope.ServiceProvider.GetRequiredService<RaceService>()
                .SaveRaceResultAsync(fastId, dbRaceId, RaceMode.Multiplayer, 1, 999, 100, 1, 0);
            Assert.Equal(fastReward.GetProperty("xpGained").GetInt32(), again.XpGained);
            Assert.Equal(before.Bananas, again.Bananas);
            Assert.Equal(2, db.RaceResults.Count()); // tik du žmonės, botai neįrašyti
        }
        Assert.Equal(before, UserState(fastId));
    }

    [Fact]
    public async Task Dnf_GetsNothing_GuestStoresNothing()
    {
        var quitterId = CreateUser("pasiduodu");
        var raceId = await Join("user:" + quitterId, "pasiduodu", quitterId);
        Assert.Equal(raceId, await Join("guest:abc", "Svečias", null));

        await _hub.WaitFor("RaceStarted", TimeSpan.FromSeconds(30));
        await Task.WhenAll(
            Task.Run(async () =>
            {
                await Type(raceId, "user:" + quitterId, 10, stopAtChars: 10);
                await _matchmaking.LeaveAsync(raceId, "user:" + quitterId, "conn-user:" + quitterId);
            }),
            Type(raceId, "guest:abc", 15));
        var finished = await _hub.WaitFor("RaceFinished", TimeSpan.FromSeconds(60));

        var quitter = PlayerResult(finished, "user:" + quitterId);
        Assert.True(quitter.GetProperty("isDnf").GetBoolean());
        Assert.Equal(JsonValueKind.Null, quitter.GetProperty("reward").ValueKind);
        Assert.Equal((0, 0, 0), UserState(quitterId));

        var guestReward = PlayerResult(finished, "guest:abc").GetProperty("reward");
        Assert.False(guestReward.GetProperty("saved").GetBoolean());
        Assert.True(guestReward.GetProperty("xpGained").GetInt32() > 0);

        using var scope = _services.CreateScope();
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<AppDbContext>().RaceResults.Count());
    }

    [Fact]
    public async Task WordsMode_EndsOnTime_PlacesByWpm_NobodyDnf()
    {
        var fastId = CreateUser("greitas");
        // klasikinis ir žodžių režimas į tą patį kambarį nepatenka
        Assert.NotEqual(_matchmaking.JoinOrCreate("guest:classic", "Klasika", null, null),
            _matchmaking.JoinOrCreate("guest:other", "Kitas", null, null, durationSeconds: 15));
        var raceId = _matchmaking.JoinOrCreate("user:" + fastId, "greitas", fastId, null, durationSeconds: 15);
        Assert.True((await _matchmaking.JoinRaceAsync(raceId, "user:" + fastId, "greitas", "tab", "conn-user:" + fastId)).IsSuccess);
        Assert.True((await _matchmaking.JoinRaceAsync(raceId, "guest:other", "Kitas", "tab", "conn-guest:other")).IsSuccess);

        await _hub.WaitFor("RaceStarted", TimeSpan.FromSeconds(30));
        var startedAt = DateTime.UtcNow;
        // greitas 18 simb./s, botai 10 simb./s (120 WPM), lėtas 6 simb./s – rašo, kol serveris priima
        async Task TypeFor(string key, double charsPerSecond)
        {
            while (DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(16))
            {
                await Task.Delay(200);
                var chars = (int)((DateTime.UtcNow - startedAt).TotalSeconds * charsPerSecond);
                await _matchmaking.SendProgressAsync(raceId, key, "conn-" + key, chars, chars, 0, "zod");
            }
        }
        await Task.WhenAll(TypeFor("user:" + fastId, 18), TypeFor("guest:other", 6));
        var finished = await _hub.WaitFor("RaceFinished", TimeSpan.FromSeconds(30));

        Assert.Equal(15, finished.GetProperty("durationSeconds").GetInt32());
        Assert.All(finished.GetProperty("players").EnumerateArray(),
            player => Assert.False(player.GetProperty("isDnf").GetBoolean()));
        var fast = PlayerResult(finished, "user:" + fastId);
        Assert.Equal(1, fast.GetProperty("place").GetInt32());
        Assert.Equal(4, PlayerResult(finished, "guest:other").GetProperty("place").GetInt32());
        Assert.Equal(15, fast.GetProperty("timeSeconds").GetInt32());
        Assert.InRange(fast.GetProperty("wpm").GetInt32(), 180, 230);

        using var scope = _services.CreateScope();
        var race = scope.ServiceProvider.GetRequiredService<AppDbContext>().Races
            .Single(r => r.Results.Any(result => result.UserId == fastId));
        Assert.Equal(15, race.DurationSeconds);
        Assert.Null(race.TextSnippetId);
    }

    public void Dispose()
    {
        _services.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }

    // Įrašo visus SignalR pranešimus, kad testas galėtų laukti konkretaus įvykio
    private sealed class FakeHub : IHubContext<RaceHub>, IHubClients, IClientProxy, IGroupManager
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _events = new();
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        private TaskCompletionSource<JsonElement> Event(string name) =>
            _events.GetOrAdd(name, _ => new TaskCompletionSource<JsonElement>(
                TaskCreationOptions.RunContinuationsAsynchronously));

        public async Task<JsonElement> WaitFor(string name, TimeSpan timeout) =>
            await Event(name).Task.WaitAsync(timeout);

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            var payload = args.Length > 0
                ? JsonSerializer.SerializeToElement(args[0], Web)
                : default;
            Event(method).TrySetResult(payload);
            return Task.CompletedTask;
        }

        public IHubClients Clients => this;
        public IGroupManager Groups => this;
        public IClientProxy All => this;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => this;
        public IClientProxy Client(string connectionId) => this;
        IClientProxy IHubClients<IClientProxy>.Clients(IReadOnlyList<string> connectionIds) => this;
        public IClientProxy Group(string groupName) => this;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => this;
        IClientProxy IHubClients<IClientProxy>.Groups(IReadOnlyList<string> groupNames) => this;
        public IClientProxy User(string userId) => this;
        public IClientProxy Users(IReadOnlyList<string> userIds) => this;
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
