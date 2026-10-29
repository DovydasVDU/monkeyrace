using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

public class Startup
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self'; connect-src 'self' ws: wss:; object-src 'none'; " +
        "base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;


   public Startup(IConfiguration configuration, IWebHostEnvironment env)
{
    _configuration = configuration;
    _env = env;
}

    // Tai klasė, kuri konfigūruoja mūsų programą. 
    // Joje nurodome, kokius servisus naudosime (ConfigureServices) ir kaip bus tvarkomi užklausos (Configure).
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllersWithViews();
        services.AddSignalR(); // multiplayer websocket
        services.AddSession();
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.Configure<BotOptions>(_configuration.GetSection("Bots"));
        // Login/Register: ne daugiau 10 bandymų per minutę iš vieno IP (apsauga nuo slaptažodžių spėliojimo)
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.OnRejected = (context, token) => new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
                new { message = "Per daug bandymų. Pabandyk po minutės." }, token));
            // 30 per IP, o ne 10: mokykloje visa klasė eina per vieną IP. Slaptažodžių spėliojimą sustabdo
            // ir nepavykusių prisijungimų riba pagal slapyvardį (AuthController)
            options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
            // Žaidėjų kortelės (/api/stats/{vardas}): pagal IP, kad nebūtų galima skenuoti vardų
            options.AddPolicy("stats", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
            // Pamokų raundai: prisijungusiam – pagal vartotoją, svečiui – pagal IP (didesnė riba dėl klasių)
            options.AddPolicy("lessons", context =>
            {
                var userId = PlayerIdentity.GetUserId(context);
                return userId != null
                    ? RateLimitPartition.GetFixedWindowLimiter("user:" + userId,
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) })
                    : RateLimitPartition.GetFixedWindowLimiter("ip:" + context.Connection.RemoteIpAddress,
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) });
            });
        });

        // Vystymui naudojame naują SQLite DB failą. Schema sukuriama paleidimo metu.
        string connectionString = _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Trūksta ConnectionStrings:DefaultConnection.");
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString));


        // Slapuko ir sesijos šifravimo raktai laikomi diske, kad perkrovus aplikaciją žaidėjai neatsijungtų.
        // Aplankas keičiamas per DataProtection:KeysPath; diegiant svetainę jo netrinti (App_Data/keys).
        var keysPath = _configuration["DataProtection:KeysPath"] ?? Path.Combine(_env.ContentRootPath, "App_Data", "keys");
        try
        {
            Directory.CreateDirectory(keysPath);
            services.AddDataProtection().SetApplicationName("MonkeyRace").PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // shortcut: aplanko sukurti nepavyko, raktai lieka numatytoje vietoje (perkrovus gali atjungti žaidėjus); nurodyti DataProtection:KeysPath
        }

        // cookies autentikavimas
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/"; // login URL
                options.LogoutPath = "/Auth/Logout"; // logout URL

                options.AccessDeniedPath = "/"; // vartotojas neturi teisės pasiekti tam tikrą puslapį

                //Cookie nustatymai
                options.Cookie.Name = "MonkeyRacer.Auth";
                options.Cookie.HttpOnly = true; // apsaugo nuo tiesioginės JavaScript prieigos
                options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always; // užtikrina, kad cookie būtų siunčiamas tik per HTTPS
                options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict; // apsaugo nuo CSRF atakų, neleidžia siųsti cookie su trečiųjų šalių užklausomis

                options.ExpireTimeSpan = TimeSpan.FromDays(7); // nustato, kiek laiko galioja cookie (7 dienos)

                // Ištrinto vartotojo slapukas (pvz. paskyra ištrinta kitame įrenginyje) nebegalioja
                // shortcut: viena PK užklausa per kiekvieną prisijungusio vartotojo užklausą; jei apkrova išaugs, cache'inti kelioms minutėms
                options.Events.OnValidatePrincipal = async context =>
                {
                    // čia HttpContext.User dar tuščias: tikrinamas ką tik iš slapuko atkurtas context.Principal
                    var userId = int.TryParse(context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var parsedId)
                        ? parsedId : (int?)null;
                    var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    if (userId == null || !await db.Users.AnyAsync(u => u.Id == userId.Value))
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            });

        // Naudojami services: 
        services.AddScoped<RaceService>(); // atsakingas už teksto parinkimą spausdinimo metu
        services.AddSingleton<MatchmakingService>();
        services.AddSingleton<BotSkillProvider>();
    }

    // Pagrindiniai konfigūracijos nustatymai, kurie apibrėžia, kaip bus tvarkomi užklausos.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        // Saugumo antraštės. CSP su 'unsafe-inline', nes puslapiuose yra inline <script> ir onclick; vis tiek neleidžia
        // kitų domenų skriptų, įdėjimo į kitą svetainę (frame-ancestors), <base> ir formų siuntimo į kitus adresus.
        // shortcut: pilnai griežtam CSP (nonce) reikėtų iškelti visus inline skriptus ir onclick į failus.
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
            await next();
        });

        // Automatiškai peradresuoja HTTP → HTTPS
        // http://localhost:5000 → https://localhost:5001
        app.UseHttpsRedirection();

        app.UseStaticFiles(); // wwwroot aplanke css, paveiklėliai (kol kas nėra, viskas failuose)
        app.UseSession(); // sesijos naudojimas, kur saugoma informacija apie prisijungusį vartotoją
        app.UseRouting(); // pasakome, kad naudosime routing'ą, kuris leidžia mums valdyti, kokie URL veda į kokius controller'ius ir jų metodus.

        // SVARBU: tvarka būtina
        app.UseAuthentication();  // pirma: kas esi?
        app.UseAuthorization();   // antra: ką gali?
        app.UseRateLimiter();


        // pasakome, kad pagrindinis maršrutas būtų HomeController ir jo Index metodas, kuris atidaro pagrindinį puslapį. 
        app.UseEndpoints(endpoints =>
        {
            // Rounting'as yra toks pats, kaip ir failų struktūra, pvz. /home/index, /user/stats ir pan. 
            // Jeigu URL yra tuščias, tai nukreipia į HomeController ir jo Index metodą.
            endpoints.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            endpoints.MapHub<RaceHub>("/racehub"); // SignalR hub'as, kuris leidžia realiu laiku atnaujinti informaciją apie lenktynes visiems prisijungusiems vartotojams.

        });
    }
}
      