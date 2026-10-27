// Models/UserLessonProgress.cs
using System;

// Žaidėjo geriausias rezultatas vienoje pamokoje (LessonCatalog Id, pvz. "2-3"). Viena eilutė vienai pamokai.
public class UserLessonProgress
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string LessonId { get; set; } = string.Empty;
    // 0..3; 0 – bandyta, bet tikslumo nepakako
    public int Stars { get; set; }
    public double BestAccuracy { get; set; }
    public int BestWpm { get; set; }
    public int Attempts { get; set; }
    // kada pirmą kartą gauta bent viena žvaigždutė
    public DateTime? CompletedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
