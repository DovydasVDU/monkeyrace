using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// /api/stats/{vardas}: savininkas mato XP ir bananus, kiti – tik kortelės laukus
public sealed class StatsPrivacyTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"monkeyrace-stats-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public StatsPrivacyTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = $"Data Source={_path}" })));
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }

    private HttpClient Client() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static Task<HttpResponseMessage> Register(HttpClient client, string name) =>
        client.PostAsync("/Auth/Register", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["username"] = name, ["password"] = "slaptas1" }));

    [Fact]
    public async Task Owner_SeesFullCard_Others_SeeOnlyPublicFields()
    {
        var owner = Client();
        await Register(owner, "savininkas");
        var other = Client();
        await Register(other, "kitas");

        var own = await owner.GetFromJsonAsync<JsonElement>("/api/stats/savininkas");
        Assert.True(own.TryGetProperty("bananas", out _));
        Assert.True(own.TryGetProperty("xp", out _));

        var seen = await other.GetFromJsonAsync<JsonElement>("/api/stats/savininkas");
        Assert.Equal("savininkas", seen.GetProperty("username").GetString());
        Assert.True(seen.TryGetProperty("level", out _));
        Assert.False(seen.TryGetProperty("bananas", out _));
        Assert.False(seen.TryGetProperty("xp", out _));
        Assert.False(seen.TryGetProperty("totalRaces", out _));

        // nepažįstamas (neprisijungęs) mato tą patį kaip kiti
        var guest = await Client().GetFromJsonAsync<JsonElement>("/api/stats/savininkas");
        Assert.False(guest.TryGetProperty("bananas", out _));
    }

    [Fact]
    public async Task UnknownUser_Returns404_WithoutEchoingTheName()
    {
        var response = await Client().GetAsync("/api/stats/nera_tokio");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("nera_tokio", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Stats_IsRateLimitedPerIp()
    {
        var client = Client();
        for (var i = 0; i < 120; i++)
            await client.GetAsync("/api/stats/nera");
        Assert.Equal((HttpStatusCode)429, (await client.GetAsync("/api/stats/nera")).StatusCode);
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }
}
