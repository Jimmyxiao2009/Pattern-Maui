using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Maui.Storage;
using System.Runtime.Versioning;

#pragma warning disable CA1416 // SDK checks are explicit; Xamarin binding analyzer cannot infer helper guards

namespace Pattern.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter([Android.Content.Intent.ActionView], Categories = [Android.Content.Intent.CategoryDefault, Android.Content.Intent.CategoryBrowsable], DataScheme = "pattern", DataHost = "pair")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SavePairingIntent(Intent);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            RequestNotificationPermission();
        var intent = new Intent(this, typeof(RelaySyncService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O) StartForegroundServiceO(intent);
        else StartService(intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        SavePairingIntent(intent);
    }

    private static void SavePairingIntent(Intent? intent)
    {
        var value = intent?.DataString;
        if (!string.IsNullOrWhiteSpace(value) && value.StartsWith("pattern://pair", StringComparison.OrdinalIgnoreCase))
            Preferences.Default.Set("pattern.pending.pairing", value);
    }

    [SupportedOSPlatform("android26.0")]
    private void StartForegroundServiceO(Intent intent) => StartForegroundService(intent);

    [SupportedOSPlatform("android23.0")]
    private void RequestNotificationPermission()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            RequestPermissions(["android.permission.POST_NOTIFICATIONS"], 1001);
    }
}
