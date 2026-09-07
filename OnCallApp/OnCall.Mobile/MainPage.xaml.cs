using System.Text.Json;

namespace OnCall.Mobile;

public partial class MainPage : ContentPage {
    private static readonly HttpClient client = new HttpClient();

    public MainPage() {
        InitializeComponent();
    }

    private async void OnFetchButtonClicked(object sender, EventArgs e) {
        ResultLabel.Text = "Fetching from Firebase...";

        try {
            // Paste your actual function URL from Step 1 here
            string firebaseUrl = "http://127.0.0.1:5001/oncall-564dc/us-central1/getValue";
            HttpResponseMessage response = await client.GetAsync(firebaseUrl);
            response.EnsureSuccessStatusCode();

            string jsonResponse = await response.Content.ReadAsStringAsync();

            // Parses {"value": "Hello from Firebase Firestore!"}
            using var doc = JsonDocument.Parse(jsonResponse);
            string result = doc.RootElement.GetProperty("value").GetString();

            ResultLabel.Text = $"Database Output: {result}";
        } catch (Exception ex) {
            ResultLabel.Text = $"Error: {ex.Message}";
        }
    }
}