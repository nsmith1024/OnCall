using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OnCall.Mobile.Models;

namespace OnCall.Mobile.Services;

public sealed class FirebaseAuthService(HttpClient http, OnCallEnvironment environment)
{
    private const string SessionKey = "oncall.auth.session";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public AuthSession? CurrentSession { get; private set; }

    public Task<AuthSession> RegisterAsync(string email, string password, CancellationToken token = default) => AuthenticateAsync("signUp", email, password, token);
    public Task<AuthSession> SignInAsync(string email, string password, CancellationToken token = default) => AuthenticateAsync("signInWithPassword", email, password, token);

    public async Task<AuthSession?> RestoreAsync(CancellationToken token = default)
    {
        string? json = await SecureStorage.Default.GetAsync(SessionKey);
        if (string.IsNullOrWhiteSpace(json)) return null;
        CurrentSession = JsonSerializer.Deserialize<AuthSession>(json, JsonOptions);
        if (CurrentSession is null) return null;
        return CurrentSession.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2) ? CurrentSession : await RefreshAsync(CurrentSession, token);
    }

    public Task SignOutAsync()
    {
        CurrentSession = null;
        SecureStorage.Default.Remove(SessionKey);
        return Task.CompletedTask;
    }

    public async Task<string> GetValidIdTokenAsync(CancellationToken token = default)
    {
        AuthSession session = CurrentSession ?? await RestoreAsync(token) ?? throw new InvalidOperationException("Please sign in.");
        if (session.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(2)) session = await RefreshAsync(session, token);
        return session.IdToken;
    }

    private async Task<AuthSession> AuthenticateAsync(string operation, string email, string password, CancellationToken token)
    {
        string url = $"{environment.AuthBaseUrl}/identitytoolkit.googleapis.com/v1/accounts:{operation}?key={OnCallEnvironment.EmulatorApiKey}";
        using HttpResponseMessage response = await http.PostAsJsonAsync(url, new {email = email.Trim(), password, returnSecureToken = true}, token);
        AuthResponse body = await ReadAsync<AuthResponse>(response, token);
        var session = new AuthSession(body.LocalId, body.Email, body.IdToken, body.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(Lifetime(body.ExpiresIn)));
        await SaveAsync(session);
        return session;
    }

    private async Task<AuthSession> RefreshAsync(AuthSession previous, CancellationToken token)
    {
        string url = $"{environment.AuthBaseUrl}/securetoken.googleapis.com/v1/token?key={OnCallEnvironment.EmulatorApiKey}";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = previous.RefreshToken });
        using HttpResponseMessage response = await http.PostAsync(url, content, token);
        RefreshResponse body = await ReadAsync<RefreshResponse>(response, token);
        var session = new AuthSession(body.UserId, previous.Email, body.IdToken, body.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(Lifetime(body.ExpiresIn)));
        await SaveAsync(session);
        return session;
    }

    private async Task SaveAsync(AuthSession session)
    {
        CurrentSession = session;
        await SecureStorage.Default.SetAsync(SessionKey, JsonSerializer.Serialize(session, JsonOptions));
    }

    private static int Lifetime(string value) => int.TryParse(value, out int seconds) ? seconds : 3600;

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken token)
    {
        string json = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            string message = "Firebase request failed.";
            try { using JsonDocument doc = JsonDocument.Parse(json); message = doc.RootElement.GetProperty("error").GetProperty("message").GetString() ?? message; }
            catch (JsonException) { }
            throw new InvalidOperationException(message.Replace('_', ' ').ToLowerInvariant());
        }
        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new InvalidOperationException("Firebase returned an empty response.");
    }

    private sealed record AuthResponse([property: JsonPropertyName("localId")] string LocalId, [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("idToken")] string IdToken, [property: JsonPropertyName("refreshToken")] string RefreshToken,
        [property: JsonPropertyName("expiresIn")] string ExpiresIn);
    private sealed record RefreshResponse([property: JsonPropertyName("user_id")] string UserId, [property: JsonPropertyName("id_token")] string IdToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken, [property: JsonPropertyName("expires_in")] string ExpiresIn);
}
