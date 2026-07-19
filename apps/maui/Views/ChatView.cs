using Microsoft.Maui.Controls.Shapes;
using Pattern.Maui.Services;

namespace Pattern.Maui.Views;

public static partial class ChatView
{
    public static View Create(SidecarRuntime runtime, AppSettingsStore settings)
    {
        var res = Application.Current!.Resources;

        // ========== Conversation Header ==========
        var eyebrow = new Label
        {
            Text = "陪伴槽",
            Style = (Style)res["EyebrowLabel"],
        };

        var title = new Label
        {
            Text = "今晚",
            Style = (Style)res["PageTitle"],
        };

        var newChatButton = new Button
        {
            Text = "＋ 新对话",
            Style = (Style)res["QuietButton"],
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star),
                new(GridLength.Auto),
            },
            Padding = new Thickness(42, 26, 42, 4),
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 3,
                    Children = { eyebrow, title },
                },
                newChatButton,
            },
        };
        Grid.SetColumn(newChatButton, 1);

        // ========== Chat Stream ==========
        var chatStream = new VerticalStackLayout { Spacing = 22 };

        // Day divider
        chatStream.Children.Add(CreateDayDivider("7 月 12 日 · 周日", res));

        // Proactive assistant message (with badge)
        chatStream.Children.Add(CreateAssistantMessage(
            "Pattern",
            "架构文档已经放了一个下午。先把 M0 的界面骨架搭起来，别等到晚上又说「明天再做」。",
            true,
            "主动 · 日程",
            "16:08",
            res));

        // User message
        chatStream.Children.Add(CreateUserMessage(
            "你",
            "行。先按设计稿把主界面做出来。",
            "16:10",
            res));

        // Assistant message with memory receipt
        chatStream.Children.Add(CreateAssistantMessage(
            "Pattern",
            "可以。先保住对话、记忆、任务和通道这四条主线。设置页只放真正影响常驻体验的开关，别一开始就堆满表单。",
            false,
            null,
            "16:11",
            res,
            "已记住 · 偏好「先做核心流程」"));

        var transcript = new ScrollView
        {
            Padding = new Thickness(42, 10),
            Content = new Border
            {
                MaximumWidthRequest = 820,
                HorizontalOptions = LayoutOptions.Center,
                StrokeThickness = 0,
                BackgroundColor = Colors.Transparent,
                Content = chatStream,
            },
        };

        // ========== Composer ==========
        var messageEntry = new Entry
        {
            Placeholder = "和 Pattern 说点什么……",
            BackgroundColor = Colors.Transparent,
            TextColor = (Color)res["TextPrimary"],
            PlaceholderColor = (Color)res["TextFaint"],
            HeightRequest = 40,
        };

        var attachButton = new Button
        {
            Text = "＋",
            FontSize = 16,
            WidthRequest = 28,
            HeightRequest = 28,
            Padding = new Thickness(0),
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            TextColor = (Color)res["TextMuted"],
        };

        var routeHint = new Label
        {
            Text = "陪伴槽",
            TextColor = (Color)res["TextFaint"],
            FontSize = 11,
            Margin = new Thickness(0, 0, 8, 0),
        };

        var sendButton = new Button
        {
            Text = "↑",
            FontSize = 19,
            FontAttributes = FontAttributes.Bold,
            WidthRequest = 32,
            HeightRequest = 32,
            Padding = new Thickness(0),
            BackgroundColor = (Color)res["Accent"],
            TextColor = (Color)res["PageBackground"],
            BorderWidth = 0,
            CornerRadius = 4,
        };

        var liveResponse = new Label
        {
            Text = string.Empty,
            FontSize = 14,
            LineHeight = 1.8,
            TextColor = (Color)res["TextPrimary"],
            IsVisible = false,
        };
        var liveMessage = new Border
        {
            MaximumWidthRequest = 620,
            HorizontalOptions = LayoutOptions.Start,
            Padding = new Thickness(0),
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "●", TextColor = (Color)res["Accent"], FontSize = 11 }, new Label { Text = "Pattern", TextColor = (Color)res["Accent"], FontSize = 11, FontAttributes = FontAttributes.Bold }, new Label { Text = "正在回复", TextColor = (Color)res["TextFaint"], FontSize = 10 } } },
                    liveResponse,
                },
            },
            IsVisible = false,
        };
        chatStream.Children.Add(liveMessage);

        async Task SendAsync()
        {
            var text = messageEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            messageEntry.Text = string.Empty;
            chatStream.Children.Insert(Math.Max(0, chatStream.Children.Count - 1), CreateUserMessage("你", text, DateTime.Now.ToString("HH:mm"), res));
            liveResponse.Text = "正在组织回答…";
            liveResponse.IsVisible = true;
            liveMessage.IsVisible = true;
            sendButton.IsEnabled = false;
            try
            {
                await runtime.SendChatAsync(text, Array.Empty<ChatTurn>(), "default");
            }
            catch (Exception error)
            {
                liveResponse.Text = $"回复失败：{error.Message}";
            }
            finally { sendButton.IsEnabled = true; }
        }
        sendButton.Clicked += async (_, _) => await SendAsync();
        messageEntry.Completed += async (_, _) => await SendAsync();
        newChatButton.Clicked += (_, _) =>
        {
            chatStream.Children.Clear();
            chatStream.Children.Add(CreateDayDivider(DateTime.Now.ToString("M 月 d 日 · ddd"), res));
            liveMessage.IsVisible = false;
            liveResponse.Text = string.Empty;
        };
        runtime.ChatDelta += delta => MainThread.BeginInvokeOnMainThread(() =>
        {
            liveResponse.Text = liveResponse.Text == "正在组织回答…" ? delta : liveResponse.Text + delta;
            liveResponse.IsVisible = true;
            liveMessage.IsVisible = true;
        });
        runtime.ChatDone += () => MainThread.BeginInvokeOnMainThread(() =>
        {
            liveMessage.IsVisible = false;
            liveResponse.Text = string.Empty;
            sendButton.IsEnabled = true;
        });
        runtime.ChatError += error => MainThread.BeginInvokeOnMainThread(() =>
        {
            liveResponse.Text = $"回复失败：{error}";
            liveResponse.IsVisible = true;
            liveMessage.IsVisible = true;
            sendButton.IsEnabled = true;
        });

        var composerActions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Auto),
                new(GridLength.Star),
                new(GridLength.Auto),
                new(GridLength.Auto),
            },
            ColumnSpacing = 8,
        };
        composerActions.Add(attachButton, 0, 0);
        composerActions.Add(messageEntry, 1, 0);
        composerActions.Add(routeHint, 2, 0);
        composerActions.Add(sendButton, 3, 0);

        var composerModes = new HorizontalStackLayout
        {
            Spacing = 6,
            Margin = new Thickness(30, 0, 0, 0),
            Children =
            {
                new Button { Text = "子 Agent · 开", FontSize = 10, Padding = new Thickness(8, 4), BackgroundColor = (Color)res["AccentWash"], BorderColor = (Color)res["AccentLine"], TextColor = (Color)res["Accent"] },
                new Button { Text = "→ 常规⌄", FontSize = 10, Padding = new Thickness(8, 4), BackgroundColor = Colors.Transparent, BorderColor = (Color)res["Line"], TextColor = (Color)res["TextMuted"] },
                new Button { Text = "⚯ 技能", FontSize = 10, Padding = new Thickness(8, 4), BackgroundColor = Colors.Transparent, BorderColor = (Color)res["Line"], TextColor = (Color)res["TextMuted"] },
                new Label { Text = "主 Agent · 可派生子代理", TextColor = (Color)res["TextFaint"], FontSize = 10, VerticalTextAlignment = TextAlignment.Center, HorizontalOptions = LayoutOptions.Fill },
                new Button { Text = "选择模型⌄", FontSize = 10, Padding = new Thickness(8, 4), BackgroundColor = Colors.Transparent, BorderColor = (Color)res["Line"], TextColor = (Color)res["TextMuted"] },
                new Button { Text = "♢⌄", FontSize = 10, Padding = new Thickness(7, 4), BackgroundColor = Colors.Transparent, BorderColor = (Color)res["Line"], TextColor = (Color)res["TextMuted"] },
            },
        };
        var composer = new Border
        {
            Margin = new Thickness(28, 8, 28, 16),
            Padding = new Thickness(12, 12),
            BackgroundColor = (Color)res["PanelBackground"],
            Stroke = (Color)res["LineStrong"],
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
            Content = new VerticalStackLayout { Spacing = 7, Children = { composerActions, composerModes } },
        };

        // ========== Main Content Area ==========
        var main = new Grid
        {
            RowDefinitions = new RowDefinitionCollection
            {
                new(GridLength.Auto),
                new(GridLength.Star),
                new(GridLength.Auto),
            },
            BackgroundColor = (Color)res["SurfaceBackground"],
            Children =
            {
                header,
                transcript,
                composer,
            },
        };
        Grid.SetRow(transcript, 1);
        Grid.SetRow(composer, 2);

        // ========== Recent Sessions Sidebar ==========
        var recentsStack = new VerticalStackLayout { Spacing = 4 };
        recentsStack.Children.Add(CreateSessionItem("今晚", true, res));
        recentsStack.Children.Add(CreateSessionItem("代码审查", false, res));
        recentsStack.Children.Add(CreateSessionItem("周末计划", false, res));

        var sidebarNewChat = new Button
        {
            Text = "＋ 新建对话",
            HorizontalOptions = LayoutOptions.Fill,
            BackgroundColor = (Color)res["AccentWash"],
            BorderColor = (Color)res["AccentLine"],
            TextColor = (Color)res["Accent"],
            BorderWidth = 1,
            CornerRadius = 6,
            FontSize = 11,
            Padding = new Thickness(8, 6),
        };
        sidebarNewChat.Clicked += (_, _) =>
        {
            chatStream.Children.Clear();
            chatStream.Children.Add(CreateDayDivider(DateTime.Now.ToString("M 月 d 日 · ddd"), res));
            liveMessage.IsVisible = false;
            liveResponse.Text = string.Empty;
        };

        var sidebar = new Border
        {
            Padding = new Thickness(16, 24, 12, 18),
            BackgroundColor = (Color)res["SurfaceBackground"],
            Stroke = (Color)res["Line"],
            StrokeThickness = 1,
            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Spacing = 12,
                    Children =
                    {
                        new Label { Text = "最近内容", TextColor = (Color)res["TextFaint"], FontSize = 10, FontAttributes = FontAttributes.Bold },
                        sidebarNewChat,
                        new Label { Text = "最近聊天", TextColor = (Color)res["TextMuted"], FontSize = 11, Margin = new Thickness(0, 8, 0, 0) },
                        recentsStack,
                        new Label { Text = "主动收件箱", TextColor = (Color)res["TextMuted"], FontSize = 11, Margin = new Thickness(0, 10, 0, 0) },
                        new Label { Text = "没有待处理的主动消息", TextColor = (Color)res["TextFaint"], FontSize = 10 },
                    },
                },
            },
        };

        // ========== Shell Layout ==========
        var shell = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(new GridLength(220)),
                new(GridLength.Star),
            },
            BackgroundColor = (Color)res["SurfaceBackground"],
            Children =
            {
                sidebar,
                main,
            },
        };
        Grid.SetColumn(main, 1);

        return shell;
    }

    // ========== Message Helpers ==========

    private static View CreateDayDivider(string text, ResourceDictionary res)
    {
        return new HorizontalStackLayout
        {
            HorizontalOptions = LayoutOptions.Center,
            Spacing = 12,
            Margin = new Thickness(0, 8),
            Children =
            {
                new BoxView { HeightRequest = 1, Color = (Color)res["Line"], VerticalOptions = LayoutOptions.Center, WidthRequest = 80 },
                new Label { Text = text, TextColor = (Color)res["TextFaint"], FontSize = 11, FontFamily = "Consolas" },
                new BoxView { HeightRequest = 1, Color = (Color)res["Line"], VerticalOptions = LayoutOptions.Center, WidthRequest = 80 },
            },
        };
    }

    private static Border CreateUserMessage(string speaker, string text, string time, ResourceDictionary res)
    {
        var meta = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label
                {
                    Text = speaker,
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = (Color)res["TextPrimary"],
                },
                new Label
                {
                    Text = time,
                    FontFamily = "Consolas",
                    FontSize = 10,
                    TextColor = (Color)res["TextFaint"],
                },
            },
        };

        var body = new Label
        {
            Text = text,
            FontSize = 14,
            LineHeight = 1.8,
            LineBreakMode = LineBreakMode.WordWrap,
            TextColor = (Color)res["TextPrimary"],
        };

        return new Border
        {
            MaximumWidthRequest = 620,
            HorizontalOptions = LayoutOptions.End,
            Padding = new Thickness(14, 10),
            BackgroundColor = (Color)res["PanelBackground"],
            Stroke = (Color)res["Line"],
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8, 8, 2, 8) },
            Content = new VerticalStackLayout
            {
                Spacing = 5,
                Children = { meta, body },
            },
        };
    }

    private static Border CreateAssistantMessage(string speaker, string text, bool isProactive, string? badge, string time, ResourceDictionary res, string? memoryReceipt = null)
    {
        // Eye icon (small amber circle)
        var eye = new Border
        {
            WidthRequest = 12,
            HeightRequest = 12,
            StrokeThickness = 0,
            BackgroundColor = (Color)res["Accent"],
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) },
            Content = new Label
            {
                Text = "·",
                TextColor = (Color)res["PageBackground"],
                FontSize = 8,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            },
        };

        var metaChildren = new List<View>
        {
            eye,
            new Label { Text = speaker, FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = (Color)res["Accent"] },
        };

        if (!string.IsNullOrEmpty(badge))
        {
            metaChildren.Add(new Border
            {
                Padding = new Thickness(6, 2),
                BackgroundColor = (Color)res["AccentWash"],
                Stroke = (Color)res["AccentLine"],
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(99) },
                Content = new Label { Text = badge, FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = (Color)res["Accent"] },
            });
        }

        metaChildren.Add(new Label { Text = time, FontFamily = "Consolas", FontSize = 10, TextColor = (Color)res["TextFaint"] });

        var meta = new HorizontalStackLayout
        {
            Spacing = 8,
        };
        foreach (var child in metaChildren)
        {
            meta.Children.Add(child);
        }

        var body = new Label
        {
            Text = text,
            FontSize = 14,
            LineHeight = 1.8,
            LineBreakMode = LineBreakMode.WordWrap,
            TextColor = (Color)res["TextPrimary"],
        };

        var content = new VerticalStackLayout
        {
            Spacing = 6,
            Children = { meta, body },
        };

        if (!string.IsNullOrEmpty(memoryReceipt))
        {
            content.Children.Add(new HorizontalStackLayout
            {
                Spacing = 6,
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(4, 9),
                BackgroundColor = (Color)res["PositiveWash"],
                Children =
                {
                    new Label { Text = "✓", TextColor = (Color)res["Positive"], FontSize = 11 },
                    new Label { Text = memoryReceipt, TextColor = (Color)res["Positive"], FontSize = 11 },
                    new Button { Text = "撤销", FontSize = 10, Padding = new Thickness(0), BackgroundColor = Colors.Transparent, BorderWidth = 0, TextColor = (Color)res["TextMuted"] },
                },
            });
        }

        var border = new Border
        {
            MaximumWidthRequest = 620,
            HorizontalOptions = LayoutOptions.Start,
            Padding = new Thickness(isProactive ? 15 : 0, 0, 0, 0),
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            Content = content,
        };

        if (isProactive)
        {
            border.Stroke = (Color)res["Accent"];
            border.StrokeThickness = 2;
            border.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0, 6, 6, 0) };
        }

        return border;
    }

    private static Button CreateSessionItem(string title, bool active, ResourceDictionary res)
    {
        return new Button
        {
            Text = title,
            HorizontalOptions = LayoutOptions.Fill,
            BackgroundColor = active ? (Color)res["AccentWash"] : Colors.Transparent,
            BorderColor = active ? (Color)res["AccentLine"] : Colors.Transparent,
            TextColor = active ? (Color)res["Accent"] : (Color)res["TextMuted"],
            BorderWidth = 1,
            CornerRadius = 6,
            FontSize = 11,
            Padding = new Thickness(8, 7),
        };
    }
}
