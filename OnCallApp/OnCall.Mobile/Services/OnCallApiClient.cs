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
    public Task<OfferList> GetMyOffersAsync(CancellationToken token = default) => SendAsync<OfferList>(HttpMethod.Get, "getMyOffers", null, token);
    public Task<ActiveRequestResult> GetMyActiveRequestAsync(CancellationToken token = default) =>
        SendAsync<ActiveRequestResult>(HttpMethod.Get, "getMyActiveRequest", null, token);
    public Task<ActiveRequestResult> GetMyActiveAssignmentAsync(CancellationToken token = default) =>
        SendAsync<ActiveRequestResult>(HttpMethod.Get, "getMyActiveAssignment", null, token);
    public Task<RequestState> CancelRequestAsync(string requestId, CancellationToken token = default) =>
        SendAsync<RequestState>(HttpMethod.Post, "cancelLegalRequest", new {requestId}, token);
    public Task<AvailabilityResult> SetAvailabilityAsync(bool available, CancellationToken token = default) =>
        SendAsync<AvailabilityResult>(HttpMethod.Post, "setLawyerAvailability", new {available}, token);
    public Task<AssignmentResult> AcceptOfferAsync(string requestId, CancellationToken token = default) =>
        SendAsync<AssignmentResult>(HttpMethod.Post, "acceptLegalRequest", new {requestId}, token);
    public Task<JitsiSession> GetMeetingSessionAsync(string requestId, CancellationToken token = default) =>
        SendAsync<JitsiSession>(HttpMethod.Post, "getMeetingSession", new {requestId}, token);
    public Task<RequestState> CompleteRequestAsync(string requestId, CancellationToken token = default) =>
        SendAsync<RequestState>(HttpMethod.Post, "completeLegalRequest", new {requestId}, token);

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

public sealed record RequestCreated(string RequestId, string Status, int OfferedLawyerCount);
public sealed record LegalOffer(string Id, string RequestId, string IncidentType, string City, string State, string Status);
public sealed record OfferList(IReadOnlyList<LegalOffer> Offers);
public sealed record AvailabilityResult(bool Available);
public sealed record AssignmentResult(string RequestId, string Status, string LawyerId);
public sealed record LegalRequestSummary(string Id, string IncidentType, string City, string State, string Status, string? AssignedLawyerId);
public sealed record ActiveRequestResult(LegalRequestSummary? Request);
public sealed record RequestState(string RequestId, string Status);
public sealed record JitsiSession(string ServerUrl, string Room, string Token, DateTimeOffset ExpiresAt);
