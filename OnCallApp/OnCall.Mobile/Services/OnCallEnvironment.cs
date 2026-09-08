namespace OnCall.Mobile.Services;

public sealed class OnCallEnvironment
{
    public const string FirebaseProjectId = "oncall-564dc";
    public const string EmulatorApiKey = "oncall-emulator-key";

    private static string EmulatorHost => DeviceInfo.Platform == DevicePlatform.Android ? "10.0.2.2" : "127.0.0.1";
    public string AuthBaseUrl => $"http://{EmulatorHost}:9099";
    public string FunctionsBaseUrl => $"http://{EmulatorHost}:5001/{FirebaseProjectId}/us-central1";
}
