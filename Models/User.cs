// Models/User.cs
using System;
using System.Collections.Generic;

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
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

    // Kiek gauta už šias lenktynes; saugoma, kad pakartotinis išsaugojimas grąžintų tą patį apdovanojimą
    public int XpGained { get; set; }
    public int BananasGained { get; set; }
    //public int Place { get; set; }

}