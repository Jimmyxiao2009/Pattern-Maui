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
            try
            {
                // Reload preferences so pairing/manual changes made in the UI are
                // picked up by the long-lived foreground worker.
                await relay.InitializeAsync();
                var incoming = await relay.SyncAsync(cancellationToken);
                if (incoming.Count > 0) NotifyIncoming(incoming.Count);
            }
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
        manager?.CreateNotificationChannel(new NotificationChannel("pattern-messages", "Pattern 消息", NotificationImportance.Default));
    }

    private void NotifyIncoming(int count)
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        intent.PutExtra("pattern.open.chat", true);
        var flags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M) flags |= PendingIntentFlags.Immutable;
        var pending = PendingIntent.GetActivity(this, 1002, intent, flags);
        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(this, "pattern-messages")
            : new Notification.Builder(this);
        var notification = builder
            .SetContentTitle("Pattern 收到新消息")
            .SetContentText($"有 {count} 条中继消息待查看")
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
            .SetContentIntent(pending)
            .SetAutoCancel(true)
            .Build();
        ((NotificationManager?)GetSystemService(NotificationService))?.Notify(1002, notification);
    }
}
