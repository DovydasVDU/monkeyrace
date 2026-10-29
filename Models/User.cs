// Models/User.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

// duomenų bazės lentelės
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Progresas. Lygis nesaugomas: visada skaičiuojamas iš Xp (Progression.LevelFor)
    public int Xp { get; set; }
    public int Bananas { get; set; }

    // Parduotuvė: užsidėtų prekių Id (ShopCatalog); null – numatytoji nemokama
    public string HatId { get; set; }
    public string FurId { get; set; }
    public string TrackId { get; set; }
    public string BackgroundId { get; set; }
    public string FrameId { get; set; }

    // Iki kurio lygio jau išdalinti bananai už lygių pakilimą (RaceService.SettleLevels); naujam žaidėjui – 1
    public int ClaimedLevel { get; set; } = 1;

    // Foreign key į RaceResult, kad galėtume matyti žaidimus, kuriuose dalyvavo
    public ICollection<RaceResult> RaceResults { get; set; } = new List<RaceResult>();
}

// Models/TextSnippet.cs
public class TextSnippet
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;    // Tekstas spausdinimui
    public string Language { get; set; } = "lt";           // Kalba
    public int WordCount { get; set; }

    // Importuoti tekstai (Data/texts-lt.jsonl). Seni seed tekstai šių laukų neturi ir į atranką nepatenka.
    public string SourceId { get; set; }                   // candidates.jsonl id, pagal jį importas nekartoja įrašų
    public string Author { get; set; }
    public string Work { get; set; }                       // kūrinys arba Vikipedijos straipsnis
    public string SourceUrl { get; set; }
    public string License { get; set; }
    public string Flags { get; set; }                      // kableliais: dialogue,digits,...

    // „Apie tekstą“ skilčiai (solo Play ir online RaceFinished)
    private static readonly Dictionary<string, string> FlagNames = new()
    {
        ["dialogue"] = "Dialogas",
        ["digits"] = "Skaičiai",
        ["many_caps"] = "Daug didžiųjų raidžių",
        ["quotes"] = "Kabutės",
        ["parens"] = "Skliaustai",
        ["colon_semicolon"] = "Dvitaškis ar kabliataškis",
        ["ellipsis"] = "Daugtaškis",
        ["informational"] = "Informacinis tekstas",
        ["wikipedia"] = "Vikipedija"
    };

    [NotMapped]
    public string[] FlagLabels => (Flags ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(flag => FlagNames.GetValueOrDefault(flag, flag))
        .ToArray();

    // PD-PATIKRINTI: visi literatūros autoriai mirę daugiau nei prieš 70 metų
    [NotMapped]
    public string LicenseLabel => License == "PD-PATIKRINTI" ? "Viešasis domenas" : License;
}

// Models/Race.cs
public class Race
{
    public int Id { get; set; }
    public int? TextSnippetId { get; set; }                // null žodžių režime (tekstas generuojamas, ne iš DB)
    public TextSnippet TextSnippet { get; set; }           // jungiasi su TextSnippe
    public int? DurationSeconds { get; set; }              // žodžių režimo trukmė; null klasikinėse lenktynėse
    public DateTime StartedAt { get; set; }
    public bool IsMultiplayer { get; set; } = false;
    public string Status { get; set; } = "waiting";        // waiting/finished
    // RaceAnalyzer.CreateReplay JSON; tik klasikinėms lenktynėms, baigtoms po replay atsiradimo
    public string ReplayJson { get; set; }

    public ICollection<RaceResult> Results { get; set; } = new List<RaceResult>();
}

// Models/RaceResult.cs
public class RaceResult
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int RaceId { get; set; }
    public Race Race { get; set; } = null!;

    public int WPM { get; set; }           // Words per minute
    public double Accuracy { get; set; }   // 0.0 - 100.0
    public int TimeSeconds { get; set; }   // Kiek laiko užtruko
    public int ErrorCount { get; set; }
    public int? Place { get; set; }        // vieta online lenktynėse; null solo ir seniems įrašams
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

    // Kiek gauta už šias lenktynes; saugoma, kad pakartotinis išsaugojimas grąžintų tą patį apdovanojimą
    public int XpGained { get; set; }
    public int BananasGained { get; set; }
    //public int Place { get; set; }

}
// Models/UserKeyStat.cs
// Kiek kartų laukiant simbolio Expected buvo paspausta Typed (Expected == Typed – pataikyta).
// Bandymai = visų Expected eilučių suma, klaidos = eilutės, kur Typed != Expected.
public class UserKeyStat
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Expected { get; set; } = string.Empty;
    public string Typed { get; set; } = string.Empty;
    public int Count { get; set; }
}

// Models/UserWordMistake.cs
// Žodis atsiranda tik pirmą kartą suklydus; CorrectStreak – kiek kartų iš eilės vėliau parašytas be klaidų
public class UserWordMistake
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Word { get; set; } = string.Empty;
    public int Misses { get; set; }
    public int CorrectStreak { get; set; }
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}

// Models/UserItem.cs
// Nupirkta prekė (ShopCatalog Id). Nemokamų prekių čia nėra – jos priklauso visiems.
public class UserItem
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string ItemId { get; set; } = string.Empty;
    public DateTime BoughtAt { get; set; } = DateTime.UtcNow;
}
