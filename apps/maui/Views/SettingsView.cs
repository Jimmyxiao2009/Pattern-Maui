using Microsoft.Maui.Controls.Shapes;

namespace Pattern.Maui.Views;

public static partial class SettingsView
{
    public static View Create()
    {
        var res = Application.Current!.Resources;

        // Match the old SettingsView groups, including the operational tabs that
        // are easy to lose when the page is reduced to a simple preferences form.
        var tabDefinitions = new[]
        {
            ("general", "常规"),
            ("persona", "人格与角色"),
            ("model", "模型"),
            ("proactive", "主动能力"),
            ("filewatch", "文件感知"),
            ("journal", "执行日志"),
            ("privacy", "隐私与权限"),
            ("shortcuts", "快捷键"),
        };

        var tabButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
        var tabList = new VerticalStackLayout { Spacing = 3 };
        var tabHost = new ContentView { HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Start };

        void SelectTab(string id)
        {
            tabHost.Content = new Border
            {
                Padding = new Thickness(18, 16),
                BackgroundColor = (Color)res["PanelBackground"],
                Stroke = (Color)res["Line"],
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
                Content = new ScrollView { Content = CreateTabContent(id, res) },
            };

            foreach (var (key, button) in tabButtons)
            {
                button.BackgroundColor = key == id ? (Color)res["AccentWash"] : Colors.Transparent;
                button.TextColor = key == id ? (Color)res["Accent"] : (Color)res["TextMuted"];
            }
        }

        foreach (var (id, label) in tabDefinitions)
        {
            var tab = new Button
            {
                Text = label,
                HorizontalOptions = LayoutOptions.Fill,
                FontSize = 11,
                Padding = new Thickness(10, 8),
                BackgroundColor = Colors.Transparent,
                TextColor = (Color)res["TextMuted"],
                BorderWidth = 0,
                CornerRadius = 6,
            };
            tab.Clicked += (_, _) => SelectTab(id);
            tabList.Children.Add(tab);
            tabButtons[id] = tab;
        }

        var settingsLayout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(new GridLength(180)),
                new(GridLength.Star),
            },
            ColumnSpacing = 32,
            Padding = new Thickness(42, 8, 42, 38),
            BackgroundColor = (Color)res["SurfaceBackground"],
            Children =
            {
                new ScrollView { Content = tabList },
                tabHost,
            },
        };
        Grid.SetColumn(tabHost, 1);

        SelectTab("general");
        return settingsLayout;
    }

    private static View CreateTabContent(string id, ResourceDictionary res)
    {
        return id switch
        {
            "general" => CreateGeneralTab(res),
            "persona" => CreatePersonaTab(res),
            "model" => CreateModelTab(res),
            "proactive" => CreateProactiveTab(res),
            "filewatch" => CreateFilewatchTab(res),
            "journal" => CreateJournalTab(res),
            "privacy" => CreatePrivacyTab(res),
            "shortcuts" => CreateShortcutsTab(res),
            _ => new Label { Text = "未知设置页" },
        };
    }

    // ========== General Tab ==========
    private static View CreateGeneralTab(ResourceDictionary res)
    {
        var themeToggle = CreateSettingRow(
            "主题",
            "切换界面的明暗外观",
            new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = CreateSegmentedButton(new[] { "夜幕", "晨光", "海湾", "森林", "纸张" }, 0, res) },
            res);

        var autostartToggle = CreateSettingRow(
            "开机启动",
            "登录系统后在托盘静默启动",
            new Switch
            {
                IsToggled = false,
                OnColor = (Color)res["Accent"],
                ThumbColor = (Color)res["TextPrimary"],
            },
            res);

        var proactiveToggle = CreateSettingRow(
            "主动开口",
            "允许 Pattern 根据时间与事件主动联系你",
            new Switch
            {
                IsToggled = true,
                OnColor = (Color)res["Accent"],
                ThumbColor = (Color)res["TextPrimary"],
            },
            res);

        var proactivePause = CreateSettingRow(
            "暂时暂停",
            "暂停所有主动消息，直到手动恢复",
            new Switch
            {
                IsToggled = false,
                OnColor = (Color)res["Accent"],
                ThumbColor = (Color)res["TextPrimary"],
            },
            res);

        return new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                new Label { Text = "外观", TextColor = (Color)res["TextMuted"], FontSize = 12, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 0, 0, 6) },
                themeToggle,
                new Label { Text = "常驻行为", TextColor = (Color)res["TextMuted"], FontSize = 12, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 16, 0, 6) },
                proactiveToggle,
                proactivePause,
                autostartToggle,
            },
        };
    }

    // ========== Persona Tab ==========
    private static View CreatePersonaTab(ResourceDictionary res)
    {
        var personaEditor = new Editor
        {
            Placeholder = "描述 Pattern 的人格和角色…",
            AutoSize = EditorAutoSizeOption.TextChanges,
            MinimumHeightRequest = 120,
            BackgroundColor = (Color)res["SurfaceBackground"],
            TextColor = (Color)res["TextPrimary"],
            PlaceholderColor = (Color)res["TextFaint"],
        };

        var saveButton = new Button
        {
            Text = "保存人格",
            Style = (Style)res["PrimaryButton"],
            HorizontalOptions = LayoutOptions.Start,
        };

        return new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new Label { Text = "人格与角色", TextColor = (Color)res["TextMuted"], FontSize = 12, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 0, 0, 6) },
                new Label { Text = "没有模板。你写下什么，Pattern 就按什么方式陪伴和执行。", TextColor = (Color)res["TextMuted"], FontSize = 11 },
                personaEditor,
                saveButton,
            },
        };
    }

    // ========== Model Tab ==========
    private static View CreateModelTab(ResourceDictionary res)
    {
        var providerEntry = new Entry
        {
            Placeholder = "Provider (例如 openai-compatible)",
            BackgroundColor = (Color)res["SurfaceBackground"],
            TextColor = (Color)res["TextPrimary"],
            PlaceholderColor = (Color)res["TextFaint"],
        };

        var endpointEntry = new Entry
        {
            Placeholder = "模型 Endpoint URL",
            BackgroundColor = (Color)res["SurfaceBackground"],
            TextColor = (Color)res["TextPrimary"],
            PlaceholderColor = (Color)res["TextFaint"],
        };

        var modelEntry = new Entry
        {
            Placeholder = "模型名称 (例如 gpt-4o)",
            BackgroundColor = (Color)res["SurfaceBackground"],
            TextColor = (Color)res["TextPrimary"],
            PlaceholderColor = (Color)res["TextFaint"],
        };

        var keyEntry = new Entry
        {
            Placeholder = "API Key（安全存储）",
            IsPassword = true,
            BackgroundColor = (Color)res["SurfaceBackground"],
            TextColor = (Color)res["TextPrimary"],
            PlaceholderColor = (Color)res["TextFaint"],
        };

        var saveButton = new Button
        {
            Text = "保存并应用",
            Style = (Style)res["PrimaryButton"],
            HorizontalOptions = LayoutOptions.Start,
        };

        return new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                new Label { Text = "模型连接", TextColor = (Color)res["TextMuted"], FontSize = 12, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 0, 0, 6) },
                new Label { Text = "配置主 Agent 使用的模型服务。", TextColor = (Color)res["TextMuted"], FontSize = 11 },
                providerEntry,
                endpointEntry,
                modelEntry,
                keyEntry,
                saveButton,
            },
        };
    }

    // ========== Privacy Tab ==========
    private static View CreatePrivacyTab(ResourceDictionary res)
    {
        var uiaToggle = CreateSettingRow(
            "桌面自动化",
            "允许 Pattern 使用 Windows UI Automation 读取控件",
            new Switch
            {
                IsToggled = true,
                OnColor = (Color)res["Accent"],
                ThumbColor = (Color)res["TextPrimary"],
            },
            res);

        var notifyToggle = CreateSettingRow(
            "系统通知",
            "接收主动消息、日程提醒和任务完成回执",
            new Switch
            {
                IsToggled = true,
                OnColor = (Color)res["Accent"],
                ThumbColor = (Color)res["TextPrimary"],
            },
            res);

        var auditButton = new Button
        {
            Text = "查看审计日志",
            Style = (Style)res["QuietButton"],
            HorizontalOptions = LayoutOptions.Start,
        };

        return new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                new Label { Text = "隐私与权限", TextColor = (Color)res["TextMuted"], FontSize = 12, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 0, 0, 6) },
                new Label { Text = "控制 Pattern 可以访问的系统功能和数据。", TextColor = (Color)res["TextMuted"], FontSize = 11 },
                uiaToggle,
                notifyToggle,
                new Label { Text = "审计", TextColor = (Color)res["TextMuted"], FontSize = 12, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 16, 0, 6) },
                auditButton,
            },
        };
    }

    private static View CreateProactiveTab(ResourceDictionary res)
    {
        return new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                new Label { Text = "主动能力", TextColor = (Color)res["TextMuted"], FontSize = 12, FontAttributes = FontAttributes.Bold },
                new Label { Text = "控制 AI 是否主动联系你；每日提醒和循环任务仍在任务页。", TextColor = (Color)res["TextMuted"], FontSize = 11, Margin = new Thickness(0, 4, 0, 8) },
                CreateSettingRow("启用 AI 主动", "关闭后不再新建主动关心链", new Switch { IsToggled = true, OnColor = (Color)res["Accent"], ThumbColor = (Color)res["TextPrimary"] }, res),
                CreateSettingRow("暂时暂停", "临时停发，托盘也可切换", new Switch { IsToggled = false, OnColor = (Color)res["Accent"], ThumbColor = (Color)res["TextPrimary"] }, res),
                CreateSettingRow("安静时间", "之后 AI 更少打扰", new Label { Text = "23:00", TextColor = (Color)res["TextMuted"], FontSize = 12 }, res),
                new Button { Text = "打开主动页", Style = (Style)res["QuietButton"], HorizontalOptions = LayoutOptions.Start },
            },
        };
    }

    private static View CreateFilewatchTab(ResourceDictionary res)
    {
        return new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                new Label { Text = "文件感知", TextColor = (Color)res["TextMuted"], FontSize = 12, FontAttributes = FontAttributes.Bold },
                new Label { Text = "只监听明确配置的工作区和扩展名，不会上传未授权文件。", TextColor = (Color)res["TextMuted"], FontSize = 11 },
                CreateSettingRow("文件监控", "监听工作区中的变更并生成可审计事件", new Switch { IsToggled = false, OnColor = (Color)res["Accent"], ThumbColor = (Color)res["TextPrimary"] }, res),
                new Label { Text = "扩展名：.md · .txt · .json · .ts · .js · .svelte · .cs", FontFamily = "Consolas", TextColor = (Color)res["TextFaint"], FontSize = 10 },
                new Button { Text = "查看文件监控页", Style = (Style)res["QuietButton"], HorizontalOptions = LayoutOptions.Start },
            },
        };
    }

    private static View CreateJournalTab(ResourceDictionary res)
    {
        return new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                new Label { Text = "执行日志", TextColor = (Color)res["TextMuted"], FontSize = 12, FontAttributes = FontAttributes.Bold },
                new Label { Text = "模型调用、权限判断、任务和恢复操作都会保留结构化回执。", TextColor = (Color)res["TextMuted"], FontSize = 11 },
                new Border { Padding = new Thickness(12, 10), BackgroundColor = (Color)res["SurfaceBackground"], Stroke = (Color)res["Line"], StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) }, Content = new Label { Text = "等待运行时事件…\n\n这里会显示最近的执行阶段、风险等级和结果回执。", TextColor = (Color)res["TextFaint"], FontFamily = "Consolas", FontSize = 10 } },
                new Button { Text = "读取完整执行日志", Style = (Style)res["QuietButton"], HorizontalOptions = LayoutOptions.Start },
            },
        };
    }

    // ========== Shortcuts Tab ==========
    private static View CreateShortcutsTab(ResourceDictionary res)
    {
        var shortcutItems = new VerticalStackLayout
        {
            Spacing = 10,
        };

        shortcutItems.Children.Add(CreateShortcutRow("打开快捷对话", "Ctrl + Alt + P", res));
        shortcutItems.Children.Add(CreateShortcutRow("新建对话", "Ctrl + N", res));
        shortcutItems.Children.Add(CreateShortcutRow("发送消息", "Enter", res));
        shortcutItems.Children.Add(CreateShortcutRow("换行", "Shift + Enter", res));

        return new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new Label { Text = "快捷键", TextColor = (Color)res["TextMuted"], FontSize = 12, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 0, 0, 6) },
                new Label { Text = "Pattern 支持的键盘快捷键。", TextColor = (Color)res["TextMuted"], FontSize = 11 },
                shortcutItems,
            },
        };
    }

    // ========== Helper Methods ==========

    private static Grid CreateSettingRow(string title, string description, View control, ResourceDictionary res)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star),
                new(GridLength.Auto),
            },
            Padding = new Thickness(10, 10),
            BackgroundColor = Colors.Transparent,
        };

        var textStack = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = title, TextColor = (Color)res["TextPrimary"], FontSize = 13, FontAttributes = FontAttributes.Bold },
                new Label { Text = description, TextColor = (Color)res["TextMuted"], FontSize = 11 },
            },
        };

        grid.Children.Add(textStack);
        grid.Children.Add(control);
        Grid.SetColumn(control, 1);

        return grid;
    }

    private static View CreateSegmentedButton(string[] options, int selectedIndex, ResourceDictionary res)
    {
        var layout = new HorizontalStackLayout
        {
            Spacing = 0,
        };

        for (int i = 0; i < options.Length; i++)
        {
            var isSelected = i == selectedIndex;
            var button = new Button
            {
                Text = options[i],
                FontSize = 11,
                Padding = new Thickness(12, 5),
                CornerRadius = i == 0 ? 6 : i == options.Length - 1 ? 6 : 0,
                BackgroundColor = isSelected ? (Color)res["Accent"] : (Color)res["SurfaceBackground"],
                TextColor = isSelected ? (Color)res["PageBackground"] : (Color)res["TextMuted"],
                BorderColor = isSelected ? (Color)res["Accent"] : (Color)res["LineStrong"],
                BorderWidth = 1,
            };
            layout.Children.Add(button);
        }

        return layout;
    }

    private static Grid CreateShortcutRow(string action, string shortcut, ResourceDictionary res)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star),
                new(GridLength.Auto),
            },
            Padding = new Thickness(12, 8),
        };

        var actionLabel = new Label
        {
            Text = action,
            TextColor = (Color)res["TextPrimary"],
            FontSize = 13,
            VerticalOptions = LayoutOptions.Center,
        };

        var shortcutBorder = new Border
        {
            Padding = new Thickness(8, 4),
            BackgroundColor = (Color)res["SurfaceBackground"],
            Stroke = (Color)res["LineStrong"],
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4) },
            Content = new Label
            {
                Text = shortcut,
                FontFamily = "Consolas",
                FontSize = 11,
                TextColor = (Color)res["TextMuted"],
            },
        };

        grid.Children.Add(actionLabel);
        grid.Children.Add(shortcutBorder);
        Grid.SetColumn(shortcutBorder, 1);

        return grid;
    }
}
