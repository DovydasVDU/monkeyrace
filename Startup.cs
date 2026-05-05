using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

public class Startup
{
    // Tai klasė, kuri konfigūruoja mūsų programą. 
    // Joje nurodome, kokius servisus naudosime (ConfigureServices) ir kaip bus tvarkomi užklausos (Configure).
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllersWithViews();
        services.AddSignalR(); // multiplayer websocket
        services.AddSession();
        services.AddHttpContextAccessor();
        services.AddMemoryCache();


        // Duomenų bazė SQLite, naudojame Entity Framework Core, kuris leidžia dirbti su duomenų baze naudojant C# objektus.
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite("Data Source=monkeyracer.db"));


        // cookies autentikavimas
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/Auth/Login"; // jei vartotojas nėra prisijungęs, bus peradresuotas į šį URL
                options.LogoutPath = "/Auth/Logout"; // URL, kuris bus naudojamas atsijungimui

                options.AccessDeniedPath = "/Auth/AccessDenied"; // vartotojas neturi teisės pasiekti tam tikrą puslapį

                //Cookie nustatymai
                options.Cookie.Name = "MonkeyRacer.Auth";
                options.Cookie.HttpOnly = true; // apsaugo nuo tiesioginės JavaScript prieigos
                options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always; // užtikrina, kad cookie būtų siunčiamas tik per HTTPS
                options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict; // apsaugo nuo CSRF atakų, neleidžia siųsti cookie su trečiųjų šalių užklausomis

                options.ExpireTimeSpan = TimeSpan.FromDays(7); // nustato, kiek laiko galioja cookie (7 dienos)
            });

        // Naudojami services: 
        services.AddScoped<RaceService>(); // atsakingas už teksto parinkimą spausdinimo metu
    }

    // Pagrindiniai konfigūracijos nustatymai, kurie apibrėžia, kaip bus tvarkomi užklausos.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else {             
            //app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        // Automatiškai peradresuoja HTTP → HTTPS
        // http://localhost:5000 → https://localhost:5001
        app.UseHttpsRedirection();

        app.UseStaticFiles(); // wwwroot aplanke css, paveiklėliai (kol kas nėra, viskas failuose)
        app.UseSession(); // sesijos naudojimas, kur saugoma informacija apie prisijungusį vartotoją
        app.UseRouting(); // pasakome, kad naudosime routing'ą, kuris leidžia mums valdyti, kokie URL veda į kokius controller'ius ir jų metodus.

        // SVARBU: tvarka būtina
        app.UseAuthentication();  // ← pirma: kas esi?
        app.UseAuthorization();   // ← antra: ką gali?


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

        // užtikriname, kad duomenų bazė būtų sukurta, jei jos dar nėra. 
        using var scope = app.ApplicationServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); // paimama AppDbContext su lentelių duomenimis
        db.Database.EnsureCreated();
    }
}