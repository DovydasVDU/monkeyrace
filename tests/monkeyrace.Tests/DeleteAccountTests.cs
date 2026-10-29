using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Paskyros ištrynimas per tikrą HTTP (cookie, antiforgery) ir SQLite DB faile
public sealed class DeleteAccountTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"monkeyrace-delete-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public DeleteAccountTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = $"Data Source={_path}" })));
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }

    private AppDbContext Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<AppDbContext>();

    private static async Task<HttpResponseMessage> Delete(HttpClient client, string password)
    {
        var page = await client.GetStringAsync("/User/Stats");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        return await client.PostAsync("/User/DeleteAccount", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = token, ["password"] = password }));
    }

    [Fact]
    public async Task WrongPassword_Keeps_RightPassword_DeletesAllRows_AndAnonymizesReplay()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var register = await client.PostAsync("/Auth/Register", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["username"] = "trinamas", ["password"] = "slaptas1" }));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        int userId, raceId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = Db(scope);
            userId = db.Users.Single(u => u.Username == "trinamas").Id;
            var race = new Race
            {
                TextSnippetId = 1, StartedAt = DateTime.UtcNow, IsMultiplayer = true,
                ReplayJson = $"{{\"tickMs\":250,\"players\":[{{\"id\":\"p0\",\"name\":\"trinamas\",\"userId\":{userId},\"place\":1}},{{\"id\":\"p1\",\"name\":\"Botas\",\"userId\":null,\"place\":2}}]}}"
            };
            db.Races.Add(race);
            db.SaveChanges();
            raceId = race.Id;
            db.RaceResults.Add(new RaceResult { UserId = userId, RaceId = raceId, WPM = 50, Accuracy = 100 });
            db.UserKeyStats.Add(new UserKeyStat { UserId = userId, Expected = "ą", Typed = "a", Count = 3 });
            db.UserWordMistakes.Add(new UserWordMistake { UserId = userId, Word = "žodis", Misses = 1 });
            db.UserItems.Add(new UserItem { UserId = userId, ItemId = "hat-test" });
            db.UserClaims.Add(new UserClaim { UserId = userId, Key = "module:1" });
            db.SaveChanges();
        }

        Assert.Equal("/User/Stats#account", (await Delete(client, "neteisingas")).Headers.Location?.OriginalString);
        using (var scope = _factory.Services.CreateScope())
            Assert.True(Db(scope).Users.Any(u => u.Id == userId));

        var deleted = await Delete(client, "slaptas1");
        Assert.Equal("/", deleted.Headers.Location?.OriginalString);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = Db(scope);
            Assert.False(db.Users.Any(u => u.Id == userId));
            Assert.False(db.RaceResults.Any(r => r.UserId == userId));
            Assert.False(db.UserKeyStats.Any(k => k.UserId == userId));
            Assert.False(db.UserWordMistakes.Any(w => w.UserId == userId));
            Assert.False(db.UserItems.Any(i => i.UserId == userId));
            Assert.False(db.UserClaims.Any(c => c.UserId == userId));
            var players = System.Text.Json.Nodes.JsonNode.Parse(db.Races.Find(raceId)!.ReplayJson)!["players"]!.AsArray();
            Assert.Equal("Ištrintas žaidėjas", (string)players[0]!["name"]!);
            Assert.Null(players[0]!["userId"]);
            Assert.Equal("Botas", (string)players[1]!["name"]!);
        }

        // atjungtas: profilis nebepasiekiamas
        Assert.NotEqual(HttpStatusCode.OK, (await client.GetAsync("/User/Stats")).StatusCode);
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }
}
