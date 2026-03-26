using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;

public class Program
{
    // tai yra pagrindinis failas, kuris paleidžia visą programą. 
    // Jis sukuria ir paleidžia localhost'ą, kuris yra tarsi konteineris visai programai. 
    // Host'as rūpinasi viskuo, kas susiję su programos gyvavimo ciklu, įskaitant serverio paleidimą, konfigūraciją ir pan.
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>(); // naudojame Startup klasę
            });
}