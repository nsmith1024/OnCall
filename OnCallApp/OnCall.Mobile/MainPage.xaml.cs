using OnCall.Mobile.Models;
using OnCall.Mobile.Services;

namespace OnCall.Mobile;

public partial class MainPage : ContentPage
{
    private readonly FirebaseAuthService auth;
    private readonly OnCallApiClient api;
    private readonly DeviceLocationService locations;
    private ResolvedLocation? currentLocation;
    private LegalRequestSummary? activeRequest;
    private string? meetingRequestId;
    private bool initialized;
    private bool loadingProfile;

    public MainPage(FirebaseAuthService auth, OnCallApiClient api, DeviceLocationService locations)
    {
        InitializeComponent();
        this.auth = auth;
        this.api = api;
        this.locations = locations;
        IncidentPicker.SelectedIndex = 0;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (initialized) return;
        initialized = true;
        await RunBusyAsync(async () =>
        {
            if (await auth.RestoreAsync() is not null) await LoadProfileAsync();
        }, showErrors: false);
    }

    private async void OnSignInClicked(object? sender, EventArgs e) => await AuthenticateAsync(register: false);
    private async void OnRegisterClicked(object? sender, EventArgs e) => await AuthenticateAsync(register: true);

    private async Task AuthenticateAsync(bool register)
    {
        string email = EmailEntry.Text?.Trim() ?? "";
        string password = PasswordEntry.Text ?? "";
        if (!email.Contains('@') || password.Length < 6)
        {
            StatusLabel.Text = "Enter a valid email and a password of at least 6 characters.";
            return;
        }
        await RunBusyAsync(async () =>
        {
            if (register) await auth.RegisterAsync(email, password); else await auth.SignInAsync(email, password);
            PasswordEntry.Text = "";
            await LoadProfileAsync();
        });
    }

    private async Task LoadProfileAsync()
    {
        UserProfile profile = await api.GetMyProfileAsync();
        WelcomeLabel.Text = $"Welcome, {profile.DisplayName}";
        RoleLabel.Text = $"Role: {profile.Role}";
        EmailLabel.Text = profile.Email ?? auth.CurrentSession?.Email ?? "";
        bool lawyer = string.Equals(profile.Role, "lawyer", StringComparison.OrdinalIgnoreCase);
        loadingProfile = true;
        AvailabilitySwitch.IsToggled = false;
        loadingProfile = false;
        ClientPanel.IsVisible = !lawyer;
        LawyerPanel.IsVisible = lawyer;
        AuthPanel.IsVisible = false;
        HomePanel.IsVisible = true;
        if (lawyer)
        {
            await RefreshActiveAssignmentAsync();
            await RefreshOffersAsync();
        }
        else await RefreshActiveRequestAsync();
    }

    private async void OnRequestLawyerClicked(object? sender, EventArgs e)
    {
        if (currentLocation is null)
        {
            StatusLabel.Text = "Use your current location before requesting a lawyer.";
            return;
        }
        await RunBusyAsync(async () =>
        {
            RequestCreated result = await api.CreateLegalRequestAsync(IncidentPicker.SelectedItem?.ToString() ?? "Other",
                currentLocation.Latitude, currentLocation.Longitude, currentLocation.City, currentLocation.State);
            StatusLabel.Text = result.OfferedLawyerCount > 0
                ? $"Request {result.RequestId[..8]} sent to {result.OfferedLawyerCount} available lawyer(s)."
                : $"Request {result.RequestId[..8]} is searching; no demo lawyer is currently available.";
            await RefreshActiveRequestAsync();
        });
    }

    private async void OnUseLocationClicked(object? sender, EventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            currentLocation = await locations.GetCurrentAsync();
            LocationLabel.Text = $"{currentLocation.City}, {currentLocation.State}";
        });
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        await auth.SignOutAsync();
        HomePanel.IsVisible = false;
        AuthPanel.IsVisible = true;
        StatusLabel.Text = "Signed out.";
    }

    private async void OnRefreshRequestClicked(object? sender, EventArgs e) => await RunBusyAsync(RefreshActiveRequestAsync);

    private async Task RefreshActiveRequestAsync()
    {
        ActiveRequestResult result = await api.GetMyActiveRequestAsync();
        activeRequest = result.Request;
        ActiveRequestPanel.IsVisible = activeRequest is not null;
        meetingRequestId = activeRequest?.Status == "assigned" ? activeRequest.Id : null;
        JoinMeetingButton.IsVisible = meetingRequestId is not null;
        CompleteRequestButton.IsVisible = false;
        RequestStatusLabel.Text = activeRequest is null ? "" : $"{activeRequest.IncidentType} in {activeRequest.City}, {activeRequest.State}: {activeRequest.Status}";
    }

    private async void OnCancelRequestClicked(object? sender, EventArgs e)
    {
        if (activeRequest is null) return;
        bool cancel = await DisplayAlertAsync("Cancel request?", "Available lawyers will be told this request is no longer active.", "Cancel Request", "Keep Searching");
        if (!cancel) return;
        await RunBusyAsync(async () =>
        {
            await api.CancelRequestAsync(activeRequest.Id);
            StatusLabel.Text = "Request cancelled.";
            await RefreshActiveRequestAsync();
        });
    }

    private async void OnAvailabilityToggled(object? sender, ToggledEventArgs e)
    {
        if (loadingProfile || !LawyerPanel.IsVisible) return;
        await RunBusyAsync(async () =>
        {
            AvailabilityResult result = await api.SetAvailabilityAsync(e.Value);
            StatusLabel.Text = result.Available ? "You are available for requests." : "You are off duty.";
            if (result.Available) await RefreshOffersAsync();
        });
    }

    private async void OnRefreshOffersClicked(object? sender, EventArgs e) => await RunBusyAsync(RefreshOffersAsync);

    private async Task RefreshOffersAsync()
    {
        OfferList result = await api.GetMyOffersAsync();
        OffersView.ItemsSource = result.Offers;
    }

    private async void OnOfferSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not LegalOffer offer) return;
        OffersView.SelectedItem = null;
        bool accept = await DisplayAlertAsync("Accept request?", $"{offer.IncidentType} help in {offer.City}, {offer.State}", "Accept", "Cancel");
        if (!accept) return;
        await RunBusyAsync(async () =>
        {
            AssignmentResult result = await api.AcceptOfferAsync(offer.RequestId);
            meetingRequestId = result.RequestId;
            JoinMeetingButton.IsVisible = true;
            CompleteRequestButton.IsVisible = true;
            StatusLabel.Text = $"Request {result.RequestId[..8]} assigned to you.";
            await RefreshOffersAsync();
        });
    }

    private async void OnJoinMeetingClicked(object? sender, EventArgs e)
    {
        if (meetingRequestId is null) return;
        await RunBusyAsync(async () =>
        {
            JitsiSession session = await api.GetMeetingSessionAsync(meetingRequestId);
            if (new Uri(session.ServerUrl).Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))
            {
                StatusLabel.Text = "Meeting authorization works; configure the production Jitsi domain before opening calls.";
                return;
            }
            string url = $"{session.ServerUrl.TrimEnd('/')}/{Uri.EscapeDataString(session.Room)}?jwt={Uri.EscapeDataString(session.Token)}";
            await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
        });
    }

    private async Task RefreshActiveAssignmentAsync()
    {
        ActiveRequestResult result = await api.GetMyActiveAssignmentAsync();
        meetingRequestId = result.Request?.Id;
        JoinMeetingButton.IsVisible = meetingRequestId is not null;
        CompleteRequestButton.IsVisible = meetingRequestId is not null;
    }

    private async void OnCompleteRequestClicked(object? sender, EventArgs e)
    {
        if (meetingRequestId is null) return;
        bool complete = await DisplayAlertAsync("Complete session?", "This closes the legal request and ends access to its meeting.", "Complete", "Keep Open");
        if (!complete) return;
        await RunBusyAsync(async () =>
        {
            await api.CompleteRequestAsync(meetingRequestId);
            meetingRequestId = null;
            JoinMeetingButton.IsVisible = CompleteRequestButton.IsVisible = false;
            StatusLabel.Text = "Legal session completed. Turn availability on when ready for another request.";
        });
    }

    private async Task RunBusyAsync(Func<Task> operation, bool showErrors = true)
    {
        BusyIndicator.IsVisible = BusyIndicator.IsRunning = true;
        StatusLabel.Text = "";
        try { await operation(); }
        catch (Exception ex)
        {
            if (showErrors) StatusLabel.Text = ex.Message;
            if (auth.CurrentSession is null)
            {
                HomePanel.IsVisible = false;
                AuthPanel.IsVisible = true;
            }
        }
        finally { BusyIndicator.IsRunning = BusyIndicator.IsVisible = false; }
    }
}
