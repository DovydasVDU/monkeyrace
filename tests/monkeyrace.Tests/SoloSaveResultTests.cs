using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Solo SaveResult per tikrą HTTP (sesija, cookie, anti-cheat) ir SQLite DB faile
public sealed class SoloSaveResultTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"monkeyrace-solo-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public SoloSaveResultTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = $"Data Source={_path}" })));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
        // atranka ima tik importuotus tekstus; Play rodo jų šaltinį
        var text = db.TextSnippets.Find(25)!;
        text.SourceId = "test";
        text.SourceUrl = "https://lt.wikipedia.org/wiki/Test";
        db.SaveChanges();
    }

    private static Task<HttpResponseMessage> Save(HttpClient client, int wpm) =>
        client.PostAsJsonAsync("/Race/SaveResult", new { wpm, accuracy = 100, timeSeconds = 10, errorCount = 0 });

    [Fact]
    public async Task TooFast_Rejected_ParallelSaves_RewardOnce_ThenRejected()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var register = await client.PostAsync("/Auth/Register", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["username"] = "solo", ["password"] = "slaptas1" }));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        // žodžių režimas: 15 s dar nepraėjo
        (await client.GetAsync("/race/words?seconds=15")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await Save(client, 40)).StatusCode);

        (await client.GetAsync("/Race/Play")).EnsureSuccessStatusCode();
        var responses = await Task.WhenAll(Save(client, 40), Save(client, 40));
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.BadRequest, (await Save(client, 40)).StatusCode);

        var card = await client.GetFromJsonAsync<JsonElement>("/api/stats/solo");
        Assert.Equal(1, card.GetProperty("totalRaces").GetInt32());
        using var scope = _factory.Services.CreateScope();
        var user = scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Single(u => u.Username == "solo");
        // pirmų lenktynių premija (50) + solo 10 + 40/4 = 70; dvigubas įrašas duotų 140
        Assert.Equal(70, user.Xp);
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }
}
