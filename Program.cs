using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public class Program
{
    // tai yra pagrindinis failas, kuris paleidžia visą programą. 
    // Jis sukuria ir paleidžia localhost'ą, kuris yra tarsi konteineris visai programai. 
    // Host'as rūpinasi viskuo, kas susiję su programos gyvavimo ciklu, įskaitant serverio paleidimą, konfigūraciją ir pan.
    public static void Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Migrate() sukuria DB ir prideda naujus stulpelius (EnsureCreated to nedaro esamai DB).
            // Pakeitus modelį: dotnet ef migrations add <Pavadinimas>
            db.Database.Migrate();
            TextImporter.Import(db, System.IO.Path.Combine(AppContext.BaseDirectory, "Data", "texts-lt.jsonl"));
        }

        host.Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>(); // naudojame Startup klasę
            });

}