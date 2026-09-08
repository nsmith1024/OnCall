using OnCall.Mobile.Models;

namespace OnCall.Mobile.Services.Location;

public interface IReverseGeocoder
{
    string ProviderName { get; }
    Task<Jurisdiction> ReverseGeocodeAsync(GeoCoordinates coordinates, CancellationToken cancellationToken = default);
}
