using OnCall.Mobile.Models;

namespace OnCall.Mobile.Services.Location;

public sealed class MauiDeviceCoordinateProvider : IDeviceCoordinateProvider
{
    public async Task<GeoCoordinates> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        PermissionStatus permission = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (permission != PermissionStatus.Granted)
            permission = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (permission != PermissionStatus.Granted)
            throw new InvalidOperationException("Location permission is needed to find lawyers serving your area.");

        var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(12));
        Microsoft.Maui.Devices.Sensors.Location? location = await Geolocation.Default.GetLocationAsync(request, cancellationToken);
        if (location is null) throw new InvalidOperationException("Your location could not be determined.");
        return new GeoCoordinates(location.Latitude, location.Longitude);
    }
}
