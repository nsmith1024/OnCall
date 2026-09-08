using System.Text.Json.Serialization;

namespace OnCall.Mobile.Models;

public sealed record UserProfile(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("accountStatus")] string AccountStatus);
