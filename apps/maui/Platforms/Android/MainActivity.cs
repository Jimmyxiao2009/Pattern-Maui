using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using System.Runtime.Versioning;

#pragma warning disable CA1416 // SDK checks are explicit; Xamarin binding analyzer cannot infer helper guards

namespace Pattern.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var intent = new Intent(this, typeof(RelaySyncService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O) StartForegroundServiceO(intent);
        else StartService(intent);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            RequestNotificationPermission();
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
