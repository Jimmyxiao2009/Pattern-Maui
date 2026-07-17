using Microsoft.Extensions.Logging;
using Pattern.Maui.Services;

namespace Pattern.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddSingleton<NativeBridgeService>(_ =>
        {
            var bridge = new NativeBridgeService();
            bridge.Start();
            return bridge;
        });
        builder.Services.AddSingleton<SidecarRuntime>();
        builder.Services.AddSingleton<AppSettingsStore>();
        builder.Services.AddSingleton<RelayService>();
        builder.Services.AddSingleton<MainPage>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
