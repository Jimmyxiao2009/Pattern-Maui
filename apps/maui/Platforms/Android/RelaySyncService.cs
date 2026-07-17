using Android.App;
using Android.Content;
using Android.OS;
using Pattern.Maui.Services;

#pragma warning disable CA1416, CA1422 // runtime SDK guards below are required by Android's binding analyzer

namespace Pattern.Maui;

[Service(Exported = false, ForegroundServiceType = Android.Content.PM.ForegroundService.TypeDataSync)]
public sealed class RelaySyncService : Service
{
    private CancellationTokenSource? _cts;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        _cts ??= new CancellationTokenSource();
        CreateChannel();
        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(this, "pattern-relay")
            : new Notification.Builder(this);
        var notification = builder
            .SetContentTitle("Pattern 中继")
            .SetContentText("后台同步已启用")
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
            .SetOngoing(true)
            .Build();
        StartForeground(1001, notification);
        _ = SyncLoopAsync(_cts.Token);
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        base.OnDestroy();
    }

    private async Task SyncLoopAsync(CancellationToken cancellationToken)
    {
        var relay = new RelayService();
        await relay.InitializeAsync();
        while (!cancellationToken.IsCancellationRequested)
        {
            try { await relay.SyncAsync(cancellationToken); }
            catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch { /* status is visible in the app's relay page */ }
            try { await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken); }
            catch (System.OperationCanceledException) { break; }
        }
    }

    private void CreateChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(new NotificationChannel("pattern-relay", "Pattern 中继", NotificationImportance.Low));
    }
}
