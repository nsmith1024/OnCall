using OnCall.Mobile.Models;

namespace OnCall.Mobile.Services.Maps;

public sealed class OpenStreetMapClientLocationMap(OpenStreetMapHtmlRenderer renderer) : IClientLocationMap
{
    public async Task ShowAsync(Page owner, AssignedRequestDetails request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = new HtmlWebViewSource {Html = renderer.Render(request.Location)};
        var map = new WebView {Source = source};
        var coordinates = new Label
        {
            Text = $"Location captured when the request was submitted\n{request.Location.Latitude:F5}, {request.Location.Longitude:F5}",
            Padding = 12, HorizontalTextAlignment = TextAlignment.Center,
        };
        var grid = new Grid {RowDefinitions = {new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)}};
        grid.Add(map);
        grid.Add(coordinates);
        Grid.SetRow(coordinates, 1);
        var page = new ContentPage {Title = "Client Location", Content = grid};
        await owner.Navigation.PushAsync(page);
    }
}
