using OnCall.Mobile.Models;

namespace OnCall.Mobile.Services.Maps;

public interface IClientLocationMap
{
    Task ShowAsync(Page owner, AssignedRequestDetails request, CancellationToken cancellationToken = default);
}
