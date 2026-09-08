namespace OnCall.Mobile.Models;

public sealed record AuthSession(string UserId, string Email, string IdToken, string RefreshToken, DateTimeOffset ExpiresAt);
