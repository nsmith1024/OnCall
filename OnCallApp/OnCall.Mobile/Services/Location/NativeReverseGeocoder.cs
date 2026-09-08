using OnCall.Mobile.Models;

namespace OnCall.Mobile.Services.Location;

public sealed class NativeReverseGeocoder : IReverseGeocoder
{
    public string ProviderName => "native";

    public async Task<Jurisdiction> ReverseGeocodeAsync(GeoCoordinates coordinates, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Placemark? placemark = (await Geocoding.Default.GetPlacemarksAsync(coordinates.Latitude, coordinates.Longitude)).FirstOrDefault();
        cancellationToken.ThrowIfCancellationRequested();
        string city = placemark?.Locality ?? placemark?.SubAdminArea ?? "";
        string state = UsStateCodes.Normalize(placemark?.AdminArea ?? "");
        if (string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(state))
            throw new InvalidOperationException("The city and state could not be determined from your location.");
        return new Jurisdiction(city, state, placemark?.SubAdminArea, placemark?.PostalCode);
    }
}
