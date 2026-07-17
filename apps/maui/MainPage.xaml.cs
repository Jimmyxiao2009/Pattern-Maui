using Pattern.Maui.Services;

namespace Pattern.Maui;

public partial class MainPage : ContentPage
{
    private readonly SidecarRuntime _runtime;
    private string _conversation = "欢迎回来。MAUI 客户端不依赖浏览器访问本地地址。";

    public MainPage(SidecarRuntime runtime)
    {
        InitializeComponent();
        _runtime = runtime;
        _runtime.StatusChanged += status => MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = status);
        _runtime.ChatDelta += delta => MainThread.BeginInvokeOnMainThread(() =>
        {
            _conversation += delta;
            ConversationLabel.Text = _conversation;
        });
        Loaded += async (_, _) => await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        try { await _runtime.StartAsync(); }
        catch (Exception error) { StatusLabel.Text = $"运行时启动失败：{error.Message}"; }
    }

    private async void OnReconnectClicked(object? sender, EventArgs e) => await ConnectAsync();
    private async void OnSendClicked(object? sender, EventArgs e) => await SendAsync();
    private async void OnSendCompleted(object? sender, EventArgs e) => await SendAsync();

    private async Task SendAsync()
    {
        var text = MessageEntry.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        MessageEntry.Text = string.Empty;
        _conversation += $"\n\n你：{text}\nPattern：";
        ConversationLabel.Text = _conversation;
        try { await _runtime.SendChatAsync(text); }
        catch (Exception error) { _conversation += $"\n[发送失败：{error.Message}]"; ConversationLabel.Text = _conversation; }
    }
}
