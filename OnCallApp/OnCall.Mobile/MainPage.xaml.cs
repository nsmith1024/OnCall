using OnCall.Mobile.Models;
using OnCall.Mobile.Services;

namespace OnCall.Mobile;

public partial class MainPage : ContentPage
{
    private readonly FirebaseAuthService auth;
    private readonly OnCallApiClient api;
    private readonly DeviceLocationService locations;
    private ResolvedLocation? currentLocation;
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
            StatusLabel.Text = $"Request {result.RequestId[..8]} assigned to you.";
            await RefreshOffersAsync();
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
