using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Saugumo antraštės ir prisijungimo riba pagal slapyvardį
public sealed class SecurityTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"monkeyrace-security-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public SecurityTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = $"Data Source={_path}" })));
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }

    private HttpClient Client() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static FormUrlEncodedContent Form(string username, string password) =>
        new(new Dictionary<string, string> { ["username"] = username, ["password"] = password });

    [Fact]
    public async Task Responses_HaveSecurityHeaders()
    {
        var response = await Client().GetAsync("/privatumas");
        response.EnsureSuccessStatusCode();
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Login_IsLocked_AfterTooManyFailures_ForThatUsernameOnly()
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/Auth/Register", Form("uzrakinamas", "slaptas1"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/Auth/Register", Form("kitas", "slaptas1"))).StatusCode);

        for (var i = 0; i < 8; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/Auth/Login", Form("uzrakinamas", "blogas"))).StatusCode);

        // net teisingas slaptažodis kol kas nepriimamas, bet kitas slapyvardis veikia
        Assert.Equal((HttpStatusCode)429, (await client.PostAsync("/Auth/Login", Form("uzrakinamas", "slaptas1"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/Auth/Login", Form("kitas", "slaptas1"))).StatusCode);
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }
}
