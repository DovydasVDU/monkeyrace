using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Mėnesio ilgų žodžių iššūkis: žodžiai, sukčiavimo riba, rezultato išsaugojimas ir HTTP apsauga
public sealed class ChallengeTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"monkeyrace-challenge-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public ChallengeTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = $"Data Source={_path}" })));
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }

    private AppDbContext NewContext() => new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_path}").Options);

    private int NewUser(string name)
    {
        using var db = NewContext();
        var user = new User { Username = name, PasswordHash = "x" };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    [Fact]
    public void WordsFor_IsStablePerMonth_StartsWithSignatureWord()
    {
        var words = MonthlyChallenge.WordsFor("2026-10");
        Assert.Equal(MonthlyChallenge.ExtraWords + 1, words.Count);
        Assert.Equal(MonthlyChallenge.SignatureWord, words[0]);
        Assert.Equal(words.Count, words.Distinct().Count());
        Assert.Equal(words, MonthlyChallenge.WordsFor("2026-10"));
    }

    [Fact]
    public void IsPlausible_RejectsImpossibleSpeed()
    {
        var chars = MonthlyChallenge.TotalChars("2026-10");
        var limitSeconds = chars / 5.0 / RaceService.MaxWpm * 60;
        Assert.False(MonthlyChallenge.IsPlausible("2026-10", limitSeconds * 0.9));
        Assert.True(MonthlyChallenge.IsPlausible("2026-10", limitSeconds * 1.1));
        Assert.False(MonthlyChallenge.IsPlausible("2026-10", 0));
        Assert.False(MonthlyChallenge.IsPlausible("2026-10", -5));
        Assert.Equal(14.0, MonthlyChallenge.Score(10, 2));
    }

    [Fact]
    public async Task Save_BestAttemptCounts_ParticipationRewardOnce_RankIsByScore()
    {
        var ana = NewUser("ana_iss");
        var jonas = NewUser("jonas_iss");
        async Task<ChallengeSaveResult> Save(int userId, double seconds, int errors)
        {
            using var db = NewContext();
            return await new RaceService(db).SaveChallengeAsync(userId, "2026-10", seconds, errors);
        }

        var first = await Save(ana, 30, 1); // 32 s
        Assert.True(first.IsBest);
        Assert.Single(first.Notes);
        Assert.Equal(1, first.Rank);

        var worse = await Save(ana, 40, 0); // 40 s – blogiau
        Assert.False(worse.IsBest);
        Assert.Empty(worse.Notes); // premija tik už pirmą dalyvavimą
        Assert.Equal(32, worse.BestScore);

        var better = await Save(ana, 25, 0);
        Assert.True(better.IsBest);
        Assert.Empty(better.Notes);

        var second = await Save(jonas, 20, 0); // 20 s – greičiau už Aną (25)
        Assert.Equal(1, second.Rank);

        using var db2 = NewContext();
        var entry = db2.ChallengeEntries.Single(e => e.UserId == ana);
        Assert.Equal((25.0, 3), (entry.BestScore, entry.Attempts));
        var user = db2.Users.Single(u => u.Id == ana);
        Assert.Equal((MonthlyChallenge.ParticipationXp, MonthlyChallenge.ParticipationBananas), (user.Xp, user.Bananas));
    }

    [Fact]
    public async Task Http_GuestCannotStart_AndInstantFinishIsRejected()
    {
        var guest = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsync("/challenge/begin", null)).StatusCode);
        // svečias iš rašymo ekrano nukreipiamas į iššūkio puslapį
        Assert.Equal("/menesio-issukis", (await guest.GetAsync("/menesio-issukis/zaisti")).RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync("/menesio-issukis")).StatusCode);

        var player = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        (await player.PostAsync("/Auth/Register", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["username"] = "iss_zaidejas", ["password"] = "slaptas1" }))).EnsureSuccessStatusCode();

        // rašymo ekranas rodo mėnesio žodžius
        var page = await player.GetStringAsync("/menesio-issukis/zaisti");
        // tekstas įrašytas į puslapį kaip JSON (raidės su diakritika užkoduotos)
        Assert.Contains(JsonSerializer.Serialize(string.Join(' ', MonthlyChallenge.WordsFor(MonthlyChallenge.MonthKey(DateTime.UtcNow)))), page);
        Assert.Equal(HttpStatusCode.NoContent, (await player.PostAsync("/challenge/begin", null)).StatusCode);

        // iškart baigus – per greita
        var finished = await player.PostAsJsonAsync("/challenge/finish", new { errors = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, finished.StatusCode);
        using var db = NewContext();
        Assert.Empty(db.ChallengeEntries);
        // be naujo rašymo ekrano atidarymo baigti negalima
        Assert.Equal(HttpStatusCode.BadRequest, (await player.PostAsJsonAsync("/challenge/finish", new { errors = 0 })).StatusCode);
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }
}
