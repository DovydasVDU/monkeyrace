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
    public int TextSnippetId { get; set; }
    public TextSnippet TextSnippet { get; set; } = null!;  // Navigacinė savybė
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
    //public int Place { get; set; }

}