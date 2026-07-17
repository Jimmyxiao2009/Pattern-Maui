using System.Text.Json;
using Microsoft.Maui.Controls.Shapes;
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
    private readonly List<ChatTurn> _history = [];
    private readonly List<ConversationSession> _sessions = [];
    private readonly Dictionary<string, View> _views = [];
    private Label? _conversationLabel;
    private Entry? _messageEntry;
    private Button? _cancelButton;
    private string? _activeChatId;
    private string _activeSessionId = "default";
    private string _conversation = "欢迎回来。Pattern 现在由 .NET MAUI 驱动，Agent 通过本地 stdio sidecar 运行。";
    private string _activeAssistantText = string.Empty;
    private bool _loaded;

    public MainPage(SidecarRuntime runtime, AppSettingsStore settings, RelayService relay, NativeBridgeService bridge, SingleInstanceService instance, GlobalHotkeyService hotkey, WindowsTrayService tray)
    {
        InitializeComponent();
        _runtime = runtime;
        _settings = settings;
        _relay = relay;
        _bridge = bridge;
        _instance = instance;
        _hotkey = hotkey;
        _tray = tray;
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
            var button = new Button { Text = label, Padding = new Thickness(13, 6), FontSize = 13, CornerRadius = 10 };
            button.Clicked += (_, _) => ShowView(id);
            NavigationBar.Children.Add(button);
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
            if (_conversationLabel is not null) _conversationLabel.Text = _conversation;
        });
        _runtime.ChatDone += () => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!string.IsNullOrWhiteSpace(_activeAssistantText)) _history.Add(new ChatTurn("assistant", _activeAssistantText));
            _activeAssistantText = string.Empty;
            _activeChatId = null;
            if (_cancelButton is not null) _cancelButton.IsEnabled = false;
            StatusLabel.Text = "运行时已连接";
            SaveHistory();
        });
        _runtime.ChatCancelled += () => MainThread.BeginInvokeOnMainThread(() =>
        {
            _activeChatId = null;
            if (_cancelButton is not null) _cancelButton.IsEnabled = false;
            StatusLabel.Text = "回复已取消";
        });
        _runtime.ChatError += error => MainThread.BeginInvokeOnMainThread(() =>
        {
            _conversation += $"\n[回复失败：{error}]";
            if (_conversationLabel is not null) _conversationLabel.Text = _conversation;
            _activeChatId = null;
            if (_cancelButton is not null) _cancelButton.IsEnabled = false;
            StatusLabel.Text = "回复失败，运行时仍保持连接";
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
        _conversation = _history.Count == 0
            ? "欢迎回来。Pattern 现在由 .NET MAUI 驱动，Agent 通过本地 stdio sidecar 运行。"
            : string.Join("\n\n", _history.Select(turn => turn.Role == "user" ? $"你：{turn.Content}" : $"Pattern：{turn.Content}"));
        if (_conversationLabel is not null) _conversationLabel.Text = _conversation;
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
            Content = new ScrollView { Content = _conversationLabel },
        };
        _messageEntry = new Entry { Placeholder = "给 Pattern 发消息", ReturnType = ReturnType.Send };
        _messageEntry.Completed += async (_, _) => await SendAsync();
        var send = new Button { Text = "发送", BackgroundColor = (Color)Application.Current!.Resources["Accent"], TextColor = (Color)Application.Current!.Resources["PageBackground"] };
        send.Clicked += async (_, _) => await SendAsync();
        _cancelButton = new Button { Text = "停止", IsEnabled = false };
        _cancelButton.Clicked += async (_, _) =>
        {
            if (_activeChatId is null) return;
            await _runtime.CancelChatAsync(_activeChatId);
        };
        var composer = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 8 };
        composer.Add(_messageEntry, 0, 0);
        composer.Add(send, 1, 0);
        composer.Add(_cancelButton, 2, 0);
        Grid.SetRow(composer, 1);
        return new Grid
        {
            RowDefinitions = new RowDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) },
            RowSpacing = 14,
            Children = { transcript, composer },
        };
    }

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
            await _settings.SaveApiKeyAsync(key.Text ?? string.Empty);
            try
            {
                await _runtime.ConfigureAsync(new { provider = next.Provider, endpoint = next.Endpoint, model = next.Model, apiKey = key.Text ?? await _settings.LoadApiKeyAsync(), persona = next.Persona, personaName = "Pattern", userName = next.UserName, proactive = new { enabled = next.ProactiveEnabled, paused = next.ProactivePaused, bedtimeHour = next.BedtimeHour } });
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
        TextColor = Colors.White,
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
        var root = new Grid { RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) }, RowSpacing = 12 };
        root.Add(new Label { Text = title, FontSize = 24, FontAttributes = FontAttributes.Bold }, 0, 0);
        root.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = actions }, 0, 1);
        root.Add(new ScrollView { Content = output }, 0, 2);
        return root;
    }

    private View CreateProjectView()
    {
        var output = OutputEditor();
        var path = new Entry { Placeholder = "工作区绝对路径", WidthRequest = 360 };
        var name = new Entry { Placeholder = "项目名", WidthRequest = 160 };
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
        return FeatureRoot("项目工作区", new HorizontalStackLayout { Spacing = 8, Children = { path, name, sync, diff, worktree } }, output);
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
            if (_conversationLabel is not null) _conversationLabel.Text = _conversation;
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
        var refresh = new Button { Text = "搜索/刷新" };
        refresh.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "memory.list", query = string.IsNullOrWhiteSpace(query.Text) ? null : query.Text, category = string.IsNullOrWhiteSpace(category.Text) ? null : category.Text }, output);
        var add = new Button { Text = "记住" };
        add.Clicked += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(text.Text)) return;
            await RequestToEditorAsync(new { type = "memory.add", item = new { text = text.Text, category = string.IsNullOrWhiteSpace(category.Text) ? "fact" : category.Text, importance = 0.6 } }, output, "记忆已写入");
            text.Text = string.Empty;
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
        return FeatureRoot("记忆", new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new HorizontalStackLayout { Spacing = 8, Children = { query, category, refresh, text, add, proposals, accept, reject, expire, consolidate } } }, output);
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
            await RequestToEditorAsync(new { type = "task.create", title = title.Text, detail = detail.Text ?? string.Empty }, output, "任务已创建");
            title.Text = string.Empty;
            detail.Text = string.Empty;
        };
        var list = new Button { Text = "刷新任务" };
        list.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "task.list" }, output);
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
        return FeatureRoot("主动能力", new HorizontalStackLayout { Spacing = 8, Children = { refresh, chains, trigger, pause, resume, config, bedtime } }, output);
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
        return FeatureRoot("技能", new HorizontalStackLayout { Spacing = 8, Children = { list, id, name, prompt, install, remove } }, output);
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
            var incoming = await _relay.SyncAsync();
            output.Text = JsonSerializer.Serialize(new { status = _relay.Status, incoming }, JsonOptions);
            foreach (var item in incoming.Where(item => item.Type == "chat"))
            {
                _conversation += $"\n\n中继消息：{item.Body}";
                if (_conversationLabel is not null) _conversationLabel.Text = _conversation;
            }
        };
        var pair = new Button { Text = "输入配对码" };
        pair.Clicked += async (_, _) =>
        {
            var raw = await DisplayPromptAsync("WebDAV 配对", "粘贴 pattern://pair?data=… 配对码");
            if (string.IsNullOrWhiteSpace(raw)) return;
            try
            {
                var paired = RelayService.ParsePairingCode(raw);
                await _relay.SaveSettingsAsync(paired);
                await ApplyRelayConfigurationAsync();
                output.Text = "配对成功：" + JsonSerializer.Serialize(_relay.Status, JsonOptions);
                StatusLabel.Text = "移动端中继已配对";
            }
            catch (Exception error) { output.Text = $"配对失败：{error.Message}"; }
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
        return FeatureRoot("通道与设备中继", new HorizontalStackLayout { Spacing = 8, Children = { status, sync, pair, manual, settings } }, output);
    }

    private View CreateModelsView()
    {
        var output = OutputEditor();
        var catalog = new Button { Text = "模型目录" };
        catalog.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "model.catalog.get" }, output);
        var metrics = new Button { Text = "使用指标" };
        metrics.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "model.metrics.get" }, output);
        var balance = new Button { Text = "余额/额度" };
        balance.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "model.balance.check" }, output);
        return FeatureRoot("模型与用量", new HorizontalStackLayout { Spacing = 8, Children = { catalog, metrics, balance } }, output);
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
            await _settings.SaveApiKeyAsync(key.Text ?? string.Empty);
            try
            {
                await _runtime.ConfigureAsync(new { provider = next.Provider, endpoint = next.Endpoint, model = next.Model, apiKey = key.Text ?? await _settings.LoadApiKeyAsync(), persona = next.Persona, personaName = "Pattern", userName = next.UserName, proactive = new { enabled = next.ProactiveEnabled, paused = next.ProactivePaused, bedtimeHour = next.BedtimeHour } });
                output.Text = "配置已保存并发送到 sidecar。API Key 不写入普通日志。";
            }
            catch (Exception error) { output.Text = $"配置失败：{error.Message}"; }
        };
        var ping = new Button { Text = "运行时 Ping" };
        ping.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "runtime.ping" }, output);
        var health = new Button { Text = "健康检查配置" };
        health.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "healthcheck.getConfig" }, output);
        var setHealth = new Button { Text = "设置健康 URL" };
        setHealth.Clicked += async (_, _) =>
        {
            var url = await DisplayPromptAsync("健康检查 URL", "输入 https://… URL（留空清空）");
            var label = string.IsNullOrWhiteSpace(url) ? "" : await DisplayPromptAsync("标签", "可选标签") ?? "";
            var checks = string.IsNullOrWhiteSpace(url) ? Array.Empty<object>() : new[] { new { url = url.Trim(), label = label.Trim() } }.Cast<object>().ToArray();
            await RequestToEditorAsync(new { type = "healthcheck.setConfig", checks }, output, "健康检查已更新");
        };
        var cron = new Button { Text = "Cron 配置" };
        cron.Clicked += async (_, _) => await RequestToEditorAsync(new { type = "cron.getConfig" }, output);
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
        var backupHint = new Label { Text = "备份包含配置与会话，不包含 API Key/Relay 密钥", FontSize = 12, TextColor = Colors.Gray, VerticalTextAlignment = TextAlignment.Center };
        return FeatureRoot("设置与运行时", new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new HorizontalStackLayout { Spacing = 8, Children = { provider, endpoint, model, user, key, save, ping, health, setHealth, cron, setCron, exportBackup, importBackup, exportData, importData, backupHint } } }, output);
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
        _conversation += $"\n\n你：{text}\nPattern：";
        if (_conversationLabel is not null) _conversationLabel.Text = _conversation;
        try
        {
            if (OperatingSystem.IsAndroid())
            {
                await _relay.PublishChatAsync(text);
                _history.Add(new ChatTurn("user", text));
                SaveHistory();
                StatusLabel.Text = _relay.Status.Online ? "消息已发送到中继" : "已进入离线 outbox";
                return;
            }
            _activeChatId = await _runtime.SendChatAsync(text, priorHistory, _activeSessionId);
            _history.Add(new ChatTurn("user", text));
            if (_cancelButton is not null) _cancelButton.IsEnabled = true;
            StatusLabel.Text = "Pattern 正在思考…";
            SaveHistory();
        }
        catch (Exception error)
        {
            _conversation += $"\n[发送失败：{error.Message}]";
            if (_conversationLabel is not null) _conversationLabel.Text = _conversation;
        }
    }
}
