using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OnCall.Mobile.Models;

namespace OnCall.Mobile.Services;

public sealed class OnCallApiClient(HttpClient http, OnCallEnvironment environment, FirebaseAuthService auth)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public Task<UserProfile> GetMyProfileAsync(CancellationToken token = default) => SendAsync<UserProfile>(HttpMethod.Get, "getMyProfile", null, token);
    public Task<RequestCreated> CreateLegalRequestAsync(string type, double latitude, double longitude, string city, string state, CancellationToken token = default) =>
        SendAsync<RequestCreated>(HttpMethod.Post, "createLegalRequest", new {incidentType = type, latitude, longitude, city, state}, token);

    private async Task<T> SendAsync<T>(HttpMethod method, string function, object? body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, $"{environment.FunctionsBaseUrl}/{function}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await auth.GetValidIdTokenAsync(token));
        if (body is not null) request.Content = JsonContent.Create(body);
        using HttpResponseMessage response = await http.SendAsync(request, token);
        string json = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            string message = $"Server request failed ({(int)response.StatusCode}).";
            try { using JsonDocument doc = JsonDocument.Parse(json); message = doc.RootElement.GetProperty("error").GetString() ?? message; }
            catch (JsonException) { }
            throw new InvalidOperationException(message.Replace('_', ' ').ToLowerInvariant());
        }
        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new InvalidOperationException("Server returned an empty response.");
    }
}

public sealed record RequestCreated(string RequestId, string Status);
