public class MonkeyViewModel
{
    public string BodyColor { get; set; } = "#8D5A2B";
    public string HatColor { get; set; } = "#FFC93C";
    // Fonas ir rėmelis iš parduotuvės (null arba "none" – nėra)
    public string BackgroundColor { get; set; }
    public string FrameColor { get; set; }
    public string Mood { get; set; } = "idle";
    // Nebūtinas: rodomas kaip ženkliukas (toks pat kaip JS monkeyTemplate)
    public int? Level { get; set; }
}
