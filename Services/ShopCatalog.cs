// Services/ShopCatalog.cs
using System.Linq;

// Iš kur daiktas gaunamas: Default – nemokamas numatytasis, Shop – perkamas už bananus,
// kiti – tik už pasiekimus žaidime (nusipirkti negalima)
public enum ItemSource { Default, Shop, Level, Lesson, Achievement }

// Value: kepurei ir kailiui – SVG spalva (_Monkey), fonui ir rėmeliui – CSS/SVG spalva ("none" – nėra),
// takeliui – lenktynių juostos stilius (race.css .lane-*).
// Unlock: kaip gauti daiktą, kai jis neperkamas (rodoma parduotuvėje)
public sealed record ShopItem(string Id, string Category, string Name, string Value, int Price,
    ItemSource Source = ItemSource.Shop, string Rarity = "common", string Unlock = null)
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
        new("frame-banana", Frames, "Bananų rėmelis", "#FFC93C", 900, Rarity: "rare")
    };

    public static ShopItem Find(string id) => Items.FirstOrDefault(item => item.Id == id);

    // Užsidėta prekė arba kategorijos numatytoji (null, nežinomas ar kitos kategorijos Id)
    public static ShopItem Equipped(string id, string category) =>
        Find(id) is { } item && item.Category == category
            ? item
            : Items.First(item => item.Category == category && item.IsDefault);
}
