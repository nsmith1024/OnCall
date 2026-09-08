namespace OnCall.Mobile.Models;

public sealed record AssignedRequestDetails(string RequestId, string IncidentType, ClientMapLocation Location);

public sealed record ClientMapLocation(double Latitude, double Longitude, string City, string State, string? County, string? PostalCode);
