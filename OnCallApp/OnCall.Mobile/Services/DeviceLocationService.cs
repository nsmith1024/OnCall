using OnCall.Mobile.Models;

namespace OnCall.Mobile.Services;

public sealed class DeviceLocationService
{
    public async Task<ResolvedLocation> GetCurrentAsync(CancellationToken token = default)
    {
        PermissionStatus permission = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (permission != PermissionStatus.Granted)
            permission = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (permission != PermissionStatus.Granted)
            throw new InvalidOperationException("Location permission is needed to find lawyers serving your area.");

        var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(12));
        Location? location = await Geolocation.Default.GetLocationAsync(request, token);
        if (location is null) throw new InvalidOperationException("Your location could not be determined.");

        Placemark? placemark = (await Geocoding.Default.GetPlacemarksAsync(location.Latitude, location.Longitude)).FirstOrDefault();
        string city = placemark?.Locality ?? placemark?.SubAdminArea ?? "Unknown";
        string state = placemark?.AdminArea ?? "Unknown";
        return new ResolvedLocation(location.Latitude, location.Longitude, city, state);
    }
}
