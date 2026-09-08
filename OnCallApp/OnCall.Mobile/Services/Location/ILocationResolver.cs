using OnCall.Mobile.Models;

namespace OnCall.Mobile.Services.Location;

public interface ILocationResolver
{
    Task<ResolvedLocation> ResolveCurrentAsync(CancellationToken cancellationToken = default);
}
