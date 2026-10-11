public class MonkeyViewModel
{
    public string BodyColor { get; set; } = "#8D5A2B";
    public string HatColor { get; set; } = "#FFC93C";
    public string Mood { get; set; } = "idle";
    // Nebūtinas: rodomas kaip ženkliukas (toks pat kaip JS monkeyTemplate)
    public int? Level { get; set; }
}
