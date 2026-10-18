// Services/ShopCatalog.cs
using System.Linq;

// Value: kepurei ir kailiui – SVG spalva (_Monkey), takeliui – lenktynių juostos stilius (race.css .lane-*)
public sealed record ShopItem(string Id, string Category, string Name, string Value, int Price);

// Parduotuvės prekės. Kainos tik serveryje – naršyklė siunčia tik prekės Id.
// Nemokamos (Price 0) priklauso visiems ir yra numatytosios.
public static class ShopCatalog
{
    public const string Hats = "hats";
    public const string Furs = "furs";
    public const string Tracks = "tracks";

    public static readonly ShopItem[] Items =
    {
        new("hat-green", Hats, "Žalia kepurė", "#2E8B4F", 0),
        new("hat-banana", Hats, "Bananų kepurė", "#FFC93C", 150),
        new("hat-red", Hats, "Raudona kepurė", "#C93A3A", 300),
        new("fur-brown", Furs, "Ruda", "#8B5E3C", 0),
        new("fur-gold", Furs, "Auksinė", "#B98300", 400),
        new("fur-dark", Furs, "Tamsi", "#5E3B22", 600),
        new("track-classic", Tracks, "Klasikinis", "classic", 0),
        new("track-leaf", Tracks, "Lapų", "leaf", 250),
        new("track-banana", Tracks, "Bananų", "banana", 500)
    };

    public static ShopItem Find(string id) => Items.FirstOrDefault(item => item.Id == id);

    // Užsidėta prekė arba kategorijos numatytoji (null, nežinomas ar kitos kategorijos Id)
    public static ShopItem Equipped(string id, string category) =>
        Find(id) is { } item && item.Category == category
            ? item
            : Items.First(item => item.Category == category && item.Price == 0);
}
