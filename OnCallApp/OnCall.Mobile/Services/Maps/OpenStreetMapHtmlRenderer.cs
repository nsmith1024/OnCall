using System.Globalization;
using System.Net;
using System.Text;
using OnCall.Mobile.Models;

namespace OnCall.Mobile.Services.Maps;

public sealed class OpenStreetMapHtmlRenderer(MapTileOptions options)
{
    public string Render(ClientMapLocation location)
    {
        int zoom = Math.Clamp(options.InitialZoom, 1, 19);
        double tileCount = Math.Pow(2, zoom);
        double x = (location.Longitude + 180d) / 360d * tileCount;
        double latitudeRadians = location.Latitude * Math.PI / 180d;
        double y = (1d - Math.Asinh(Math.Tan(latitudeRadians)) / Math.PI) / 2d * tileCount;
        int centerX = (int)Math.Floor(x);
        int centerY = (int)Math.Floor(y);
        var tiles = new StringBuilder();

        for (int offsetY = -2; offsetY <= 2; offsetY++)
        for (int offsetX = -2; offsetX <= 2; offsetX++)
        {
            int tileX = ((centerX + offsetX) % (int)tileCount + (int)tileCount) % (int)tileCount;
            int tileY = Math.Clamp(centerY + offsetY, 0, (int)tileCount - 1);
            string url = options.UrlTemplate.Replace("{z}", zoom.ToString(CultureInfo.InvariantCulture))
                .Replace("{x}", tileX.ToString(CultureInfo.InvariantCulture))
                .Replace("{y}", tileY.ToString(CultureInfo.InvariantCulture));
            double left = (centerX + offsetX - x) * 256d;
            double top = (centerY + offsetY - y) * 256d;
            tiles.Append(CultureInfo.InvariantCulture, $"<img class=\"tile\" alt=\"\" src=\"{WebUtility.HtmlEncode(url)}\" style=\"left:calc(50% + {left:F2}px);top:calc(50% + {top:F2}px)\">");
        }

        string label = WebUtility.HtmlEncode($"Client location - {location.City}, {location.State}");
        return $$"""
            <!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src https:; style-src 'unsafe-inline';">
            <style>
              html,body,#map{margin:0;width:100%;height:100%;overflow:hidden;background:#dde5d4;font-family:sans-serif}
              .tile{position:absolute;width:256px;height:256px;max-width:none}
              .pin{position:absolute;left:50%;top:50%;width:24px;height:24px;margin:-24px 0 0 -12px;background:#b42318;border:3px solid white;border-radius:50% 50% 50% 0;transform:rotate(-45deg);box-shadow:0 2px 5px #333}
              .pin:after{content:'';position:absolute;width:8px;height:8px;left:5px;top:5px;background:white;border-radius:50%}
              .label{position:absolute;left:12px;top:12px;right:12px;padding:9px 12px;background:rgba(255,255,255,.92);border-radius:8px;font-weight:600;text-align:center}
              .attribution{position:absolute;right:4px;bottom:4px;padding:3px 5px;background:rgba(255,255,255,.85);font-size:11px;color:#333}
              .attribution a{color:#2457a6}
            </style></head><body><div id="map">{{tiles}}<div class="pin"></div><div class="label">{{label}}</div>
            <div class="attribution">{{options.AttributionHtml}}</div></div></body></html>
            """;
    }
}
