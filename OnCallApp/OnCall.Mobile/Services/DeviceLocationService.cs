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
        string state = StateCode(placemark?.AdminArea ?? "");
        if (string.IsNullOrEmpty(state)) throw new InvalidOperationException("The state could not be determined from your location.");
        return new ResolvedLocation(location.Latitude, location.Longitude, city, state);
    }

    private static string StateCode(string value)
    {
        if (value.Length == 2) return value.ToUpperInvariant();
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Alabama"]="AL", ["Alaska"]="AK", ["Arizona"]="AZ", ["Arkansas"]="AR", ["California"]="CA",
            ["Colorado"]="CO", ["Connecticut"]="CT", ["Delaware"]="DE", ["Florida"]="FL", ["Georgia"]="GA",
            ["Hawaii"]="HI", ["Idaho"]="ID", ["Illinois"]="IL", ["Indiana"]="IN", ["Iowa"]="IA",
            ["Kansas"]="KS", ["Kentucky"]="KY", ["Louisiana"]="LA", ["Maine"]="ME", ["Maryland"]="MD",
            ["Massachusetts"]="MA", ["Michigan"]="MI", ["Minnesota"]="MN", ["Mississippi"]="MS", ["Missouri"]="MO",
            ["Montana"]="MT", ["Nebraska"]="NE", ["Nevada"]="NV", ["New Hampshire"]="NH", ["New Jersey"]="NJ",
            ["New Mexico"]="NM", ["New York"]="NY", ["North Carolina"]="NC", ["North Dakota"]="ND", ["Ohio"]="OH",
            ["Oklahoma"]="OK", ["Oregon"]="OR", ["Pennsylvania"]="PA", ["Rhode Island"]="RI", ["South Carolina"]="SC",
            ["South Dakota"]="SD", ["Tennessee"]="TN", ["Texas"]="TX", ["Utah"]="UT", ["Vermont"]="VT",
            ["Virginia"]="VA", ["Washington"]="WA", ["West Virginia"]="WV", ["Wisconsin"]="WI", ["Wyoming"]="WY",
            ["District of Columbia"]="DC",
        }.GetValueOrDefault(value, "");
    }
}
