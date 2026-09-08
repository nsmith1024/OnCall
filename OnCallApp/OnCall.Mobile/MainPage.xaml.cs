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
