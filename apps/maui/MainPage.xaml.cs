using System.Text.Json;
using Microsoft.Maui.Controls.Shapes;
using Pattern.Maui.Services;

namespace Pattern.Maui;

public partial class MainPage : ContentPage
{
    private readonly SidecarRuntime _runtime;
    private readonly List<ChatTurn> _history = [];
    private readonly Dictionary<string, View> _views = [];
    private Label? _conversationLabel;
    private Entry? _messageEntry;
    private string _conversation = "欢迎回来。MAUI 客户端不依赖浏览器地址，原生进程直接连接 Agent。";
    private string _activeAssistantText = string.Empty;

    private static readonly (string Id, string Label)[] NavigationItems =
    [
        ("chat", "对话"), ("project", "项目"), ("conversations", "管理"), ("memory", "记忆"),
        ("goals", "目标"), ("tasks", "任务"), ("proactive", "主动"), ("workflows", "技能"), ("mcp", "工具"),
        ("channels", "通道"), ("settings", "设置")
    ];

    public MainPage(SidecarRuntime runtime)
    {
        InitializeComponent();
        _runtime = runtime;
        foreach (var (id, label) in NavigationItems)
        {
            var button = new Button { Text = label, Padding = new Thickness(14, 6), FontSize = 13 };
            button.Clicked += (_, _) => ShowView(id);
            NavigationBar.Children.Add(button);
        }
        _runtime.StatusChanged += status => MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = status);
        _runtime.ChatDelta += delta => MainThread.BeginInvokeOnMainThread(() =>
        {
            _activeAssistantText += delta;
            _conversation += delta;
            if (_conversationLabel is not null) _conversationLabel.Text = _conversation;
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
            if (_conversationLabel is not null) _conversationLabel.Text = _conversation;
            StatusLabel.Text = "回复失败，运行时仍保持连接";
        });
        Loaded += async (_, _) => { ShowView("chat"); await ConnectAsync(); };
    }

    private async Task ConnectAsync()
    {
        try { await _runtime.StartAsync(); }
        catch (Exception error) { StatusLabel.Text = $"运行时启动失败：{error.Message}"; }
    }

    private async void OnReconnectClicked(object? sender, EventArgs e) => await ConnectAsync();

    private void ShowView(string id)
    {
        if (!_views.TryGetValue(id, out var view))
        {
            view = id == "chat" ? CreateChatView() : CreateFeatureView(id);
            _views[id] = view;
        }
        ContentHost.Content = view;
    }

    private View CreateChatView()
    {
        _conversationLabel = new Label { Text = _conversation, FontSize = 16, LineBreakMode = LineBreakMode.WordWrap };
        var transcript = new Border
        {
            BackgroundColor = (Color)Application.Current!.Resources["PanelBackground"],
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Padding = 18,
            Content = new ScrollView { Content = _conversationLabel }
        };
        _messageEntry = new Entry { Placeholder = "给 Pattern 发消息", ReturnType = ReturnType.Send };
        _messageEntry.Completed += async (_, _) => await SendAsync();
        var send = new Button { Text = "发送", BackgroundColor = (Color)Application.Current!.Resources["Accent"], TextColor = (Color)Application.Current!.Resources["PageBackground"] };
        send.Clicked += async (_, _) => await SendAsync();
        var composer = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10, Children = { _messageEntry, new ViewWithColumn(send, 1) } };
        Grid.SetRow(composer, 1);
        return new Grid { RowDefinitions = new RowDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) }, RowSpacing = 14, Children = { transcript, composer } };
    }

    private View CreateFeatureView(string id)
    {
        var title = NavigationItems.First(item => item.Id == id).Label;
        var output = new Editor { IsReadOnly = true, AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 180, BackgroundColor = (Color)Application.Current!.Resources["PanelBackground"], TextColor = Colors.White };
        var root = new Grid { RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) }, RowSpacing = 12 };
        root.Add(new Label { Text = title, FontSize = 24, FontAttributes = FontAttributes.Bold });
        var actionRow = new HorizontalStackLayout { Spacing = 8 };
        var load = new Button { Text = "刷新" };
        load.Clicked += async (_, _) => await LoadFeatureAsync(id, output);
        actionRow.Children.Add(load);
        if (id == "memory")
        {
            var memoryText = new Entry { Placeholder = "要记住的内容", WidthRequest = 280 };
            var add = new Button { Text = "记住" };
            add.Clicked += async (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(memoryText.Text)) return;
                var response = await _runtime.RequestAsync(new { type = "memory.add", item = new { text = memoryText.Text, category = "fact", importance = 0.6 } });
                output.Text = response.GetRawText(); memoryText.Text = string.Empty;
            };
            actionRow.Children.Add(memoryText); actionRow.Children.Add(add);
        }
        if (id == "goals" || id == "tasks")
        {
            var goalText = new Entry { Placeholder = id == "goals" ? "新目标" : "新任务", WidthRequest = 280 };
            var add = new Button { Text = "创建" };
            add.Clicked += async (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(goalText.Text)) return;
                object request = id == "goals"
                    ? new { type = "goal.set", objective = goalText.Text }
                    : new { type = "task.create", title = goalText.Text, detail = "由 MAUI 创建" };
                output.Text = (await _runtime.RequestAsync(request)).GetRawText(); goalText.Text = string.Empty;
            };
            actionRow.Children.Add(goalText); actionRow.Children.Add(add);
        }
        if (id == "settings")
        {
            var endpoint = new Entry { Placeholder = "模型 Endpoint", Text = Environment.GetEnvironmentVariable("PATTERN_ENDPOINT") ?? "https://api.openai.com/v1", WidthRequest = 280 };
            var model = new Entry { Placeholder = "模型", Text = Environment.GetEnvironmentVariable("PATTERN_MODEL") ?? "gpt-4o-mini", WidthRequest = 180 };
            var key = new Entry { Placeholder = "API Key", IsPassword = true, WidthRequest = 220 };
            var save = new Button { Text = "应用配置" };
            save.Clicked += async (_, _) =>
            {
                try
                {
                    await _runtime.ConfigureAsync(new { provider = "openai-compatible", endpoint = endpoint.Text, model = model.Text, apiKey = key.Text ?? string.Empty, persona = "You are Pattern, a helpful personal AI companion." });
                    output.Text = "配置已发送到 sidecar；API Key 不会写入普通日志。";
                }
                catch (Exception error) { output.Text = $"配置失败：{error.Message}"; }
            };
            actionRow.Children.Add(endpoint); actionRow.Children.Add(model); actionRow.Children.Add(key); actionRow.Children.Add(save);
        }
        root.Add(actionRow, 0, 1);
        root.Add(new ScrollView { Content = output }, 0, 2);
        _ = LoadFeatureAsync(id, output);
        return root;
    }

    private async Task LoadFeatureAsync(string id, Editor output)
    {
        try
        {
            var request = id switch
            {
                "memory" => (object)new { type = "memory.list", query = (string?)null, category = (string?)null },
                "goals" => new { type = "goal.list" },
                "tasks" => new { type = "task.list" },
                "proactive" => new { type = "proactive.list", limit = 100 },
                "workflows" => new { type = "workflow.list" },
                "mcp" => new { type = "mcp.list" },
                "channels" => new { type = "relay.status" },
                "settings" => new { type = "runtime.ping" },
                "project" => new { type = "projects.sync", projects = Array.Empty<object>() },
                "conversations" => new { type = "runtime.ping" },
                _ => new { type = "runtime.ping" }
            };
            output.Text = (await _runtime.RequestAsync(request)).GetRawText();
        }
        catch (Exception error) { output.Text = $"加载失败：{error.Message}"; }
    }

    private async Task SendAsync()
    {
        var text = _messageEntry?.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _messageEntry!.Text = string.Empty;
        var priorHistory = _history.ToArray();
        _activeAssistantText = string.Empty;
        _conversation += $"\n\n你：{text}\nPattern：";
        if (_conversationLabel is not null) _conversationLabel.Text = _conversation;
        try
        {
            await _runtime.SendChatAsync(text, priorHistory);
            _history.Add(new ChatTurn("user", text));
        }
        catch (Exception error)
        {
            _conversation += $"\n[发送失败：{error.Message}]";
            if (_conversationLabel is not null) _conversationLabel.Text = _conversation;
        }
    }

    private sealed class ViewWithColumn : ContentView
    {
        public ViewWithColumn(View content, int column) { Content = content; Grid.SetColumn(this, column); }
    }
}
