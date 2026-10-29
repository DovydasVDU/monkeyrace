// Services/ShopCatalog.cs
using System.Linq;

// Iš kur daiktas gaunamas: Default – nemokamas numatytasis, Shop – perkamas už bananus,
// kiti – tik už pasiekimus žaidime (nusipirkti negalima)
public enum ItemSource { Default, Shop, Level, Lesson, Achievement }

// Value: kepurei ir kailiui – SVG spalva (_Monkey), fonui ir rėmeliui – CSS/SVG spalva ("none" – nėra),
// takeliui – lenktynių juostos stilius (race.css .lane-*).
// Unlock: kaip gauti daiktą, kai jis neperkamas (rodoma parduotuvėje)
public sealed record ShopItem(string Id, string Category, string Name, string Value, int Price,
    ItemSource Source = ItemSource.Shop, string Rarity = "common", string Unlock = null, int Level = 0)
{
    public bool IsDefault => Source == ItemSource.Default;
    public bool IsPurchasable => Source == ItemSource.Shop;
}

// Parduotuvės prekės. Kainos tik serveryje – naršyklė siunčia tik prekės Id.
// Numatytieji (Source Default) priklauso visiems. Neperkami daiktai įrašomi į UserItems, kai pasiekiamas tikslas.
public static class ShopCatalog
{
    public const string Hats = "hats";
    public const string Furs = "furs";
    public const string Tracks = "tracks";
    public const string Backgrounds = "backgrounds";
    public const string Frames = "frames";

    public static readonly ShopItem[] Items =
    {
        new("hat-green", Hats, "Žalia kepurė", "#2E8B4F", 0, ItemSource.Default),
        new("hat-banana", Hats, "Bananų kepurė", "#FFC93C", 150),
        new("hat-red", Hats, "Raudona kepurė", "#C93A3A", 300),
        new("fur-brown", Furs, "Ruda", "#8B5E3C", 0, ItemSource.Default),
        new("fur-gold", Furs, "Auksinė", "#B98300", 400),
        new("fur-dark", Furs, "Tamsi", "#5E3B22", 600, Rarity: "rare"),
        new("track-classic", Tracks, "Klasikinis", "classic", 0, ItemSource.Default),
        new("track-leaf", Tracks, "Lapų", "leaf", 250),
        new("track-water", Tracks, "Vandens", "water", 350),
        new("track-banana", Tracks, "Bananų", "banana", 500),
        new("bg-none", Backgrounds, "Be fono", "none", 0, ItemSource.Default),
        new("bg-mint", Backgrounds, "Mėtinis fonas", "rgba(127, 191, 147, 0.55)", 200),
        new("bg-sun", Backgrounds, "Saulėtas fonas", "rgba(255, 201, 60, 0.45)", 300),
        new("frame-none", Frames, "Be rėmelio", "none", 0, ItemSource.Default),
        new("frame-leaf", Frames, "Lapų rėmelis", "#2E8B4F", 600, Rarity: "rare"),
        new("frame-banana", Frames, "Bananų rėmelis", "#FFC93C", 900, Rarity: "rare"),
        // Prizai už lygius (tik už XP): kas 5 lygius, o 50 ir 100 – po du
        ForLevel(5, "bg-sand", Backgrounds, "Smėlio fonas", "rgba(233, 221, 184, 0.75)", "common"),
        ForLevel(10, "hat-student", Hats, "Mokinio kepuraitė", "#3B5BA9", "common"),
        ForLevel(15, "fur-latte", Furs, "Pienas su šokoladu", "#A67B5B", "common"),
        ForLevel(20, "track-dunes", Tracks, "Smėlio kopos", "dunes", "common"),
        ForLevel(25, "bg-sunset", Backgrounds, "Saulėlydis", "rgba(235, 104, 52, 0.45)", "common"),
        ForLevel(30, "hat-pirate", Hats, "Pirato kepurė", "#2B2B2B", "rare"),
        ForLevel(35, "fur-albino", Furs, "Albinosas", "#F1E6D0", "rare"),
        ForLevel(40, "frame-bronze", Frames, "Bronzinis rėmelis", "#E0A36A", "rare"),
        ForLevel(45, "track-sunwave", Tracks, "Saulės bangos", "sunwave", "rare"),
        ForLevel(50, "hat-crown-small", Hats, "Mažoji karūnėlė", "#FFD866", "rare"),
        ForLevel(50, "bg-fog", Backgrounds, "Džiunglių rūkas", "rgba(127, 191, 147, 0.85)", "rare"),
        ForLevel(55, "fur-blue", Furs, "Mėlynoji beždžionė", "#4F6DB8", "rare"),
        ForLevel(60, "frame-silver", Frames, "Sidabrinis rėmelis", "#D7D9D2", "epic"),
        ForLevel(65, "track-jungle", Tracks, "Džiunglių lapai", "jungle", "epic"),
        ForLevel(70, "hat-tiger", Hats, "Tigro kepurė", "#E27D1A", "epic"),
        ForLevel(75, "fur-tiger", Furs, "Tigro kailis", "#D9822B", "epic"),
        ForLevel(80, "bg-storm", Backgrounds, "Perkūnija", "rgba(98, 80, 214, 0.45)", "epic"),
        ForLevel(85, "track-bananarain", Tracks, "Bananų lietus", "bananarain", "epic"),
        ForLevel(90, "frame-gold", Frames, "Auksinis rėmelis", "#D9A400", "legendary"),
        ForLevel(95, "hat-laurel", Hats, "Laurų vainikas", "#5BA05B", "legendary"),
        ForLevel(100, "hat-king", Hats, "Džiunglių karūna", "#FFD700", "legendary"),
        ForLevel(100, "fur-royal", Furs, "Karališkas kailis", "#C9A227", "legendary")
    };

    private static ShopItem ForLevel(int level, string id, string category, string name, string value, string rarity) =>
        new(id, category, name, value, 0, ItemSource.Level, rarity, $"Pasiek {level} lygį", level);

    // Prizai, gaunami pasiekus tam tikrą lygį
    public static ShopItem[] LevelPrizes(int level) =>
        Items.Where(item => item.Source == ItemSource.Level && item.Level == level).ToArray();

    public static ShopItem Find(string id) => Items.FirstOrDefault(item => item.Id == id);

    // Užsidėta prekė arba kategorijos numatytoji (null, nežinomas ar kitos kategorijos Id)
    public static ShopItem Equipped(string id, string category) =>
        Find(id) is { } item && item.Category == category
            ? item
            : Items.First(item => item.Category == category && item.IsDefault);
}
