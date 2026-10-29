using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Viešas profilis /u/{vardas}, /api/user/{vardas} ir paieška /api/users/search
public sealed class PlayersTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"monkeyrace-players-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public PlayersTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = $"Data Source={_path}" })));
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }

    private HttpClient Client() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task Register(HttpClient client, string name) =>
        (await client.PostAsync("/Auth/Register", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["username"] = name, ["password"] = "slaptas1" }))).EnsureSuccessStatusCode();

    private void SetVisibility(string name, ProfileVisibility visibility)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Single(u => u.Username == name).ProfileVisibility = visibility;
        db.SaveChanges();
    }

    [Fact]
    public async Task Visibility_MembersByDefault_PublicForAll_PrivateForNobodyButOwner()
    {
        var owner = Client();
        await Register(owner, "ona_profilis");
        var member = Client();
        await Register(member, "narys");
        var guest = Client();

        // numatyta: tik prisijungę
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/u/ona_profilis")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync("/u/ona_profilis")).StatusCode);

        SetVisibility("ona_profilis", ProfileVisibility.Public);
        var response = await guest.GetAsync("/api/user/ona_profilis");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("noindex", response.Headers.GetValues("X-Robots-Tag"));
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ona_profilis", json.GetProperty("card").GetProperty("username").GetString());
        // jokių slaptų laukų
        var text = json.GetRawText();
        Assert.DoesNotContain("bananas", text);
        Assert.DoesNotContain("\"xp\"", text);
        Assert.DoesNotContain("totalRaces", text);

        SetVisibility("ona_profilis", ProfileVisibility.Private);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync("/api/user/ona_profilis")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/user/ona_profilis")).StatusCode);
    }

    [Fact]
    public async Task PrivateAndMissing_GiveIdenticalAnswers()
    {
        var owner = Client();
        await Register(owner, "uzdaras");
        SetVisibility("uzdaras", ProfileVisibility.Private);
        var viewer = Client();
        await Register(viewer, "zmogus");

        var privateProfile = await viewer.GetAsync("/api/user/uzdaras");
        var missing = await viewer.GetAsync("/api/user/nera_tokio");
        Assert.Equal(missing.StatusCode, privateProfile.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await privateProfile.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Search_NeedsThreeLetters_MaxEight_OnlyVisiblePlayers()
    {
        var viewer = Client();
        await Register(viewer, "ieskotojas");
        for (var i = 0; i < 10; i++)
        {
            await Register(Client(), $"zaid{i}");
            SetVisibility($"zaid{i}", ProfileVisibility.Public);
        }
        await Register(Client(), "zaidnarys"); // numatyta: tik prisijungusiems
        await Register(Client(), "zaiduzdaras");
        SetVisibility("zaiduzdaras", ProfileVisibility.Private);

        Assert.Empty(await viewer.GetFromJsonAsync<JsonElement[]>("/api/users/search?q=za") ?? Array.Empty<JsonElement>());
        var hits = await viewer.GetFromJsonAsync<JsonElement[]>("/api/users/search?q=zaid");
        Assert.Equal(8, hits!.Length);

        var guestHits = await Client().GetFromJsonAsync<JsonElement[]>("/api/users/search?q=zaidn");
        Assert.Empty(guestHits!);
        var memberHits = await viewer.GetFromJsonAsync<JsonElement[]>("/api/users/search?q=zaidn");
        Assert.Equal("zaidnarys", Assert.Single(memberHits!).GetProperty("username").GetString());
        Assert.Empty((await viewer.GetFromJsonAsync<JsonElement[]>("/api/users/search?q=zaidu"))!);
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }
}
