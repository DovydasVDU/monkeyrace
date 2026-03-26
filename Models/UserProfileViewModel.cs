using System;
using System.Collections.Generic;

// tai yra objektas, kurį suformuoja UserController. Paima duomenis iš db, sudeda į UserProfileViewModel ir siunčia į view. 
public class UserProfileViewModel
{
    public string Username { get; set; } = string.Empty;
    public int AverageWpm { get; set; }
    public double AverageAccuracy { get; set; }
    public List<PastRaceViewModel> PastRaces { get; set; } = new List<PastRaceViewModel>();
}
// lygiai taspats, tik surenkama buvusių naudotojo žaidimų istorija
public class PastRaceViewModel
{
    public DateTime CompletedAt { get; set; }
    public int Wpm { get; set; }
    public double Accuracy { get; set; }
    public int ErrorCount { get; set; }
    public string Difficulty { get; set; } = string.Empty;
}
