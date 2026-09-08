namespace OnCall.Mobile.Services.Maps;

public sealed class MapTileOptions
{
    public string UrlTemplate { get; init; } = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    public string AttributionHtml { get; init; } = "© <a href=\"https://www.openstreetmap.org/copyright\">OpenStreetMap contributors</a>";
    public int InitialZoom { get; init; } = 15;
}
