using OnCall.Mobile.Models;

namespace OnCall.Mobile.Services.Location;

public sealed class LocationResolver(IDeviceCoordinateProvider coordinates, IReverseGeocoder geocoder) : ILocationResolver
{
    public async Task<ResolvedLocation> ResolveCurrentAsync(CancellationToken cancellationToken = default)
    {
        GeoCoordinates point = await coordinates.GetCurrentAsync(cancellationToken);
        Jurisdiction jurisdiction = await geocoder.ReverseGeocodeAsync(point, cancellationToken);
        return new ResolvedLocation(point, jurisdiction, geocoder.ProviderName);
    }
}
