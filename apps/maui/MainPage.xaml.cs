using System.Text.Json;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Storage;
using Pattern.Maui.Services;

namespace Pattern.Maui;

public partial class MainPage : ContentPage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly (string Id, string Label)[] NavigationItems =
    [
        ("oobe", "首次设置"), ("chat", "对话"), ("project", "项目"), ("conversations", "会话"), ("memory", "记忆"),
        ("goals", "目标"), ("tasks", "任务"), ("proactive", "主动"), ("skills", "技能"),
        ("workflows", "工作流"), ("mcp", "工具"), ("channels", "通道"), ("models", "模型"),
        ("filewatch", "文件监控"), ("audit", "审计"), ("settings", "设置")
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
    private VerticalStackLayout? _conversationStack;
    private VerticalStackLayout? _recentsStack;
    private Entry? _messageEntry;
    private Button? _cancelButton;
    private Button? _attachButton;
    private string? _activeChatId;
    private string _activeSessionId = "default";
    private string _conversation = "欢迎回来。Pattern 现在由 .NET MAUI 驱动，Agent 通过本地 stdio sidecar 运行。";
    private string _activeAssistantText = string.Empty;
    private string _chatError = string.Empty;
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
            _messageEntry?.Focus();
        });
        _tray.ShowRequested += () => MainThread.BeginInvokeOnMainThread(() =>
        {
            ShowView("chat");
            _messageEntry?.Focus();
        });
        MacCatalystMenuService.QuickChatRequested += () => MainThread.BeginInvokeOnMainThread(() =>
        {
            ShowView("chat");
            _messageEntry?.Focus();
        });

        foreach (var (id, label) in NavigationItems)
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
                ImageSource = new FontImageSource
                {
                    Glyph = NavigationGlyph(id),
                    FontFamily = "Segoe MDL2 Assets",
                    Size = 17,
                    Color = (Color)Application.Current.Resources["TextFaint"],
                },
            };
            button.Clicked += (_, _) => ShowView(id);
            NavigationBar.Children.Add(button);
            _navigationButtons[id] = button;
        }

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
            _activeAssistantText += delta;
            _conversation += delta;
            RefreshConversationView();
        });
        _runtime.ChatDone += () => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!string.IsNullOrWhiteSpace(_activeAssistantText)) _history.Add(new ChatTurn("assistant", _activeAssistantText));
            _activeAssistantText = string.Empty;
            _chatError = string.Empty;
            _activeChatId = null;
            if (_cancelButton is not null) _cancelButton.IsEnabled = false;
            StatusLabel.Text = "运行时已连接";
            RefreshConversationView();
            SaveHistory();
        });
        _runtime.ChatCancelled += () => MainThread.BeginInvokeOnMainThread(() =>
        {
            _activeChatId = null;
            if (_cancelButton is not null) _cancelButton.IsEnabled = false;
            StatusLabel.Text = "回复已取消";
            RefreshConversationView();
        });
        _runtime.ChatError += error => MainThread.BeginInvokeOnMainThread(() =>
        {
            _conversation += $"\n[回复失败：{error}]";
            _chatError = error;
            _activeChatId = null;
            if (_cancelButton is not null) _cancelButton.IsEnabled = false;
            StatusLabel.Text = "回复失败，运行时仍保持连接";
            RefreshConversationView();
        });
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            ShowView("chat");
            if (!_instance.IsPrimary)
            {
                StatusLabel.Text = "Pattern 已在运行；为避免两个 sidecar 同时写入数据，本窗口不会启动第二个运行时。";
                return;
            }
            await ConnectAsync();
        };
    }

    private void LoadHistory()
    {
        try
        {
            _sessions.Clear();
            _history.Clear();
            _sessions.AddRange(_settings.LoadConversationSessions());
            if (_sessions.Count == 0)
            {
                var raw = _settings.LoadConversationHistory();
                var turns = string.IsNullOrWhiteSpace(raw) ? [] : JsonSerializer.Deserialize<List<ChatTurn>>(raw, JsonOptions) ?? [];
                _sessions.Add(new ConversationSession("default", "默认会话", turns.TakeLast(100).ToList(), false, DateTimeOffset.UtcNow));
            }
            var active = _sessions.FirstOrDefault(item => !item.Archived) ?? _sessions[0];
            ActivateSession(active.Id, persist: false);
        }
        catch { _history.Clear(); }
    }

    private void SaveHistory()
    {
        try
        {
            UpdateActiveSession();
            _settings.SaveConversationSessions(_sessions);
            _settings.SaveConversationHistory(JsonSerializer.Serialize(_history.TakeLast(100), JsonOptions));
        }
        catch { /* persistence must never break chat */ }
    }

    private void UpdateActiveSession()
    {
        var index = _sessions.FindIndex(item => item.Id == _activeSessionId);
        if (index < 0) return;
        _sessions[index] = _sessions[index] with { Messages = _history.TakeLast(100).ToList(), UpdatedAt = DateTimeOffset.UtcNow };
    }

    private void ActivateSession(string id, bool persist = true)
    {
        if (persist) UpdateActiveSession();
        var session = _sessions.FirstOrDefault(item => item.Id == id);
        if (session is null) return;
        _activeSessionId = session.Id;
        _history.Clear();
        _history.AddRange(session.Messages.TakeLast(100));
        _chatError = string.Empty;
        _conversation = _history.Count == 0
            ? "欢迎回来。Pattern 现在由 .NET MAUI 驱动，Agent 通过本地 stdio sidecar 运行。"
            : string.Join("\n\n", _history.Select(turn => turn.Role == "user" ? $"你：{turn.Content}" : $"Pattern：{turn.Content}"));
        RefreshConversationView();
        RefreshRecentsView();
        if (persist) SaveHistory();
    }

    private async Task ConnectAsync()
    {
        try
        {
            await _relay.InitializeAsync();
            if (OperatingSystem.IsAndroid())
            {
                var pairedFromLink = await _relay.ConsumePendingPairingAsync();
                var relayStatus = _relay.Status;
                StatusLabel.Text = relayStatus.Configured
                    ? (pairedFromLink ? "已通过配对链接配置中继 · 等待同步" : "移动端中继已配置 · 等待同步")
                    : "移动端中继模式 · 请在通道页配对";
                return;
            }
            await _runtime.StartAsync();
            var profile = _settings.LoadProfile();
            var key = await _settings.LoadApiKeyAsync();
            var relaySettings = _relay.CurrentSettings;
            await _runtime.ConfigureAsync(new
            {
                provider = profile.Provider,
                endpoint = profile.Endpoint,
                model = profile.Model,
                apiKey = string.IsNullOrWhiteSpace(key) ? Environment.GetEnvironmentVariable("PATTERN_API_KEY") ?? string.Empty : key,
                persona = profile.Persona,
                personaName = "Pattern",
                userName = profile.UserName,
                modelConnections = ModelConnectionPayload(),
                proactive = new { enabled = profile.ProactiveEnabled, paused = profile.ProactivePaused, bedtimeHour = profile.BedtimeHour },
                webdav = relaySettings.IsConfigured ? new { url = relaySettings.Url, username = relaySettings.Username, password = relaySettings.Password } : null,
                deviceId = relaySettings.DeviceId,
                channelKey = relaySettings.ChannelKey,
                bridgeUrl = _bridge.Url,
                bridgeToken = _bridge.Token,
            });
            StatusLabel.Text = "运行时已连接 · stdio";
        }
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
            if (button.ImageSource is FontImageSource icon)
            {
                icon.Color = button.TextColor;
            }
        }
    }

    private View CreateChatView()
    {
        _conversationStack = new VerticalStackLayout { Spacing = 22 };
        RefreshConversationView();
        var transcript = new ScrollView
        {
            Padding = new Thickness(42, 10),
            Content = new Border
            {
                MaximumWidthRequest = 820,
                HorizontalOptions = LayoutOptions.Center,
                StrokeThickness = 0,
                BackgroundColor = Colors.Transparent,
                Content = _conversationStack,
            },
        };
        _messageEntry = new Entry { Placeholder = "给 Pattern 发消息", ReturnType = ReturnType.Send };
        _messageEntry.Completed += async (_, _) => await SendAsync();
        var attach = new Button { Text = "附加" };
        _attachButton = attach;
        attach.Clicked += async (_, _) =>
        {
            try
            {
                var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "选择要附加给 Pattern 的文件" });
                if (file is not null && !string.IsNullOrWhiteSpace(file.FullPath) && !_attachedPaths.Contains(file.FullPath, StringComparer.OrdinalIgnoreCase))
                {
                    _attachedPaths.Add(file.FullPath);
                    attach.Text = $"附加({_attachedPaths.Count})";
                    StatusLabel.Text = $"已附加 {_attachedPaths.Count} 个路径";
                }
            }
            catch (Exception error) { StatusLabel.Text = $"选择附件失败：{error.Message}"; }
        };
        var send = new Button
        {
            Text = "发送",
            BackgroundColor = (Color)Application.Current!.Resources["Accent"],
            BorderColor = (Color)Application.Current!.Resources["Accent"],
            TextColor = (Color)Application.Current!.Resources["PageBackground"],
            FontAttributes = FontAttributes.Bold,
            BorderWidth = 1,
            CornerRadius = 6,
            Padding = new Thickness(12, 7),
        };
        send.Clicked += async (_, _) => await SendAsync();
        _cancelButton = new Button { Text = "停止", IsEnabled = false };
        _cancelButton.Clicked += async (_, _) =>
        {
            if (_activeChatId is null) return;
            await _runtime.CancelChatAsync(_activeChatId);
        };
        var composerGrid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 8 };
        composerGrid.Add(_messageEntry, 0, 0);
        composerGrid.Add(attach, 1, 0);
        composerGrid.Add(send, 2, 0);
        composerGrid.Add(_cancelButton, 3, 0);
        var composer = new Border
        {
            Margin = new Thickness(28, 8, 28, 16),
            Padding = new Thickness(12, 10),
            BackgroundColor = (Color)Application.Current!.Resources["PanelBackground"],
            Stroke = (Color)Application.Current!.Resources["LineStrong"],
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
            Content = composerGrid,
        };
        Grid.SetRow(composer, 2);
        var heading = new VerticalStackLayout
        {
            Padding = new Thickness(42, 26, 42, 4),
            Spacing = 3,
            Children =
            {
                new Label { Text = "对话", FontSize = 24, FontAttributes = FontAttributes.Bold },
                new Label { Text = "保持连续上下文，或用快捷键 Ctrl+Alt+P 随时叫醒 Pattern", TextColor = (Color)Application.Current!.Resources["TextMuted"], FontSize = 11 },
            },
        };
        Grid.SetRow(heading, 0);
        Grid.SetRow(transcript, 1);
        var main = new Grid
        {
            RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
            Children = { heading, transcript, composer },
        };
        var shell = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(new GridLength(220)), new(GridLength.Star) } };
        shell.Add(CreateRecentsSidebar(), 0, 0);
        shell.Add(main, 1, 0);
        return shell;
    }

    private View CreateRecentsSidebar()
    {
        _recentsStack = new VerticalStackLayout { Spacing = 4 };
        RefreshRecentsView();
        var newChat = new Button
        {
            Text = "＋ 新建对话",
            HorizontalOptions = LayoutOptions.Fill,
            BackgroundColor = (Color)Application.Current!.Resources["AccentWash"],
            BorderColor = (Color)Application.Current.Resources["AccentLine"],
            TextColor = (Color)Application.Current.Resources["Accent"],
            BorderWidth = 1,
            CornerRadius = 6,
            FontSize = 11,
            Padding = new Thickness(8, 6),
        };
        newChat.Clicked += (_, _) =>
        {
            var session = new ConversationSession(Guid.NewGuid().ToString("N"), "新对话", [], false, DateTimeOffset.UtcNow);
            _sessions.Add(session);
            ActivateSession(session.Id);
            ShowView("chat");
            StatusLabel.Text = "已新建对话";
        };
        var root = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new Label { Text = "最近内容", TextColor = (Color)Application.Current.Resources["TextFaint"], FontSize = 10, FontAttributes = FontAttributes.Bold },
                newChat,
                new Label { Text = "最近聊天", TextColor = (Color)Application.Current.Resources["TextMuted"], FontSize = 11, Margin = new Thickness(0, 8, 0, 0) },
                _recentsStack,
                new Label { Text = "主动收件箱", TextColor = (Color)Application.Current.Resources["TextMuted"], FontSize = 11, Margin = new Thickness(0, 10, 0, 0) },
                new Label { Text = "没有待处理的主动消息", TextColor = (Color)Application.Current.Resources["TextFaint"], FontSize = 10 },
            },
        };
        return new Border
        {
            Padding = new Thickness(16, 24, 12, 18),
            BackgroundColor = (Color)Application.Current.Resources["SurfaceBackground"],
            Stroke = (Color)Application.Current.Resources["Line"],
            StrokeThickness = 1,
            Content = new ScrollView { Content = root },
        };
    }

    private void RefreshRecentsView()
    {
        if (_recentsStack is null) return;
        _recentsStack.Children.Clear();
        var recent = _sessions.Where(item => !item.Archived).OrderByDescending(item => item.UpdatedAt).Take(12).ToArray();
        if (recent.Length == 0)
        {
            _recentsStack.Children.Add(new Label { Text = "还没有全局对话", TextColor = (Color)Application.Current!.Resources["TextFaint"], FontSize = 10, Padding = new Thickness(4, 8) });
            return;
        }
        foreach (var session in recent)
        {
            var item = session;
            var button = new Button
            {
                Text = item.Title,
                HorizontalOptions = LayoutOptions.Fill,
                BackgroundColor = item.Id == _activeSessionId ? (Color)Application.Current!.Resources["AccentWash"] : Colors.Transparent,
                BorderColor = item.Id == _activeSessionId ? (Color)Application.Current!.Resources["AccentLine"] : Colors.Transparent,
                TextColor = item.Id == _activeSessionId ? (Color)Application.Current!.Resources["Accent"] : (Color)Application.Current!.Resources["TextMuted"],
                BorderWidth = 1,
                CornerRadius = 6,
                FontSize = 11,
                Padding = new Thickness(8, 7),
            };
            button.Clicked += (_, _) =>
            {
                ActivateSession(item.Id);
                ShowView("chat");
            };
            _recentsStack.Children.Add(button);
        }
    }

    private void RefreshConversationView()
    {
        if (_conversationStack is null) return;
        _conversationStack.Children.Clear();
        if (_history.Count == 0 && string.IsNullOrWhiteSpace(_activeAssistantText))
        {
            _conversationStack.Children.Add(CreateMessageBubble("Pattern", _conversation, isUser: false));
        }
        else
        {
            foreach (var turn in _history)
            {
                _conversationStack.Children.Add(CreateMessageBubble(turn.Role == "user" ? "你" : "Pattern", turn.Content, turn.Role == "user"));
            }
            if (!string.IsNullOrWhiteSpace(_activeAssistantText))
            {
                _conversationStack.Children.Add(CreateMessageBubble("Pattern", _activeAssistantText, isUser: false));
            }
        }
        if (_activeChatId is not null)
        {
            _conversationStack.Children.Add(new Label
            {
                Text = "Pattern 正在思考…",
                TextColor = (Color)Application.Current!.Resources["TextMuted"],
                FontSize = 11,
                Margin = new Thickness(15, -12, 0, 0),
            });
        }
        if (!string.IsNullOrWhiteSpace(_chatError))
        {
            _conversationStack.Children.Add(new Border
            {
                Padding = new Thickness(12, 9),
                BackgroundColor = (Color)Application.Current!.Resources["AccentWash"],
                Stroke = (Color)Application.Current!.Resources["AccentLine"],
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) },
                Content = new Label { Text = $"回复失败：{_chatError}", TextColor = (Color)Application.Current!.Resources["Accent"], FontSize = 11 },
            });
        }
    }

    private static Border CreateMessageBubble(string speaker, string text, bool isUser)
    {
        var meta = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label { Text = speaker, FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = isUser ? (Color)Application.Current!.Resources["TextPrimary"] : (Color)Application.Current!.Resources["Accent"] },
                new Label { Text = DateTime.Now.ToString("HH:mm"), FontFamily = "Consolas", FontSize = 10, TextColor = (Color)Application.Current!.Resources["TextFaint"] },
            },
        };
        var body = new Label
        {
            Text = text,
            FontSize = 14,
            LineHeight = 1.8,
            LineBreakMode = LineBreakMode.WordWrap,
            TextColor = (Color)Application.Current!.Resources["TextPrimary"],
        };
        var copy = new VerticalStackLayout { Spacing = 5, Children = { meta, body } };
        return new Border
        {
            MaximumWidthRequest = 620,
            HorizontalOptions = isUser ? LayoutOptions.End : LayoutOptions.Start,
            Padding = isUser ? new Thickness(14, 10) : new Thickness(0),
            BackgroundColor = isUser ? (Color)Application.Current!.Resources["PanelBackground"] : Colors.Transparent,
            Stroke = isUser ? (Color)Application.Current!.Resources["Line"] : Colors.Transparent,
            StrokeThickness = isUser ? 1 : 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8, 8, 2, 8) },
            Content = copy,
        };
    }

    private static string NavigationGlyph(string id) => id switch
    {
        "oobe" => "\uE713",
        "chat" => "\uE8BD",
        "project" => "\uE8B7",
        "conversations" => "\uE8A5",
        "memory" => "\uE946",
        "goals" => "\uE944",
        "tasks" => "\uE9D5",
        "proactive" => "\uE7F4",
        "skills" => "\uE8F1",
        "workflows" => "\uE9D2",
        "mcp" => "\uE90F",
        "channels" => "\uE715",
        "models" => "\uE950",
        "filewatch" => "\uE8B7",
        "audit" => "\uE9D9",
        "settings" => "\uE713",
        _ => "\uE7C3",
    };

    private View CreateFeatureView(string id) => id switch
    {
        "oobe" => CreateOobeView(),
        "project" => CreateProjectView(),
        "conversations" => CreateConversationsView(),
        "memory" => CreateMemoryView(),
        "goals" => CreateGoalsView(),
        "tasks" => CreateTasksView(),
        "proactive" => CreateProactiveView(),
        "skills" => CreateSkillsView(),
        "workflows" => CreateWorkflowsView(),
        "mcp" => CreateMcpView(),
        "channels" => CreateChannelsView(),
        "models" => CreateModelsView(),
        "filewatch" => CreateFileWatchView(),
        "audit" => CreateAuditView(),
        "settings" => CreateSettingsView(),
        _ => CreateDataView(id, new { type = "runtime.ping" }),
    };

    private View CreateOobeView()
    {
        var output = OutputEditor();
        var profile = _settings.LoadProfile();
        var user = new Entry { Placeholder = "你的名字", Text = profile.UserName, WidthRequest = 180 };
        var endpoint = new Entry { Placeholder = "模型 Endpoint", Text = profile.Endpoint, WidthRequest = 300 };
        var model = new Entry { Placeholder = "模型", Text = profile.Model, WidthRequest = 180 };
        var key = new Entry { Placeholder = "API Key（安全保存）", IsPassword = true, WidthRequest = 240 };
        var persona = profile.Persona;
        var personaLabel = new Label { Text = "人格：默认陪伴", VerticalTextAlignment = TextAlignment.Center };
        var gentle = new Button { Text = "温柔陪伴" };
        gentle.Clicked += (_, _) => { persona = "You are Pattern, a warm and patient personal AI companion. Be concise but empathetic."; personaLabel.Text = "人格：温柔陪伴"; };
        var focused = new Button { Text = "专注执行" };
        focused.Clicked += (_, _) => { persona = "You are Pattern, a precise personal AI companion. Prefer actionable plans and clear next steps."; personaLabel.Text = "人格：专注执行"; };
        var concise = new Button { Text = "简洁回答" };
        concise.Clicked += (_, _) => { persona = "You are Pattern, a concise personal AI companion. Answer directly and avoid unnecessary prose."; personaLabel.Text = "人格：简洁回答"; };
        var save = new Button { Text = "完成设置并进入对话", BackgroundColor = (Color)Application.Current!.Resources["Accent"], TextColor = (Color)Application.Current!.Resources["PageBackground"] };
        save.Clicked += async (_, _) =>
        {
            var next = new RuntimeProfile("openai-compatible", endpoint.Text ?? string.Empty, model.Text ?? string.Empty, persona, user.Text ?? "User", profile.ProactiveEnabled, profile.ProactivePaused, profile.BedtimeHour);
            _settings.SaveProfile(next);
            if (!string.IsNullOrWhiteSpace(key.Text)) await _settings.SaveApiKeyAsync(key.Text.Trim());
            try
            {
                await _runtime.ConfigureAsync(new { provider = next.Provider, endpoint = next.Endpoint, model = next.Model, apiKey = string.IsNullOrWhiteSpace(key.Text) ? await _settings.LoadApiKeyAsync() : key.Text.Trim(), persona = next.Persona, personaName = "Pattern", userName = next.UserName, modelConnections = ModelConnectionPayload(), proactive = new { enabled = next.ProactiveEnabled, paused = next.ProactivePaused, bedtimeHour = next.BedtimeHour } });
                output.Text = "设置完成。可以开始对话；API Key 已交给系统安全存储。";
                ShowView("chat");
            }
            catch (Exception error) { output.Text = $"设置保存成功，但 sidecar 尚未连接：{error.Message}"; }
        };
        var actions = new VerticalStackLayout { Spacing = 8, Children = { new Label { Text = "欢迎使用 Pattern", FontSize = 18, FontAttributes = FontAttributes.Bold }, new Label { Text = "先选择你的使用方式，再填入模型配置。之后可以在设置页修改。" }, user, new HorizontalStackLayout { Spacing = 8, Children = { gentle, focused, concise, personaLabel } }, endpoint, model, key, save } };
        return FeatureRoot("首次设置", actions, output);
    }

    private static Editor OutputEditor() => new()
    {
        IsReadOnly = true,
        AutoSize = EditorAutoSizeOption.TextChanges,
        MinimumHeightRequest = 180,
        BackgroundColor = (Color)Application.Current!.Resources["PanelBackground"],
        TextColor = (Color)Application.Current!.Resources["TextPrimary"],
        FontFamily = DeviceInfo.Platform == DevicePlatform.WinUI ? "Consolas" : null,
    };

    private static string Format(JsonElement value)
    {
        try
        {
            using var doc = JsonDocument.Parse(value.GetRawText());
            return JsonSerializer.Serialize(doc.RootElement, JsonOptions);
        }
        catch { return value.GetRawText(); }
    }

    private object[] ModelConnectionPayload() => _settings.LoadModelProfiles()
        .Select(item => (object)new { id = item.Id, name = item.Name, provider = item.Provider, endpoint = item.Endpoint, models = item.Models, enabled = true })
        .ToArray();

    private async Task RequestToEditorAsync(object request, Editor output, string? successStatus = null)
    {
        try
        {
            StatusLabel.Text = "正在请求 sidecar…";
            var response = await _runtime.RequestAsync(request);
            output.Text = Format(response);
            if (!string.IsNullOrWhiteSpace(successStatus)) StatusLabel.Text = successStatus;
        }
        catch (Exception error)
        {
            output.Text = $"请求失败\n\n{error.Message}";
            StatusLabel.Text = "请求失败（可重连）";
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!OperatingSystem.IsAndroid()) return;
        try
        {
            await _relay.InitializeAsync();
            AppendRelayMessages(_relay.DrainInbox());
            if (Preferences.Default.Get("pattern.pending.open.chat", false))
            {
                Preferences.Default.Remove("pattern.pending.open.chat");
                ShowView("chat");
                StatusLabel.Text = "已打开中继消息会话";
            }
        }
        catch (Exception error) { StatusLabel.Text = $"中继状态读取失败：{error.Message}"; }
    }

    private void AppendRelayMessages(IEnumerable<RelayEnvelope> messages)
    {
        var incoming = messages.Where(item => item.Type is "chat" or "proactive" or "task").ToArray();
        if (incoming.Length == 0) return;
        foreach (var item in incoming)
        {
            var body = item.Type == "task" ? FormatRelayTask(item.Body) : item.Body;
            _conversation += $"\n\n中继消息：{body}";
            _history.Add(new ChatTurn("assistant", body));
        }
        RefreshConversationView();
        SaveHistory();
        StatusLabel.Text = $"收到 {incoming.Length} 条中继消息";
    }

    private static string FormatRelayTask(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var task = document.RootElement.TryGetProperty("task", out var nested) ? nested : document.RootElement;
            var title = task.TryGetProperty("title", out var titleValue) ? titleValue.GetString() : "远程任务";
            var status = task.TryGetProperty("status", out var statusValue) ? statusValue.GetString() : "updated";
            return $"远程任务：{title}\n状态：{status}";
        }
        catch { return $"远程任务：{body}"; }
    }

    private View CreateDataView(string title, object request)
    {
        var output = OutputEditor();
        var refresh = new Button { Text = "刷新" };
        refresh.Clicked += async (_, _) => await RequestToEditorAsync(request, output);
        var root = FeatureRoot(title, new HorizontalStackLayout { Children = { refresh } }, output);
        _ = RequestToEditorAsync(request, output);
        return root;
    }

    private static Grid FeatureRoot(string title, View actions, Editor output)
    {
        var root = new Grid
        {
            Padding = new Thickness(42, 26, 42, 30),
            RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) },
            RowSpacing = 14,
            BackgroundColor = (Color)Application.Current!.Resources["SurfaceBackground"],
        };
        root.Add(new VerticalStackLayout
        {
            Spacing = 3,
            Children =
            {
                new Label { Text = "WORKSPACE", TextColor = (Color)Application.Current.Resources["Accent"], FontSize = 10, FontAttributes = FontAttributes.Bold },
                new Label { Text = title, FontSize = 24, FontAttributes = FontAttributes.Bold },
            },
        }, 0, 0);
        root.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Vertical,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = actions,
        }, 0, 1);
        root.Add(new Border
        {
            BackgroundColor = (Color)Application.Current.Resources["PanelBackground"],
            Stroke = (Color)Application.Current.Resources["Line"],
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
            Content = new ScrollView { Content = output },
        }, 0, 2);
        return root;
    }

    private View CreateProjectView()
    {
        var output = OutputEditor();
        var preview = OutputEditor();
        preview.Placeholder = "选择文件后显示文本预览（最多 120 KB）";
        var path = new Entry { Placeholder = "工作区绝对路径" };
        var name = new Entry { Placeholder = "项目名" };
        var tree = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            EmptyView = new Label { Text = "还没有文件树，请先输入目录并点击“列出文件”。", Padding = 12 },
            ItemTemplate = new DataTemplate(() =>
            {
                var label = new Label { FontSize = 13, Padding = new Thickness(8, 6) };
                label.SetBinding(Label.TextProperty, nameof(WorkspaceRow.Display));
                return new Border
                {
                    StrokeThickness = 0,
                    BackgroundColor = (Color)Application.Current!.Resources["PanelBackground"],
                    Content = label,
                    Margin = new Thickness(0, 1),
                };
            }),
        };
        var list = new Button { Text = "列出文件" };
        list.Clicked += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(path.Text)) return;
            try
            {
                var response = await _runtime.RequestAsync(new { type = "workspace.list", root = path.Text.Trim(), depth = 2 });
                var rows = response.TryGetProperty("nodes", out var nodes) ? FlattenWorkspaceNodes(nodes).ToArray() : Array.Empty<WorkspaceRow>();
                tree.ItemsSource = rows;
                output.Text = $"已读取 {rows.Length} 个节点\n\n目录与文件均经过 sidecar workspace policy 校验。";
                StatusLabel.Text = "项目文件树已加载";
            }
            catch (Exception error) { output.Text = $"读取项目目录失败：{error.Message}"; }
        };
        tree.SelectionChanged += async (_, args) =>
        {
            var row = args.CurrentSelection.FirstOrDefault() as WorkspaceRow;
            if (row is null || row.Kind != "file") return;
            try
            {
                var response = await _runtime.RequestAsync(new { type = "workspace.read", path = row.Path, maxBytes = 120000 });
                preview.Text = response.TryGetProperty("content", out var content) ? content.GetString() ?? string.Empty : Format(response);
                if (response.TryGetProperty("truncated", out var truncated) && truncated.GetBoolean()) preview.Text += "\n\n[内容已截断]";
            }
            catch (Exception error) { preview.Text = $"文件预览失败：{error.Message}"; }
            finally { tree.SelectedItem = null; }
        };
        var sync = new Button { Text = "同步项目" };
        sync.Clicked += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(path.Text)) return;
            await RequestToEditorAsync(new { type = "projects.sync", projects = new[] { new { id = "default", name = string.IsNullOrWhiteSpace(name.Text) ? "工作区" : name.Text, path = path.Text } } }, output, "项目已同步");
        };
        var diff = new Button { Text = "查看 Git Diff" };
        diff.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "workspace.diff", root = path.Text }, output);
        var worktree = new Button { Text = "创建 Worktree" };
        worktree.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "workspace.worktree.create", root = path.Text, name = name.Text }, output);
        var projectControls = new VerticalStackLayout
        {
            Spacing = 9,
            Children =
            {
                new Label { Text = "项目", TextColor = (Color)Application.Current!.Resources["Accent"], FontSize = 10, FontAttributes = FontAttributes.Bold },
                new Label { Text = "工作区上下文", TextColor = (Color)Application.Current.Resources["TextMuted"], FontSize = 11 },
                name,
                path,
                list,
                sync,
                new BoxView { HeightRequest = 1, Color = (Color)Application.Current.Resources["Line"], Margin = new Thickness(0, 5) },
                new Label { Text = "Git 操作", TextColor = (Color)Application.Current.Resources["TextMuted"], FontSize = 11 },
                diff,
                worktree,
            },
        };
        var sidebar = new Border
        {
            Padding = new Thickness(14),
            BackgroundColor = (Color)Application.Current.Resources["PanelBackground"],
            Stroke = (Color)Application.Current.Resources["Line"],
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
            Content = new ScrollView { Content = projectControls },
        };
        var treePanel = new Border
        {
            Padding = new Thickness(12),
            BackgroundColor = (Color)Application.Current.Resources["PanelBackground"],
            Stroke = (Color)Application.Current.Resources["Line"],
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
            Content = new Grid
            {
                RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Star) },
                RowSpacing = 9,
                Children =
                {
                    new Label { Text = "文件树", TextColor = (Color)Application.Current.Resources["TextMuted"], FontSize = 11 },
                    tree,
                },
            },
        };
        Grid.SetRow(tree, 1);
        var previewPanel = new Border
        {
            Padding = new Thickness(12),
            BackgroundColor = (Color)Application.Current.Resources["PanelBackground"],
            Stroke = (Color)Application.Current.Resources["Line"],
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
            Content = new Grid
            {
                RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Star) },
                RowSpacing = 9,
                Children =
                {
                    new Label { Text = "文件预览", TextColor = (Color)Application.Current.Resources["TextMuted"], FontSize = 11 },
                    preview,
                },
            },
        };
        Grid.SetRow(preview, 1);
        var workspace = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(new GridLength(220)), new(new GridLength(260)), new(GridLength.Star) },
            RowDefinitions = new RowDefinitionCollection { new(GridLength.Star), new(new GridLength(130)) },
            ColumnSpacing = 10,
            RowSpacing = 10,
        };
        workspace.Add(sidebar, 0, 0);
        workspace.Add(treePanel, 1, 0);
        workspace.Add(previewPanel, 2, 0);
        var outputPanel = new Border
        {
            Padding = new Thickness(10, 6),
            BackgroundColor = (Color)Application.Current.Resources["PanelBackground"],
            Stroke = (Color)Application.Current.Resources["Line"],
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
            Content = output,
        };
        Grid.SetColumnSpan(outputPanel, 3);
        Grid.SetRow(outputPanel, 1);
        workspace.Add(outputPanel);
        var root = new Grid
        {
            Padding = new Thickness(42, 26, 42, 30),
            RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Star) },
            RowSpacing = 14,
            BackgroundColor = (Color)Application.Current.Resources["SurfaceBackground"],
            Children =
            {
                new VerticalStackLayout { Spacing = 3, Children = { new Label { Text = "PROJECT WORKSPACE", TextColor = (Color)Application.Current.Resources["Accent"], FontSize = 10, FontAttributes = FontAttributes.Bold }, new Label { Text = "项目工作区", FontSize = 24, FontAttributes = FontAttributes.Bold } } },
                workspace,
            },
        };
        Grid.SetRow(workspace, 1);
        return root;
    }

    private static IEnumerable<WorkspaceRow> FlattenWorkspaceNodes(JsonElement nodes, int level = 0)
    {
        if (nodes.ValueKind != JsonValueKind.Array) yield break;
        foreach (var node in nodes.EnumerateArray())
        {
            var name = node.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "" : "";
            var path = node.TryGetProperty("path", out var pathValue) ? pathValue.GetString() ?? "" : "";
            var kind = node.TryGetProperty("kind", out var kindValue) ? kindValue.GetString() ?? "file" : "file";
            var size = node.TryGetProperty("size", out var sizeValue) && sizeValue.ValueKind == JsonValueKind.Number ? $" · {sizeValue.GetInt64():n0} B" : "";
            yield return new WorkspaceRow(kind, path, $"{new string(' ', Math.Min(12, level) * 2)}{(kind == "directory" ? "▸ " : "  ")}{name}{size}");
            if (node.TryGetProperty("children", out var children))
                foreach (var child in FlattenWorkspaceNodes(children, level + 1)) yield return child;
        }
    }

    private View CreateConversationsView()
    {
        var output = OutputEditor();
        var picker = new Picker { Title = "选择会话", WidthRequest = 240 };
        void RefreshPicker()
        {
            picker.ItemsSource = null;
            picker.ItemsSource = _sessions.Where(item => !item.Archived).OrderByDescending(item => item.UpdatedAt).Select(item => $"{item.Title} · {item.Id[..Math.Min(8, item.Id.Length)]}").ToList();
            var active = _sessions.Where(item => !item.Archived).OrderByDescending(item => item.UpdatedAt).ToList().FindIndex(item => item.Id == _activeSessionId);
            if (active >= 0) picker.SelectedIndex = active;
        }
        picker.SelectedIndexChanged += (_, _) =>
        {
            var active = _sessions.Where(item => !item.Archived).OrderByDescending(item => item.UpdatedAt).ToList();
            if (picker.SelectedIndex >= 0 && picker.SelectedIndex < active.Count) ActivateSession(active[picker.SelectedIndex].Id);
        };
        RefreshPicker();
        var refresh = new Button { Text = "查看当前历史" };
        refresh.Clicked += (_, _) => output.Text = _history.Count == 0 ? "暂无本地消息" : string.Join("\n\n", _history.Select((item, index) => $"{index + 1}. {item.Role}: {item.Content}"));
        var create = new Button { Text = "新建会话" };
        create.Clicked += async (_, _) =>
        {
            var title = await DisplayPromptAsync("新建会话", "输入会话名称") ?? "";
            if (string.IsNullOrWhiteSpace(title)) return;
            UpdateActiveSession();
            var session = new ConversationSession(Guid.NewGuid().ToString("N"), title.Trim(), [], false, DateTimeOffset.UtcNow);
            _sessions.Add(session);
            ActivateSession(session.Id);
            RefreshPicker();
            output.Text = $"已创建会话：{title.Trim()}";
        };
        var rename = new Button { Text = "重命名" };
        rename.Clicked += async (_, _) =>
        {
            var session = _sessions.FirstOrDefault(item => item.Id == _activeSessionId);
            if (session is null) return;
            var title = await DisplayPromptAsync("重命名会话", "输入新名称", initialValue: session.Title);
            if (string.IsNullOrWhiteSpace(title)) return;
            _sessions[_sessions.IndexOf(session)] = session with { Title = title.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
            SaveHistory(); RefreshPicker(); output.Text = "会话名称已更新";
        };
        var archive = new Button { Text = "归档当前" };
        archive.Clicked += (_, _) =>
        {
            var index = _sessions.FindIndex(item => item.Id == _activeSessionId);
            if (index < 0 || _sessions.Count(item => !item.Archived) <= 1) return;
            _sessions[index] = _sessions[index] with { Archived = true, UpdatedAt = DateTimeOffset.UtcNow };
            var next = _sessions.First(item => !item.Archived);
            ActivateSession(next.Id); RefreshPicker(); output.Text = "会话已归档";
        };
        var delete = new Button { Text = "删除当前" };
        delete.Clicked += (_, _) =>
        {
            var index = _sessions.FindIndex(item => item.Id == _activeSessionId);
            if (index < 0 || _sessions.Count <= 1) return;
            _sessions.RemoveAt(index);
            var next = _sessions.FirstOrDefault(item => !item.Archived) ?? _sessions[0];
            ActivateSession(next.Id); RefreshPicker(); output.Text = "会话已删除";
        };
        var clear = new Button { Text = "清空当前消息" };
        clear.Clicked += (_, _) =>
        {
            _history.Clear();
            _conversation = "会话已清空。";
            RefreshConversationView();
            SaveHistory();
            output.Text = "已清空本地会话历史";
        };
        var plan = new Button { Text = "读取当前计划" };
        plan.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "session_plan.get", conversationId = _activeSessionId }, output);
        return FeatureRoot("会话管理", new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new HorizontalStackLayout { Spacing = 8, Children = { picker, refresh, create, rename, archive, delete, clear, plan } } }, output);
    }

    private View CreateMemoryView()
    {
        var output = OutputEditor();
        var query = new Entry { Placeholder = "搜索记忆", WidthRequest = 220 };
        var category = new Entry { Placeholder = "分类（可选）", WidthRequest = 140 };
        var text = new Entry { Placeholder = "新增记忆内容", WidthRequest = 260 };
        var memoryGrid = new FlexLayout
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlignItems.Stretch,
            AlignContent = FlexAlignContent.Start,
            JustifyContent = FlexJustify.Start,
        };
        async Task RefreshMemoryCardsAsync()
        {
            try
            {
                StatusLabel.Text = "正在读取记忆…";
                var response = await _runtime.RequestAsync(new
                {
                    type = "memory.list",
                    query = string.IsNullOrWhiteSpace(query.Text) ? null : query.Text,
                    category = string.IsNullOrWhiteSpace(category.Text) ? null : category.Text,
                });
                memoryGrid.Children.Clear();
                if (!response.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
                {
                    memoryGrid.Children.Add(new Label { Text = "还没有记忆，先保存一件值得长期记住的事。", TextColor = (Color)Application.Current!.Resources["TextMuted"], FontSize = 12, Margin = new Thickness(4, 18) });
                    output.Text = Format(response);
                    StatusLabel.Text = "记忆列表为空";
                    return;
                }
                foreach (var item in items.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? string.Empty : string.Empty;
                    var rawCategory = item.TryGetProperty("category", out var categoryValue) ? categoryValue.GetString() ?? "fact" : "fact";
                    var label = rawCategory switch { "preference" => "偏好", "event" => "事件", "feedback" => "反馈", _ => "事实" };
                    var value = item.TryGetProperty("text", out var textValue) ? textValue.GetString() ?? string.Empty : string.Empty;
                    var importance = item.TryGetProperty("importance", out var importanceValue) && importanceValue.ValueKind == JsonValueKind.Number ? importanceValue.GetDouble() : 0.5;
                    var expired = item.TryGetProperty("expired", out var expiredValue) && expiredValue.ValueKind == JsonValueKind.True;
                    var meta = item.TryGetProperty("meta", out var metaValue) ? metaValue.GetString() ?? "本地记忆" : "本地记忆";
                    memoryGrid.Children.Add(CreateMemoryCard(id, label, value, importance, expired, meta, output, RefreshMemoryCardsAsync));
                }
                output.Text = $"已加载 {items.GetArrayLength()} 条记忆。\n\n数据由 sidecar memory engine 提供。";
                StatusLabel.Text = $"已加载 {items.GetArrayLength()} 条记忆";
            }
            catch (Exception error)
            {
                memoryGrid.Children.Clear();
                memoryGrid.Children.Add(new Label { Text = "记忆读取失败，请检查运行时连接。", TextColor = (Color)Application.Current!.Resources["Danger"], FontSize = 12, Margin = new Thickness(4, 18) });
                output.Text = $"记忆读取失败：{error.Message}";
                StatusLabel.Text = "记忆读取失败";
            }
        }
        var refresh = new Button { Text = "搜索/刷新" };
        refresh.Clicked += async (_, _) => await RefreshMemoryCardsAsync();
        var add = new Button { Text = "记住" };
        add.Clicked += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(text.Text)) return;
            await RequestToEditorAsync(new { type = "memory.add", item = new { text = text.Text, category = string.IsNullOrWhiteSpace(category.Text) ? "fact" : category.Text, importance = 0.6 } }, output, "记忆已写入");
            text.Text = string.Empty;
            await RefreshMemoryCardsAsync();
        };
        var proposals = new Button { Text = "提案" };
        proposals.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "memory.propose.list" }, output);
        var accept = new Button { Text = "接受提案（按 ID）" };
        accept.Clicked += async (_, _) => await ControlByPromptAsync("记忆提案 ID", id => new { type = "memory.propose.accept", proposalId = id }, output);
        var reject = new Button { Text = "拒绝提案（按 ID）" };
        reject.Clicked += async (_, _) => await ControlByPromptAsync("记忆提案 ID", id => new { type = "memory.propose.reject", proposalId = id }, output);
        var expire = new Button { Text = "过期记忆（按 ID）" };
        expire.Clicked += async (_, _) => await ControlByPromptAsync("记忆 ID", id => new { type = "memory.expire", memoryId = id }, output);
        var consolidate = new Button { Text = "固化" };
        consolidate.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "memory.consolidate" }, output);
        var toolbar = new FlexLayout { Direction = FlexDirection.Row, Wrap = FlexWrap.Wrap, AlignItems = FlexAlignItems.Center, JustifyContent = FlexJustify.Start, Children = { query, category, refresh, text, add, proposals, accept, reject, expire, consolidate } };
        var content = new VerticalStackLayout { Spacing = 12, Children = { toolbar, memoryGrid } };
        _ = RefreshMemoryCardsAsync();
        return FeatureRoot("记忆", content, output);
    }

    private Border CreateMemoryCard(string id, string category, string text, double importance, bool expired, string meta, Editor output, Func<Task> refresh)
    {
        var stars = new string('★', Math.Clamp((int)Math.Round(importance > 1 ? importance : importance * 3), 1, 3));
        var expire = new Button { Text = "过期", FontSize = 10, Padding = new Thickness(7, 4), TextColor = (Color)Application.Current!.Resources["TextMuted"], BorderColor = (Color)Application.Current.Resources["Line"] };
        expire.Clicked += async (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id)) return;
                await _runtime.RequestAsync(new { type = "memory.expire", memoryId = id });
                output.Text = $"已将记忆标记为过期：{id}";
                await refresh();
            }
            catch (Exception error) { output.Text = $"过期记忆失败：{error.Message}"; }
        };
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 6 };
        footer.Add(new Label { Text = meta, TextColor = (Color)Application.Current.Resources["TextFaint"], FontFamily = "Consolas", FontSize = 10, LineBreakMode = LineBreakMode.TailTruncation }, 0, 0);
        footer.Add(expire, 1, 0);
        return new Border
        {
            WidthRequest = 250,
            MinimumHeightRequest = 160,
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(14),
            BackgroundColor = (Color)Application.Current.Resources["PanelBackground"],
            Stroke = expired ? (Color)Application.Current.Resources["Line"] : (Color)Application.Current.Resources["LineStrong"],
            StrokeThickness = 1,
            Opacity = expired ? 0.55 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7) },
            Content = new VerticalStackLayout
            {
                Spacing = 9,
                Children =
                {
                    new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) }, Children = { new Label { Text = category, TextColor = (Color)Application.Current.Resources["Accent"], FontSize = 10, FontAttributes = FontAttributes.Bold }, new Label { Text = stars, TextColor = (Color)Application.Current.Resources["Accent"], FontSize = 10 } } },
                    new Label { Text = text, TextColor = (Color)Application.Current.Resources["TextPrimary"], FontSize = 13, LineHeight = 1.55, LineBreakMode = LineBreakMode.WordWrap, MaxLines = 4 },
                    new BoxView { HeightRequest = 1, Color = (Color)Application.Current.Resources["Line"] },
                    footer,
                },
            },
        };
    }

    private View CreateGoalsView()
    {
        var output = OutputEditor();
        var objective = new Entry { Placeholder = "新目标", WidthRequest = 320 };
        var create = new Button { Text = "创建目标" };
        create.Clicked += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(objective.Text)) return;
            await RequestToEditorAsync(new { type = "goal.set", objective = objective.Text }, output, "目标已保存");
            objective.Text = string.Empty;
        };
        var list = new Button { Text = "刷新目标" };
        list.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "goal.list" }, output);
        var pause = new Button { Text = "暂停目标（按 ID）" };
        pause.Clicked += async (_, _) => await ControlByPromptAsync("目标 ID", id => new { type = "goal.control", goalId = id, action = "pause" }, output);
        var resume = new Button { Text = "恢复目标（按 ID）" };
        resume.Clicked += async (_, _) => await ControlByPromptAsync("目标 ID", id => new { type = "goal.control", goalId = id, action = "resume" }, output);
        var complete = new Button { Text = "完成目标（按 ID）" };
        complete.Clicked += async (_, _) => await ControlByPromptAsync("目标 ID", id => new { type = "goal.control", goalId = id, action = "complete" }, output);
        var clear = new Button { Text = "清除目标（按 ID）" };
        clear.Clicked += async (_, _) => await ControlByPromptAsync("目标 ID", id => new { type = "goal.control", goalId = id, action = "clear" }, output);
        return FeatureRoot("目标", new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new HorizontalStackLayout { Spacing = 8, Children = { objective, create, list, pause, resume, complete, clear } } }, output);
    }

    private View CreateTasksView()
    {
        var output = OutputEditor();
        var title = new Entry { Placeholder = "新任务", WidthRequest = 280 };
        var detail = new Entry { Placeholder = "任务详情", WidthRequest = 280 };
        var create = new Button { Text = "创建任务" };
        create.Clicked += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(title.Text)) return;
            if (OperatingSystem.IsAndroid())
            {
                await _relay.PublishTaskAsync(title.Text.Trim(), detail.Text ?? string.Empty);
                output.Text = _relay.Status.Online ? "远程任务已发送到桌面 sidecar。" : "桌面当前离线，任务已进入 relay outbox。";
                title.Text = string.Empty;
                detail.Text = string.Empty;
                return;
            }
            await RequestToEditorAsync(new { type = "task.create", title = title.Text, detail = detail.Text ?? string.Empty }, output, "任务已创建");
            title.Text = string.Empty;
            detail.Text = string.Empty;
        };
        var list = new Button { Text = "刷新任务" };
        list.Clicked += async (_, _) =>
        {
            if (!OperatingSystem.IsAndroid()) { await RequestToEditorAsync(new { type = "task.list" }, output); return; }
            await _relay.InitializeAsync();
            var incoming = _relay.DrainInbox().Where(item => item.Type == "task").ToArray();
            output.Text = JsonSerializer.Serialize(new { status = _relay.Status, incoming = incoming.Select(item => FormatRelayTask(item.Body)) }, JsonOptions);
            AppendRelayMessages(incoming);
        };
        var run = new Button { Text = "运行（按 ID）" };
        run.Clicked += async (_, _) => await ControlByPromptAsync("任务 ID", id => new { type = "task.control", taskId = id, action = "run" }, output);
        var cancel = new Button { Text = "终止（按 ID）" };
        cancel.Clicked += async (_, _) => await ControlByPromptAsync("任务 ID", id => new { type = "task.control", taskId = id, action = "cancel" }, output);
        var pause = new Button { Text = "暂停（按 ID）" };
        pause.Clicked += async (_, _) => await ControlByPromptAsync("任务 ID", id => new { type = "task.control", taskId = id, action = "pause" }, output);
        var resume = new Button { Text = "恢复（按 ID）" };
        resume.Clicked += async (_, _) => await ControlByPromptAsync("任务 ID", id => new { type = "task.control", taskId = id, action = "resume" }, output);
        var approve = new Button { Text = "批准（按 ID）" };
        approve.Clicked += async (_, _) => await ControlByPromptAsync("任务 ID", id => new { type = "task.control", taskId = id, action = "approve" }, output);
        var reject = new Button { Text = "拒绝（按 ID）" };
        reject.Clicked += async (_, _) => await ControlByPromptAsync("任务 ID", id => new { type = "task.control", taskId = id, action = "reject" }, output);
        var recover = new Button { Text = "回滚恢复（按 ID）" };
        recover.Clicked += async (_, _) => await ControlByPromptAsync("任务 ID", id => new { type = "task.recovery.rollback", taskId = id, assumeExclusive = false }, output);
        var delete = new Button { Text = "删除（按 ID）" };
        delete.Clicked += async (_, _) => await ControlByPromptAsync("任务 ID", id => new { type = "task.delete", taskId = id }, output);
        return FeatureRoot("任务与执行", new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new HorizontalStackLayout { Spacing = 8, Children = { title, detail, create, list, run, cancel, pause, resume, approve, reject, recover, delete } } }, output);
    }

    private View CreateProactiveView()
    {
        var output = OutputEditor();
        var refresh = new Button { Text = "刷新收件箱" };
        refresh.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "proactive.list", limit = 100 }, output);
        var chains = new Button { Text = "主动链" };
        chains.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "proactive.chain.list", limit = 100 }, output);
        var mark = new Button { Text = "处理收件箱（按 ID）" };
        mark.Clicked += async (_, _) =>
        {
            var id = await DisplayPromptAsync("主动消息 ID", "输入收件箱项目 ID");
            if (string.IsNullOrWhiteSpace(id)) return;
            var state = await DisplayActionSheetAsync("标记主动消息", "取消", null, "已读", "忽略", "已回复");
            var value = state switch { "已读" => "read", "忽略" => "dismissed", "已回复" => "replied", _ => null };
            if (value is not null) await RequestToEditorAsync(new { type = "proactive.inbox.mark", itemId = id.Trim(), state = value }, output);
        };
        var runChain = new Button { Text = "立即运行主动链" };
        runChain.Clicked += async (_, _) => await ControlByPromptAsync("主动链 ID", id => new { type = "proactive.chain.runNow", chainId = id }, output);
        var cancelChain = new Button { Text = "取消主动链" };
        cancelChain.Clicked += async (_, _) => await ControlByPromptAsync("主动链 ID", id => new { type = "proactive.chain.cancel", chainId = id }, output);
        var trigger = new Button { Text = "手动触发" };
        trigger.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "proactive.trigger", kind = "manual", reason = "MAUI 用户手动触发" }, output);
        var pause = new Button { Text = "暂停主动能力" };
        pause.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "proactive.setPaused", paused = true }, output);
        var resume = new Button { Text = "恢复主动能力" };
        resume.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "proactive.setPaused", paused = false }, output);
        var config = new Button { Text = "主动配置" };
        config.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "proactive.getConfig" }, output);
        var bedtime = new Button { Text = "设置睡眠小时" };
        bedtime.Clicked += async (_, _) =>
        {
            var value = await DisplayPromptAsync("睡眠小时", "输入 0-23");
            if (int.TryParse(value, out var hour)) await RequestToEditorAsync(new { type = "proactive.setConfig", bedtimeHour = Math.Clamp(hour, 0, 23) }, output);
        };
        return FeatureRoot("主动能力", new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new HorizontalStackLayout { Spacing = 8, Children = { refresh, chains, mark, runChain, cancelChain, trigger, pause, resume, config, bedtime } } }, output);
    }

    private View CreateSkillsView()
    {
        var output = OutputEditor();
        var list = new Button { Text = "刷新技能" };
        list.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "skill.list" }, output);
        var id = new Entry { Placeholder = "技能 ID", WidthRequest = 160 };
        var name = new Entry { Placeholder = "名称", WidthRequest = 160 };
        var prompt = new Entry { Placeholder = "提示词", WidthRequest = 300 };
        var install = new Button { Text = "安装/保存技能" };
        install.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "skill.install", skill = new { id = id.Text, name = name.Text, kind = "coding", description = name.Text, permissions = new[] { "workspace.read" }, prompt = prompt.Text, builtin = false } }, output);
        var remove = new Button { Text = "删除（按 ID）" };
        remove.Clicked += async (_, _) => await ControlByPromptAsync("技能 ID", value => new { type = "skill.remove", skillId = value }, output);
        var run = new Button { Text = "运行技能" };
        run.Clicked += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(id.Text)) return;
            var goal = await DisplayPromptAsync("技能目标", "输入这次技能要完成的目标") ?? string.Empty;
            await RequestToEditorAsync(new { type = "skill.run", skillId = id.Text.Trim(), goal }, output);
        };
        return FeatureRoot("技能", new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new HorizontalStackLayout { Spacing = 8, Children = { list, id, name, prompt, install, run, remove } } }, output);
    }

    private View CreateWorkflowsView()
    {
        var output = OutputEditor();
        var list = new Button { Text = "刷新工作流" };
        list.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "workflow.list" }, output);
        var workflow = new Entry { Placeholder = "工作流 ID", WidthRequest = 220 };
        var input = new Entry { Placeholder = "执行目标", WidthRequest = 320 };
        var run = new Button { Text = "运行工作流" };
        run.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "workflow.run", workflowId = workflow.Text, input = input.Text, isolatedWorktree = false, agentCount = 1 }, output);
        return FeatureRoot("工作流", new HorizontalStackLayout { Spacing = 8, Children = { list, workflow, input, run } }, output);
    }

    private View CreateMcpView()
    {
        var output = OutputEditor();
        var list = new Button { Text = "刷新 MCP" };
        list.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "mcp.list" }, output);
        var id = new Entry { Placeholder = "Server ID", WidthRequest = 160 };
        var name = new Entry { Placeholder = "名称", WidthRequest = 150 };
        var command = new Entry { Placeholder = "命令（如 npx）", WidthRequest = 180 };
        var set = new Button { Text = "保存 Server" };
        set.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "mcp.set", servers = new[] { new { id = id.Text, name = name.Text, command = command.Text, args = Array.Empty<string>(), enabled = true, permissions = new[] { "workspace.read" } } } }, output);
        var discover = new Button { Text = "发现工具" };
        discover.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "mcp.discover", serverId = id.Text }, output);
        var call = new Button { Text = "调用（按 tool）" };
        call.Clicked += async (_, _) =>
        {
            var tool = await DisplayPromptAsync("MCP 工具", "输入工具名");
            if (!string.IsNullOrWhiteSpace(tool)) await RequestToEditorAsync(new { type = "mcp.call", serverId = id.Text, tool, arguments = new { } }, output);
        };
        return FeatureRoot("MCP 工具", new HorizontalStackLayout { Spacing = 8, Children = { list, id, name, command, set, discover, call } }, output);
    }

    private View CreateChannelsView()
    {
        var output = OutputEditor();
        var status = new Button { Text = "Relay 状态" };
        status.Clicked += async (_, _) =>
        {
            if (OperatingSystem.IsAndroid()) output.Text = JsonSerializer.Serialize(_relay.Status, JsonOptions);
            else await RequestToEditorAsync(new { type = "relay.status" }, output);
        };
        var sync = new Button { Text = "立即同步 Relay" };
        sync.Clicked += async (_, _) =>
        {
            if (!OperatingSystem.IsAndroid()) { await RequestToEditorAsync(new { type = "relay.syncNow" }, output); return; }
            var incoming = await _relay.SyncAsync(persistIncoming: false);
            output.Text = JsonSerializer.Serialize(new { status = _relay.Status, incoming }, JsonOptions);
            AppendRelayMessages(incoming);
        };
        var pair = new Button { Text = "输入配对码" };
        pair.Clicked += async (_, _) =>
        {
            var raw = await DisplayPromptAsync("WebDAV 配对", "粘贴 pattern://pair?data=… 配对码");
            if (string.IsNullOrWhiteSpace(raw)) return;
            try
            {
                var paired = await _relay.ApplyPairingCodeAsync(raw);
                await ApplyRelayConfigurationAsync();
                output.Text = "配对成功：" + JsonSerializer.Serialize(_relay.Status, JsonOptions);
                StatusLabel.Text = "移动端中继已配对";
            }
            catch (Exception error) { output.Text = $"配对失败：{error.Message}"; }
        };
        var secureRequest = new Button { Text = "生成安全配对请求" };
        secureRequest.Clicked += async (_, _) =>
        {
            try
            {
                var code = await _relay.CreateSecurePairingRequestAsync();
                await Clipboard.Default.SetTextAsync(code);
                output.Text = $"已生成 X25519 安全配对请求并复制到剪贴板：\n{code}\n\n请把请求码交给桌面端生成加密响应。私钥只保存在本机安全存储。";
            }
            catch (Exception error) { output.Text = $"生成安全配对请求失败：{error.Message}"; }
        };
        var secureResponse = new Button { Text = "生成加密配对响应" };
        secureResponse.Clicked += async (_, _) =>
        {
            var request = await DisplayPromptAsync("安全配对", "粘贴手机生成的 X25519 请求码");
            if (string.IsNullOrWhiteSpace(request)) return;
            try
            {
                var code = _relay.CreateSecurePairingResponse(request);
                await Clipboard.Default.SetTextAsync(code);
                output.Text = $"已生成 X25519 + XChaCha20-Poly1305 加密响应并复制到剪贴板：\n{code}\n\n请把响应码交回手机端完成配对。";
            }
            catch (Exception error) { output.Text = $"生成加密响应失败：{error.Message}"; }
        };
        var telegram = new Button { Text = "配置 Telegram" };
        telegram.Clicked += async (_, _) =>
        {
            if (OperatingSystem.IsAndroid()) { output.Text = "Telegram 由配对的桌面 sidecar 运行，请在桌面端配置。"; return; }
            var token = await DisplayPromptAsync("Telegram Bot Token", "输入 bot token（留空停用）") ?? string.Empty;
            var chatId = string.IsNullOrWhiteSpace(token) ? string.Empty : await DisplayPromptAsync("Telegram Chat ID", "输入 chat id") ?? string.Empty;
            try
            {
                await _runtime.ConfigureAsync(new { telegram = new { enabled = !string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(chatId), token, chatId } });
                output.Text = string.IsNullOrWhiteSpace(token) ? "Telegram 已停用。" : "Telegram 配置已发送；token 不会写入普通日志。";
            }
            catch (Exception error) { output.Text = $"Telegram 配置失败：{error.Message}"; }
        };
        var email = new Button { Text = "配置邮件" };
        email.Clicked += async (_, _) =>
        {
            if (OperatingSystem.IsAndroid()) { output.Text = "邮件通道由配对的桌面 sidecar 运行，请在桌面端配置。"; return; }
            var host = await DisplayPromptAsync("SMTP Host", "输入 SMTP 主机（留空停用）") ?? string.Empty;
            var username = string.IsNullOrWhiteSpace(host) ? string.Empty : await DisplayPromptAsync("SMTP 用户名", "输入用户名") ?? string.Empty;
            var password = string.IsNullOrWhiteSpace(host) ? string.Empty : await DisplayPromptAsync("SMTP 密码", "输入密码") ?? string.Empty;
            var recipient = string.IsNullOrWhiteSpace(host) ? string.Empty : await DisplayPromptAsync("收件人", "输入收件邮箱") ?? string.Empty;
            var portText = string.IsNullOrWhiteSpace(host) ? "587" : await DisplayPromptAsync("SMTP 端口", "默认 587") ?? "587";
            var imapHost = string.IsNullOrWhiteSpace(host) ? string.Empty : await DisplayPromptAsync("IMAP Host（可选）", "留空则不启用收件轮询") ?? string.Empty;
            var imapPortText = string.IsNullOrWhiteSpace(imapHost) ? "993" : await DisplayPromptAsync("IMAP 端口", "默认 993") ?? "993";
            _ = int.TryParse(portText, out var port);
            _ = int.TryParse(imapPortText, out var imapPort);
            try
            {
                await _runtime.ConfigureAsync(new { email = new { enabled = !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(recipient), host, port = port is > 0 and < 65536 ? port : 587, secure = true, username, recipient, password, imapEnabled = !string.IsNullOrWhiteSpace(imapHost), imapHost, imapPort = imapPort is > 0 and < 65536 ? imapPort : 993, imapSecure = true } });
                output.Text = string.IsNullOrWhiteSpace(host) ? "邮件通道已停用。" : "SMTP 配置已发送；密码不写入普通日志。";
            }
            catch (Exception error) { output.Text = $"邮件配置失败：{error.Message}"; }
        };
        var manual = new Button { Text = "手动配置" };
        manual.Clicked += async (_, _) =>
        {
            var url = await DisplayPromptAsync("WebDAV URL", "例如 https://dav.example.com");
            if (string.IsNullOrWhiteSpace(url)) return;
            var username = await DisplayPromptAsync("用户名", "WebDAV 用户名") ?? string.Empty;
            var password = await DisplayPromptAsync("密码", "WebDAV 密码") ?? string.Empty;
            var secret = await DisplayPromptAsync("频道密钥", "与 sidecar 配置一致") ?? string.Empty;
            await _relay.SaveSettingsAsync(new RelaySettings(url, username, password, secret, Guid.NewGuid().ToString("N")));
            try
            {
                await ApplyRelayConfigurationAsync();
                output.Text = "中继配置已保存并应用到 sidecar。";
            }
            catch (Exception error) { output.Text = $"中继配置已保存；运行时稍后应用：{error.Message}"; }
        };
        var settings = new Button { Text = "打开通道设置" };
        settings.Clicked += (_, _) => ShowView("settings");
        var pluginStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var pluginId = new Entry { Placeholder = "插件 ID", WidthRequest = 170 };
        var pluginList = new Button { Text = "发现插件" };
        pluginList.Clicked += async (_, _) =>
        {
            try
            {
                var response = await _runtime.RequestAsync(new { type = "channel.plugins.list" });
                pluginStates.Clear();
                if (response.TryGetProperty("plugins", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.TryGetProperty("manifest", out var manifest) && manifest.TryGetProperty("id", out var id))
                            pluginStates[id.GetString() ?? ""] = item.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                    }
                }
                output.Text = Format(response);
            }
            catch (Exception error) { output.Text = $"插件发现失败：{error.Message}"; }
        };
        var pluginToggle = new Button { Text = "切换插件" };
        pluginToggle.Clicked += async (_, _) =>
        {
            var id = pluginId.Text?.Trim();
            if (string.IsNullOrWhiteSpace(id)) { output.Text = "请输入插件 ID。"; return; }
            if (!pluginStates.ContainsKey(id))
            {
                output.Text = "请先点击“发现插件”，确认该插件存在。";
                return;
            }
            pluginStates[id] = !pluginStates[id];
            try
            {
                var response = await _runtime.RequestAsync(new
                {
                    type = "channel.plugins.set",
                    plugins = pluginStates.Select(item => new { id = item.Key, enabled = item.Value }).ToArray(),
                });
                output.Text = Format(response);
            }
            catch (Exception error) { output.Text = $"插件设置失败：{error.Message}"; }
        };
        return FeatureRoot("通道与设备中继", new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new HorizontalStackLayout { Spacing = 8, Children = { status, sync, pair, secureRequest, secureResponse, telegram, email, manual, pluginList, pluginId, pluginToggle, settings } } }, output);
    }

    private View CreateModelsView()
    {
        var output = OutputEditor();
        var profiles = _settings.LoadModelProfiles();
        var profilePicker = new Picker { Title = "模型配置", WidthRequest = 220, ItemsSource = profiles.Select(item => item.Name).ToList() };
        var activeId = _settings.LoadActiveModelProfileId();
        profilePicker.SelectedIndex = Math.Max(0, profiles.FindIndex(item => item.Id == activeId));
        var profileName = new Entry { Placeholder = "配置名称", WidthRequest = 160 };
        var provider = new Entry { Placeholder = "Provider", WidthRequest = 150 };
        var endpoint = new Entry { Placeholder = "模型 Endpoint", WidthRequest = 280 };
        var model = new Entry { Placeholder = "当前模型", WidthRequest = 180 };
        var models = new Entry { Placeholder = "模型目录（逗号分隔）", WidthRequest = 260 };
        void LoadSelectedProfile()
        {
            var selected = profilePicker.SelectedIndex >= 0 && profilePicker.SelectedIndex < profiles.Count ? profiles[profilePicker.SelectedIndex] : null;
            if (selected is null) return;
            profileName.Text = selected.Name;
            provider.Text = selected.Provider;
            endpoint.Text = selected.Endpoint;
            model.Text = selected.Model;
            models.Text = string.Join(",", selected.Models);
        }
        profilePicker.SelectedIndexChanged += (_, _) => LoadSelectedProfile();
        LoadSelectedProfile();
        var add = new Button { Text = "新增配置" };
        add.Clicked += (_, _) =>
        {
            profiles.Add(new ModelProfile(Guid.NewGuid().ToString("N"), "新模型配置", "openai-compatible", "https://api.openai.com/v1", "gpt-4o-mini", ["gpt-4o-mini"]));
            profilePicker.ItemsSource = profiles.Select(item => item.Name).ToList();
            profilePicker.SelectedIndex = profiles.Count - 1;
        };
        var save = new Button { Text = "保存并切换" };
        save.Clicked += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(endpoint.Text) || string.IsNullOrWhiteSpace(model.Text)) { output.Text = "Endpoint 和模型不能为空。"; return; }
            var index = profilePicker.SelectedIndex < 0 ? profiles.Count : profilePicker.SelectedIndex;
            var current = index < profiles.Count ? profiles[index] : new ModelProfile(Guid.NewGuid().ToString("N"), "模型配置", "openai-compatible", "", "", []);
            var next = current with
            {
                Name = string.IsNullOrWhiteSpace(profileName.Text) ? current.Name : profileName.Text.Trim(),
                Provider = string.IsNullOrWhiteSpace(provider.Text) ? "openai-compatible" : provider.Text.Trim(),
                Endpoint = endpoint.Text.Trim(),
                Model = model.Text.Trim(),
                Models = models.Text?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList() ?? [model.Text.Trim()],
            };
            if (index < profiles.Count) profiles[index] = next; else profiles.Add(next);
            _settings.SaveModelProfiles(profiles);
            _settings.SaveActiveModelProfileId(next.Id);
            profilePicker.ItemsSource = profiles.Select(item => item.Name).ToList();
            profilePicker.SelectedIndex = profiles.FindIndex(item => item.Id == next.Id);
            var currentRuntime = _settings.LoadProfile();
            _settings.SaveProfile(currentRuntime with { Provider = next.Provider, Endpoint = next.Endpoint, Model = next.Model });
            try
            {
                await _runtime.ConfigureAsync(new
                {
                    provider = next.Provider,
                    endpoint = next.Endpoint,
                    model = next.Model,
                    apiKey = await _settings.LoadApiKeyAsync(),
                    persona = currentRuntime.Persona,
                    personaName = "Pattern",
                    userName = currentRuntime.UserName,
                    modelConnections = profiles.Select(item => new { id = item.Id, name = item.Name, provider = item.Provider, endpoint = item.Endpoint, models = item.Models, enabled = true }),
                });
                output.Text = $"已保存并切换到：{next.Name}\n共 {profiles.Count} 个模型配置。API Key 仍由系统安全存储管理。";
            }
            catch (Exception error) { output.Text = $"配置已保存，运行时应用失败：{error.Message}"; }
        };
        var catalog = new Button { Text = "模型目录" };
        catalog.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "model.catalog.get" }, output);
        var metrics = new Button { Text = "使用指标" };
        metrics.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "model.metrics.get" }, output);
        var balance = new Button { Text = "余额/额度" };
        balance.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "model.balance.check" }, output);
        var actions = new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new HorizontalStackLayout { Spacing = 8, Children = { profilePicker, profileName, provider, endpoint, model, models, add, save, catalog, metrics, balance } } };
        return FeatureRoot("模型与用量", actions, output);
    }

    private View CreateFileWatchView()
    {
        var output = OutputEditor();
        var get = new Button { Text = "配置" };
        get.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "filewatch.getConfig" }, output);
        var list = new Button { Text = "事件" };
        list.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "filewatch.list", limit = 100 }, output);
        var enable = new Button { Text = "启用工作区监控" };
        enable.Clicked += async (_, _) =>
        {
            var path = await DisplayPromptAsync("监控目录", "输入绝对路径");
            if (!string.IsNullOrWhiteSpace(path)) await RequestToEditorAsync(new { type = "filewatch.setConfig", config = new { enabled = true, paths = new[] { path }, extensions = new[] { ".md", ".txt", ".cs", ".ts" }, maxBytes = 65536 } }, output);
        };
        var disable = new Button { Text = "停用监控" };
        disable.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "filewatch.setConfig", config = new { enabled = false, paths = Array.Empty<string>(), extensions = Array.Empty<string>(), maxBytes = 65536 } }, output);
        return FeatureRoot("文件监控", new HorizontalStackLayout { Spacing = 8, Children = { get, list, enable, disable } }, output);
    }

    private View CreateAuditView()
    {
        var output = OutputEditor();
        var journal = new Button { Text = "审计日志" };
        journal.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "journal.list", limit = 200 }, output);
        var policy = new Button { Text = "安全策略" };
        policy.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "security.policy.get" }, output);
        var recovery = new Button { Text = "恢复状态" };
        recovery.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "recovery.status" }, output);
        var foreground = new Button { Text = "前台窗口" };
        foreground.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "runtime.foreground" }, output);
        return FeatureRoot("安全、审计与恢复", new HorizontalStackLayout { Spacing = 8, Children = { journal, policy, recovery, foreground } }, output);
    }

    private View CreateSettingsView()
    {
        var output = OutputEditor();
        var profile = _settings.LoadProfile();
        var endpoint = new Entry { Placeholder = "模型 Endpoint", Text = profile.Endpoint, WidthRequest = 300 };
        var model = new Entry { Placeholder = "模型", Text = profile.Model, WidthRequest = 180 };
        var provider = new Entry { Placeholder = "Provider", Text = profile.Provider, WidthRequest = 170 };
        var user = new Entry { Placeholder = "称呼", Text = profile.UserName, WidthRequest = 130 };
        var key = new Entry { Placeholder = "API Key（安全存储）", IsPassword = true, WidthRequest = 220 };
        var save = new Button { Text = "保存并应用" };
        save.Clicked += async (_, _) =>
        {
            var next = new RuntimeProfile(provider.Text ?? "openai-compatible", endpoint.Text ?? string.Empty, model.Text ?? string.Empty, profile.Persona, user.Text ?? "User", profile.ProactiveEnabled, profile.ProactivePaused, profile.BedtimeHour);
            _settings.SaveProfile(next);
            if (!string.IsNullOrWhiteSpace(key.Text)) await _settings.SaveApiKeyAsync(key.Text.Trim());
            try
            {
                await _runtime.ConfigureAsync(new { provider = next.Provider, endpoint = next.Endpoint, model = next.Model, apiKey = string.IsNullOrWhiteSpace(key.Text) ? await _settings.LoadApiKeyAsync() : key.Text.Trim(), persona = next.Persona, personaName = "Pattern", userName = next.UserName, modelConnections = ModelConnectionPayload(), proactive = new { enabled = next.ProactiveEnabled, paused = next.ProactivePaused, bedtimeHour = next.BedtimeHour } });
                output.Text = "配置已保存并发送到 sidecar。API Key 不写入普通日志。";
            }
            catch (Exception error) { output.Text = $"配置失败：{error.Message}"; }
        };
        var ping = new Button { Text = "运行时 Ping" };
        ping.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "runtime.ping" }, output, "运行时已连接");
        var health = new Button { Text = "健康检查配置" };
        health.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "healthcheck.getConfig" }, output, "健康检查配置已读取");
        var setHealth = new Button { Text = "设置健康 URL" };
        setHealth.Clicked += async (_, _) =>
        {
            var url = await DisplayPromptAsync("健康检查 URL", "输入 https://… URL（留空清空）");
            var label = string.IsNullOrWhiteSpace(url) ? "" : await DisplayPromptAsync("标签", "可选标签") ?? "";
            var checks = string.IsNullOrWhiteSpace(url) ? Array.Empty<object>() : new[] { new { url = url.Trim(), label = label.Trim() } }.Cast<object>().ToArray();
            await RequestToEditorAsync(new { type = "healthcheck.setConfig", checks }, output, "健康检查已更新");
        };
        var cron = new Button { Text = "Cron 配置" };
        cron.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "cron.getConfig" }, output, "Cron 配置已读取");
        var setCron = new Button { Text = "设置每日 Cron" };
        setCron.Clicked += async (_, _) =>
        {
            var time = await DisplayPromptAsync("Cron 时间", "HH:mm，例如 09:30（留空清空）");
            var message = string.IsNullOrWhiteSpace(time) ? "" : await DisplayPromptAsync("Cron 内容", "要发送给 Pattern 的内容") ?? "";
            var triggers = string.IsNullOrWhiteSpace(time) || string.IsNullOrWhiteSpace(message)
                ? Array.Empty<object>()
                : new[] { new { id = Guid.NewGuid().ToString("N"), time = time.Trim(), message = message.Trim(), enabled = true } }.Cast<object>().ToArray();
            await RequestToEditorAsync(new { type = "cron.setConfig", triggers }, output, "Cron 已更新");
        };
        var exportBackup = new Button { Text = "导出客户端备份" };
        exportBackup.Clicked += async (_, _) => await ExportBackupAsync(output);
        var importBackup = new Button { Text = "导入客户端备份" };
        importBackup.Clicked += async (_, _) => await ImportBackupAsync(output);
        var exportData = new Button { Text = "导出 Agent 数据" };
        exportData.Clicked += async (_, _) => await ExportDataSnapshotAsync(output);
        var importData = new Button { Text = "导入 Agent 数据" };
        importData.Clicked += async (_, _) => await ImportDataSnapshotAsync(output);
        var autostart = new Button { Text = "开机启动" };
        autostart.Clicked += (_, _) =>
        {
            var enabled = _autostart.SetEnabled(!_autostart.IsEnabled);
            output.Text = _autostart.Supported
                ? (enabled ? $"Windows 开机启动已{(_autostart.IsEnabled ? "启用" : "停用")}" : "开机启动设置失败")
                : "当前平台不支持由 Pattern 管理开机启动。";
        };
        var backupHint = new Label { Text = "备份包含配置与会话，不包含 API Key/Relay 密钥", FontSize = 11, TextColor = (Color)Application.Current!.Resources["TextMuted"], VerticalTextAlignment = TextAlignment.Center };
        var connectionSection = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label { Text = "常规连接", TextColor = (Color)Application.Current.Resources["Accent"], FontSize = 11, FontAttributes = FontAttributes.Bold },
                new Label { Text = "模型服务和身份信息", TextColor = (Color)Application.Current.Resources["TextMuted"], FontSize = 11 },
                provider,
                endpoint,
                model,
                user,
                key,
                save,
            },
        };
        var runtimeSection = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label { Text = "运行时", TextColor = (Color)Application.Current.Resources["Accent"], FontSize = 11, FontAttributes = FontAttributes.Bold },
                new HorizontalStackLayout { Spacing = 8, Children = { ping, health, setHealth, cron, setCron } },
            },
        };
        var backupSection = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label { Text = "数据与系统", TextColor = (Color)Application.Current.Resources["Accent"], FontSize = 11, FontAttributes = FontAttributes.Bold },
                new HorizontalStackLayout { Spacing = 8, Children = { exportBackup, importBackup, exportData, importData, autostart } },
                backupHint,
            },
        };
        var actions = new VerticalStackLayout
        {
            Spacing = 18,
            Children = { connectionSection, new BoxView { HeightRequest = 1, Color = (Color)Application.Current.Resources["Line"] }, runtimeSection, new BoxView { HeightRequest = 1, Color = (Color)Application.Current.Resources["Line"] }, backupSection },
        };
        return FeatureRoot("设置与运行时", actions, output);
    }

    private async Task ExportBackupAsync(Editor output)
    {
        try
        {
            var backup = _settings.CreateBackup(_relay.CurrentSettings);
            var json = JsonSerializer.Serialize(backup, JsonOptions);
            var path = System.IO.Path.Combine(FileSystem.CacheDirectory, $"pattern-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(path, json);
            try
            {
                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "Pattern 客户端备份",
                    File = new ShareFile(path),
                });
                output.Text = $"备份已生成并打开系统分享：\n{path}\n\nAPI Key 与 Relay 密钥未包含在备份中。";
            }
            catch (Exception shareError)
            {
                output.Text = $"备份已生成（系统分享不可用）：\n{path}\n{shareError.Message}";
            }
            StatusLabel.Text = "客户端备份已导出";
        }
        catch (Exception error)
        {
            output.Text = $"导出失败：{error.Message}";
            StatusLabel.Text = "客户端备份导出失败";
        }
    }

    private async Task ImportBackupAsync(Editor output)
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "选择 Pattern JSON 备份" });
            if (file is null) return;
            await using var stream = await file.OpenReadAsync();
            if (stream.Length > 8 * 1024 * 1024) throw new InvalidOperationException("备份文件超过 8 MB 限制。");
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var backup = AppSettingsStore.ParseBackup(json);
            _settings.RestoreBackup(backup);
            LoadHistory();
            output.Text = $"已导入 {backup.Sessions.Count} 个会话（版本 {backup.Version}）。API Key 与 Relay 密钥不会被覆盖，请在设置/通道页重新配置。";
            StatusLabel.Text = "客户端备份已导入";
            ShowView("conversations");
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            output.Text = $"导入失败：{error.Message}";
            StatusLabel.Text = "客户端备份导入失败";
        }
    }

    private async Task ExportDataSnapshotAsync(Editor output)
    {
        try
        {
            if (OperatingSystem.IsAndroid()) throw new InvalidOperationException("Android 是 relay-only 客户端，Agent 数据由配对的桌面 sidecar 管理。");
            var response = await _runtime.RequestAsync(new { type = "data.export" }, TimeSpan.FromSeconds(30));
            var snapshot = response.TryGetProperty("snapshot", out var value) ? value : response;
            var path = System.IO.Path.Combine(FileSystem.CacheDirectory, $"pattern-agent-data-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(path, snapshot.GetRawText());
            try
            {
                await Share.Default.RequestAsync(new ShareFileRequest { Title = "Pattern Agent 数据快照", File = new ShareFile(path) });
                output.Text = $"Agent 数据快照已导出并打开系统分享：\n{path}\n\n设备密钥、relay outbox、模型缓存和插件目录已排除。";
            }
            catch (Exception shareError) { output.Text = $"快照已生成（系统分享不可用）：\n{path}\n{shareError.Message}"; }
            StatusLabel.Text = "Agent 数据快照已导出";
        }
        catch (Exception error)
        {
            output.Text = $"导出 Agent 数据失败：{error.Message}";
            StatusLabel.Text = "Agent 数据导出失败";
        }
    }

    private async Task ImportDataSnapshotAsync(Editor output)
    {
        try
        {
            if (OperatingSystem.IsAndroid()) throw new InvalidOperationException("Android 是 relay-only 客户端，请在桌面 sidecar 所在设备导入 Agent 数据。");
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "选择 Pattern Agent 数据快照" });
            if (file is null) return;
            await using var stream = await file.OpenReadAsync();
            if (stream.Length > 8 * 1024 * 1024) throw new InvalidOperationException("Agent 数据快照超过 8 MB 限制。");
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            using var document = JsonDocument.Parse(json);
            var response = await _runtime.RequestAsync(new Dictionary<string, object?>
            {
                ["type"] = "data.import",
                ["snapshot"] = document.RootElement.Clone(),
            }, TimeSpan.FromSeconds(30));
            output.Text = Format(response) + "\n\n导入后建议重启 Pattern，使主动能力引擎重新载入快照文件。";
            StatusLabel.Text = "Agent 数据快照已导入";
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            output.Text = $"导入 Agent 数据失败：{error.Message}";
            StatusLabel.Text = "Agent 数据导入失败";
        }
    }

    private async Task ControlByPromptAsync(string title, Func<string, object> requestFactory, Editor output)
    {
        var value = await DisplayPromptAsync(title, "输入 ID");
        if (!string.IsNullOrWhiteSpace(value)) await RequestToEditorAsync(requestFactory(value.Trim()), output);
    }

    private async Task ApplyRelayConfigurationAsync()
    {
        if (OperatingSystem.IsAndroid() || !_runtime.IsConnected) return;
        var relay = _relay.CurrentSettings;
        await _runtime.ConfigureAsync(new
        {
            webdav = relay.IsConfigured ? new { url = relay.Url, username = relay.Username, password = relay.Password } : null,
            deviceId = relay.DeviceId,
            channelKey = relay.ChannelKey,
        });
    }

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

    private async Task SendAsync()
    {
        var text = _messageEntry?.Text?.Trim();
        if (string.IsNullOrEmpty(text) || _activeChatId is not null) return;
        _messageEntry!.Text = string.Empty;
        var priorHistory = _history.ToArray();
        _activeAssistantText = string.Empty;
        _chatError = string.Empty;
        _conversation += $"\n\n你：{text}\nPattern：";
        RefreshConversationView();
        try
        {
            if (OperatingSystem.IsAndroid())
            {
                await _relay.PublishChatAsync(text);
                _history.Add(new ChatTurn("user", text));
                _attachedPaths.Clear();
                ResetAttachButton();
                SaveHistory();
                StatusLabel.Text = _relay.Status.Online ? "消息已发送到中继" : "已进入离线 outbox";
                return;
            }
            _activeChatId = await _runtime.SendChatAsync(text, priorHistory, _activeSessionId, _attachedPaths.ToArray());
            _attachedPaths.Clear();
            ResetAttachButton();
            _history.Add(new ChatTurn("user", text));
            if (_cancelButton is not null) _cancelButton.IsEnabled = true;
            StatusLabel.Text = "Pattern 正在思考…";
            SaveHistory();
        }
        catch (Exception error)
        {
            _conversation += $"\n[发送失败：{error.Message}]";
            _chatError = error.Message;
            RefreshConversationView();
        }
    }

    private void ResetAttachButton()
    {
        if (_attachButton is not null) _attachButton.Text = "附加";
    }

    private sealed record WorkspaceRow(string Kind, string Path, string Display);
}
