using System.Text.Json;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Storage;
using Pattern.Maui.Services;
using Pattern.Maui.Views;

namespace Pattern.Maui;

public partial class MainPage : ContentPage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly (string Id, string Label, string Glyph)[] NavigationItems =
    [
        ("chat", "对话", ""),
        ("project", "项目", ""),
        ("conversations", "管理", ""),
        ("memory", "记忆", ""),
        ("goals", "目标", ""),
        ("tasks", "任务", ""),
        ("proactive", "主动", ""),
        ("workflows", "技能", ""),
        ("mcp", "工具", ""),
        ("channels", "通道", ""),
        ("settings", "设置", ""),
    ];

    private readonly SidecarRuntime _runtime;
    private readonly AppSettingsStore _settings;
    private readonly RelayService _relay;
    private readonly NativeBridgeService _bridge;
    private readonly SingleInstanceService _instance;
    private readonly GlobalHotkeyService _hotkey;
    private readonly WindowsTrayService _tray;
    private readonly AutostartService _autostart;
    private readonly List<ChatTurn> _history = [];
    private readonly List<string> _attachedPaths = [];
    private readonly List<ConversationSession> _sessions = [];
    private readonly Dictionary<string, View> _views = [];
    private readonly Dictionary<string, Button> _navigationButtons = [];
    private readonly Dictionary<string, Label> _navigationIcons = [];
    private readonly Dictionary<string, Label> _navigationLabels = [];
    private bool _loaded;

    public MainPage(SidecarRuntime runtime, AppSettingsStore settings, RelayService relay, NativeBridgeService bridge, SingleInstanceService instance, GlobalHotkeyService hotkey, WindowsTrayService tray, AutostartService autostart)
    {
        InitializeComponent();
        _runtime = runtime;
        _settings = settings;
        _relay = relay;
        _bridge = bridge;
        _instance = instance;
        _hotkey = hotkey;
        _tray = tray;
        _autostart = autostart;
        LoadHistory();

        _hotkey.QuickChatRequested += () => MainThread.BeginInvokeOnMainThread(() =>
        {
            ShowView("chat");
            StatusLabel.Text = "快捷窗口已打开（Ctrl+Alt+P）";
        });
        _tray.ShowRequested += () => MainThread.BeginInvokeOnMainThread(() => ShowView("chat"));
        MacCatalystMenuService.QuickChatRequested += () => MainThread.BeginInvokeOnMainThread(() => ShowView("chat"));

        BuildNavigationRail();

        _runtime.StatusChanged += status => MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = status);
        _runtime.RuntimeEvent += message => MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (message.TryGetProperty("state", out var state)) StatusLabel.Text = $"运行时：{state.GetString()}";
            if (message.TryGetProperty("item", out _)) StatusLabel.Text = "收到新的主动消息";
            if (message.TryGetProperty("type", out var type))
            {
                switch (type.GetString())
                {
                    case "task.updated": StatusLabel.Text = "任务状态已更新"; break;
                    case "task.screenshot": StatusLabel.Text = "任务收到新的桌面回执"; break;
                    case "task.approval_required": await ShowApprovalReviewAsync(message); break;
                }
            }
        });
        _runtime.ChatDelta += delta => MainThread.BeginInvokeOnMainThread(() =>
        {
            // Chat streaming handled by view
        });
        _runtime.ChatDone += () => MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusLabel.Text = "运行时已连接";
        });
        _runtime.ChatCancelled += () => MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusLabel.Text = "回复已取消";
        });
        _runtime.ChatError += error => MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusLabel.Text = "回复失败，运行时仍保持连接";
        });

        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            ShowView("chat");
            if (!_instance.IsPrimary)
            {
                StatusLabel.Text = "Pattern 已在运行；本窗口不会启动第二个运行时。";
                return;
            }
            await ConnectAsync();
        };
    }

    private void BuildNavigationRail()
    {
        foreach (var (id, label, glyph) in NavigationItems)
        {
            var button = new Button
            {
                Text = label,
                WidthRequest = 56,
                HeightRequest = 52,
                Padding = new Thickness(2, 3),
                FontSize = 10,
                TextColor = (Color)Application.Current!.Resources["TextFaint"],
                BackgroundColor = Colors.Transparent,
                BorderWidth = 0,
                CornerRadius = 6,
            };
            button.Clicked += (_, _) => ShowView(id);
            var icon = new Label
            {
                Text = NavigationSymbol(id),
                FontFamily = "Segoe UI Symbol",
                FontSize = 17,
                TextColor = (Color)Application.Current.Resources["TextFaint"],
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            };
            var caption = new Label
            {
                Text = label,
                FontSize = 10,
                TextColor = (Color)Application.Current.Resources["TextFaint"],
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            };
            var railContent = new VerticalStackLayout { Spacing = 1, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Center, Children = { icon, caption } };
            var railCell = new Grid { WidthRequest = 56, HeightRequest = 52, Children = { railContent, button } };
            button.BackgroundColor = Colors.Transparent;
            button.TextColor = Colors.Transparent;
            button.BorderWidth = 0;
            NavigationBar.Children.Add(railCell);
            _navigationButtons[id] = button;
            _navigationIcons[id] = icon;
            _navigationLabels[id] = caption;
        }
    }

    private void LoadHistory()
    {
        try
        {
            _sessions.Clear();
            var raw = _settings.LoadConversationHistory();
            var turns = string.IsNullOrWhiteSpace(raw) ? [] : JsonSerializer.Deserialize<List<ChatTurn>>(raw, JsonOptions) ?? [];
            if (turns.Count > 0)
            {
                _sessions.Add(new ConversationSession("default", "默认会话", turns.TakeLast(100).ToList(), false, DateTimeOffset.UtcNow));
            }
        }
        catch { _history.Clear(); }
    }

    private async Task ConnectAsync()
    {
        try
        {
            await _relay.InitializeAsync();
            if (OperatingSystem.IsAndroid())
            {
                var pairedFromLink = await _relay.ConsumePendingPairingAsync();
                StatusLabel.Text = _relay.Status.Configured
                    ? (pairedFromLink ? "已通过配对链接配置中继" : "移动端中继已配置")
                    : "移动端中继模式";
                return;
            }
            await _runtime.StartAsync();
            var profile = _settings.LoadProfile();
            var key = await _settings.LoadApiKeyAsync();
            await _runtime.ConfigureAsync(new
            {
                provider = profile.Provider,
                endpoint = profile.Endpoint,
                model = profile.Model,
                apiKey = string.IsNullOrWhiteSpace(key) ? Environment.GetEnvironmentVariable("PATTERN_API_KEY") ?? string.Empty : key,
                persona = profile.Persona,
                personaName = "Pattern",
                userName = profile.UserName,
            });
            StatusLabel.Text = "运行时已连接 · stdio";
        }
        catch (Exception error) { StatusLabel.Text = $"运行时启动失败：{error.Message}"; }
    }

    private void OnReconnectClicked(object? sender, EventArgs e) => _ = ConnectAsync();

    private void ShowView(string id)
    {
        if (!_views.TryGetValue(id, out var view))
        {
            view = id switch
            {
                "chat" => ChatView.Create(_runtime, _settings),
                "project" => ProjectView.Create(),
                "conversations" => ConversationsView.Create(),
                "memory" => MemoryView.Create(),
                "goals" => GoalsView.Create(),
                "tasks" => TasksView.Create(_runtime),
                "proactive" => ProactiveView.Create(),
                "workflows" => WorkflowsView.Create(_runtime),
                "mcp" => McpView.Create(_runtime),
                "channels" => ChannelsView.Create(),
                "settings" => SettingsView.Create(),
                _ => new Label { Text = "页面建设中", HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
            };
            _views[id] = view;
        }
        ContentHost.Content = view;
        ViewTitleLabel.Text = NavigationItems.FirstOrDefault(item => item.Id == id).Label ?? "工作区";
        foreach (var (itemId, button) in _navigationButtons)
        {
            var active = itemId == id;
            button.BackgroundColor = active
                ? (Color)Application.Current!.Resources["AccentWash"]
                : Colors.Transparent;
            button.TextColor = active
                ? (Color)Application.Current!.Resources["Accent"]
                : (Color)Application.Current!.Resources["TextFaint"];
            if (_navigationIcons.TryGetValue(itemId, out var icon)) icon.TextColor = button.TextColor;
            if (_navigationLabels.TryGetValue(itemId, out var caption)) caption.TextColor = button.TextColor;
        }
    }

    private static string NavigationSymbol(string id) => id switch
    {
        "chat" => "●",
        "project" => "▦",
        "conversations" => "▤",
        "memory" => "◇",
        "goals" => "◎",
        "tasks" => "☷",
        "proactive" => "✦",
        "workflows" => "◆",
        "mcp" => "⚒",
        "channels" => "➤",
        "settings" => "⚙",
        _ => "•",
    };

    private async Task ShowApprovalReviewAsync(JsonElement message)
    {
        var taskId = message.TryGetProperty("taskId", out var id) ? id.GetString() : null;
        if (string.IsNullOrWhiteSpace(taskId)) return;
        var detail = "Pattern 请求执行一项需要确认的桌面操作。";
        if (message.TryGetProperty("step", out var step) && step.ValueKind == JsonValueKind.Object)
        {
            var action = step.TryGetProperty("action", out var actionValue) ? actionValue.GetString() : "操作";
            var reason = step.TryGetProperty("detail", out var reasonValue) ? reasonValue.GetString() : "";
            detail = $"动作：{action}\n原因：{reason}";
        }
        var choice = await DisplayActionSheetAsync($"需要人工审批\n{detail}", "拒绝", null, "批准");
        var actionName = choice == "批准" ? "approve" : "reject";
        try
        {
            await _runtime.RequestAsync(new { type = "task.control", taskId, action = actionName });
            StatusLabel.Text = actionName == "approve" ? "已批准任务操作" : "已拒绝任务操作";
        }
        catch (Exception error) { StatusLabel.Text = $"审批回传失败：{error.Message}"; }
    }

    private sealed record ChatTurn(string Role, string Content);
    private sealed record ConversationSession(string Id, string Title, List<ChatTurn> Messages, bool Archived, DateTimeOffset UpdatedAt);
}
