using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Profilio „Paskutinės lenktynės“: 10 eilučių ir „Užkrauti daugiau“ (UserController.PastRaces) per tikrą HTTP
public sealed class PastRacesTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"monkeyrace-pastraces-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public PastRacesTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = $"Data Source={_path}" })));
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    // eilutė = class="lb-row" (antraštė turi dar lb-header, todėl netinka)
    private static int Rows(string html) => Regex.Matches(html, "class=\"lb-row\"").Count;

    [Fact]
    public async Task Stats_ShowsTen_AndPastRaces_PagesTheRest_NewestFirst()
    {
        var client = NewClient();
        var register = await client.PostAsync("/Auth/Register", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["username"] = "istorija", ["password"] = "slaptas1" }));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        // 25 rezultatų: WPM 100..124, naujesnis = didesnis WPM
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = db.Users.Single(u => u.Username == "istorija").Id;
            var race = new Race { TextSnippetId = 1, StartedAt = DateTime.UtcNow, IsMultiplayer = false };
            db.Races.Add(race);
            db.SaveChanges();
            var start = DateTime.UtcNow.AddHours(-2);
            for (var i = 0; i < 25; i++)
                db.RaceResults.Add(new RaceResult
                    { UserId = userId, RaceId = race.Id, WPM = 100 + i, Accuracy = 100, CompletedAt = start.AddMinutes(i) });
            db.SaveChanges();
        }

        var stats = await client.GetStringAsync("/User/Stats");
        Assert.Equal(10, Rows(stats));
        Assert.Contains("id=\"history-more\"", stats);

        // antras puslapis: 124..115 jau buvo Stats, čia 114..105
        var second = await client.GetStringAsync("/User/PastRaces?skip=10");
        Assert.Equal(10, Rows(second));
        Assert.Contains("<strong>114</strong>", second);
        Assert.Contains("<strong>105</strong>", second);
        Assert.DoesNotContain("<strong>115</strong>", second);

        Assert.Equal(5, Rows(await client.GetStringAsync("/User/PastRaces?skip=20")));
        Assert.Equal(0, Rows(await client.GetStringAsync("/User/PastRaces?skip=25")));
        // neigiamas skip nesudaužo užklausos
        Assert.Equal(10, Rows(await client.GetStringAsync("/User/PastRaces?skip=-5")));
    }

    [Fact]
    public async Task Stats_WithTenOrFewer_HasNoLoadMoreButton_AndPastRaces_NeedsLogin()
    {
        var client = NewClient();
        await client.PostAsync("/Auth/Register", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["username"] = "mazai", ["password"] = "slaptas1" }));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = db.Users.Single(u => u.Username == "mazai").Id;
            var race = new Race { TextSnippetId = 1, StartedAt = DateTime.UtcNow, IsMultiplayer = false };
            db.Races.Add(race);
            db.SaveChanges();
            for (var i = 0; i < 10; i++)
                db.RaceResults.Add(new RaceResult { UserId = userId, RaceId = race.Id, WPM = 50 + i, Accuracy = 100 });
            db.SaveChanges();
        }

        var stats = await client.GetStringAsync("/User/Stats");
        Assert.Equal(10, Rows(stats));
        Assert.DoesNotContain("id=\"history-more\"", stats);

        // be prisijungimo – ne 200 su eilutėmis (peradresavimas / 401)
        var anonymous = await NewClient().GetAsync("/User/PastRaces?skip=0");
        Assert.NotEqual(HttpStatusCode.OK, anonymous.StatusCode);
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }
}
