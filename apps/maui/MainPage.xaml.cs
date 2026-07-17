using Pattern.Maui.Services;

namespace Pattern.Maui;

public partial class MainPage : ContentPage
{
    private readonly SidecarRuntime _runtime;
    private string _conversation = "欢迎回来。MAUI 客户端不依赖浏览器访问本地地址。";
    private readonly List<ChatTurn> _history = [];
    private string _activeAssistantText = string.Empty;

    public MainPage(SidecarRuntime runtime)
    {
        InitializeComponent();
        _runtime = runtime;
        _runtime.StatusChanged += status => MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = status);
        _runtime.ChatDelta += delta => MainThread.BeginInvokeOnMainThread(() =>
        {
            _activeAssistantText += delta;
            _conversation += delta;
            ConversationLabel.Text = _conversation;
        });
        _runtime.ChatDone += () => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!string.IsNullOrWhiteSpace(_activeAssistantText)) _history.Add(new ChatTurn("assistant", _activeAssistantText));
            _activeAssistantText = string.Empty;
            StatusLabel.Text = "运行时已连接";
        });
        _runtime.ChatCancelled += () => MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = "回复已取消");
        _runtime.ChatError += error => MainThread.BeginInvokeOnMainThread(() =>
        {
            _conversation += $"\n[回复失败：{error}]";
            ConversationLabel.Text = _conversation;
            StatusLabel.Text = "回复失败，运行时仍保持连接";
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
        var priorHistory = _history.ToArray();
        _activeAssistantText = string.Empty;
        _conversation += $"\n\n你：{text}\nPattern：";
        ConversationLabel.Text = _conversation;
        try
        {
            await _runtime.SendChatAsync(text, priorHistory);
            _history.Add(new ChatTurn("user", text));
        }
        catch (Exception error)
        {
            _conversation += $"\n[发送失败：{error.Message}]";
            ConversationLabel.Text = _conversation;
        }
    }
}
