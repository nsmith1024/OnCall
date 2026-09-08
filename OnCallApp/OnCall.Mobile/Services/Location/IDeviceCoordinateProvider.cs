using OnCall.Mobile.Models;

namespace OnCall.Mobile.Services.Location;

public interface IDeviceCoordinateProvider
{
    Task<GeoCoordinates> GetCurrentAsync(CancellationToken cancellationToken = default);
}
