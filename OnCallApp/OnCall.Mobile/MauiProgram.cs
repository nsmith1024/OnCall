using Microsoft.Extensions.Logging;

using OnCall.Mobile.Services;

namespace OnCall.Mobile {
    public static class MauiProgram {
        public static MauiApp CreateMauiApp() {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts => {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
            builder.Services.AddSingleton<OnCallEnvironment>();
            builder.Services.AddSingleton<FirebaseAuthService>();
            builder.Services.AddSingleton<OnCallApiClient>();
            builder.Services.AddSingleton<DeviceLocationService>();
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddSingleton<AppShell>();

            return builder.Build();
        }
    }
}
