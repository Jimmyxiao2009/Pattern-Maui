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
        builder.Services.AddSingleton<SingleInstanceService>(_ =>
        {
            var instance = new SingleInstanceService();
            instance.Acquire();
            return instance;
        });
        builder.Services.AddSingleton<GlobalHotkeyService>(_ =>
        {
            var hotkey = new GlobalHotkeyService();
            hotkey.Start();
            return hotkey;
        });
        builder.Services.AddSingleton<AppSettingsStore>();
        builder.Services.AddSingleton<RelayService>();
        builder.Services.AddSingleton<MainPage>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
