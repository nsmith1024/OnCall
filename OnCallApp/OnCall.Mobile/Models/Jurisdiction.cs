namespace OnCall.Mobile.Models;

public sealed record Jurisdiction(string City, string State, string? County = null, string? PostalCode = null);
