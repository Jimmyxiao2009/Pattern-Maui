using Microsoft.Maui.Controls.Shapes;

namespace Pattern.Maui.Views;

public static partial class ProactiveView
{
    public static View Create()
    {
        var res = Application.Current!.Resources;

        // ========== Page Header ==========
        var eyebrow = new Label
        {
            Text = "AI 主动关心",
            Style = (Style)res["EyebrowLabel"],
        };

        var title = new Label
        {
            Text = "主动",
            Style = (Style)res["PageTitle"],
        };

        var subtitle = new Label
        {
            Text = "只管理「AI 会不会主动找你」。每天几点发提醒，请到「任务」页。",
            Style = (Style)res["PageSubtitle"],
        };

        var refreshButton = new Button
        {
            Text = "⟳ 刷新",
            Style = (Style)res["QuietButton"],
        };

        var runNowButton = new Button
        {
            Text = "✦ 现在关心一次",
            Style = (Style)res["PrimaryButton"],
        };

        var backButton = new Button
        {
            Text = "回到对话",
            Style = (Style)res["QuietButton"],
        };

        var headerRight = new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { backButton, refreshButton, runNowButton },
        };
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star),
                new(GridLength.Auto),
            },
            Padding = new Thickness(42, 26, 42, 4),
        };
        header.Children.Add(new VerticalStackLayout
        {
            Spacing = 3,
            Children = { eyebrow, title, subtitle },
        });
        header.Children.Add(headerRight);
        Grid.SetColumn(headerRight, 1);
        headerRight.VerticalOptions = LayoutOptions.Start;

        // ========== Scope Map ==========
        var scopeMap = new Grid
        {
            Padding = new Thickness(42, 12, 42, 12),
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star),
                new(GridLength.Star),
            },
            ColumnSpacing = 12,
        };

        var scopeCard1 = CreateScopeCard("本页 · AI 主动", "开关、安静时间、主动链、AI 发过的消息", res);
        scopeMap.Children.Add(scopeCard1);
        Grid.SetColumn(scopeCard1, 0);
        var scopeCard2 = CreateScopeCard("对话内提醒 / 定时", "每日 HH:MM 系统提醒、循环任务、定时执行", res);
        scopeMap.Children.Add(scopeCard2);
        Grid.SetColumn(scopeCard2, 1);

        // ========== Overview Cards ==========
        var overview = new Grid
        {
            Padding = new Thickness(42, 0, 42, 12),
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star),
                new(GridLength.Star),
                new(GridLength.Star),
            },
            ColumnSpacing = 12,
        };

        var ovCard1 = CreateOverviewCard("运行中", "主动引擎", res["Positive"] as Color, res);
        overview.Children.Add(ovCard1);
        Grid.SetColumn(ovCard1, 0);
        var ovCard2 = CreateOverviewCard("0", "条活跃主动链", res["Info"] as Color, res);
        overview.Children.Add(ovCard2);
        Grid.SetColumn(ovCard2, 1);
        var ovCard3 = CreateOverviewCard("0", "条每日提醒", res["Accent"] as Color, res);
        overview.Children.Add(ovCard3);
        Grid.SetColumn(ovCard3, 2);

        // ========== Engine Settings Card ==========
        var engineCard = CreateEngineCard(res);

        // ========== Chain List ==========
        var chainSection = new VerticalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(42, 12, 42, 12),
            Children =
            {
                new Label
                {
                    Text = "AI 主动链",
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = (Color)res["TextPrimary"],
                },
                new Label
                {
                    Text = "模型决定要不要说、何时再说。可手动跑一次或取消。",
                    FontSize = 11,
                    TextColor = (Color)res["TextMuted"],
                },
            },
        };

        // Empty state for chains
        var chainEmptyState = new VerticalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            Padding = new Thickness(0, 30),
            Children =
            {
                new Label
                {
                    Text = "✦",
                    FontSize = 24,
                    TextColor = (Color)res["TextFaint"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Label
                {
                    Text = "还没有主动链",
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = (Color)res["TextPrimary"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Label
                {
                    Text = "引擎开启后，AI 会在合适时机自己建链。也可点「现在关心一次」试跑。",
                    FontSize = 11,
                    TextColor = (Color)res["TextMuted"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Button
                {
                    Text = "✦ 现在关心一次",
                    Style = (Style)res["PrimaryButton"],
                    HorizontalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 8, 0, 0),
                },
            },
        };
        chainSection.Children.Add(chainEmptyState);

        var contentStack = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                header,
                scopeMap,
                overview,
                engineCard,
                chainSection,
            },
        };

        return new ScrollView
        {
            Content = new Grid
            {
                BackgroundColor = (Color)res["SurfaceBackground"],
                Children = { contentStack },
            },
        };
    }

    private static Border CreateScopeCard(string title, string description, ResourceDictionary res)
    {
        return new Border
        {
            BackgroundColor = (Color)res["PanelBackground"],
            Stroke = (Color)res["Line"],
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7) },
            Padding = new Thickness(14, 11),
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Label
                    {
                        Text = title,
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = (Color)res["TextPrimary"],
                    },
                    new Label
                    {
                        Text = description,
                        FontSize = 11,
                        TextColor = (Color)res["TextMuted"],
                    },
                },
            },
        };
    }

    private static Border CreateOverviewCard(string count, string label, Color? accentColor, ResourceDictionary res)
    {
        return new Border
        {
            BackgroundColor = (Color)res["PanelBackground"],
            Stroke = (Color)res["Line"],
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7) },
            Padding = new Thickness(14, 11),
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Label
                    {
                        Text = count,
                        FontSize = 20,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = accentColor ?? (Color)res["TextPrimary"],
                    },
                    new Label
                    {
                        Text = label,
                        FontSize = 11,
                        TextColor = (Color)res["TextMuted"],
                    },
                },
            },
        };
    }

    private static Border CreateEngineCard(ResourceDictionary res)
    {
        var title = new Label
        {
            Text = "引擎",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = (Color)res["TextPrimary"],
        };

        var desc = new Label
        {
            Text = "控制 AI 会不会自己找你说话。不影响已经创建的系统提醒。",
            FontSize = 11,
            TextColor = (Color)res["TextMuted"],
        };

        var enableToggle = CreateSettingRow("启用 AI 主动", "关闭后不再新建 AI 关心链", new Switch
        {
            IsToggled = true,
            OnColor = (Color)res["Accent"],
            ThumbColor = (Color)res["TextPrimary"],
        }, res);

        var pauseToggle = CreateSettingRow("暂停", "临时停发；托盘也可切换", new Switch
        {
            IsToggled = false,
            OnColor = (Color)res["Accent"],
            ThumbColor = (Color)res["TextPrimary"],
        }, res);

        var quietTime = CreateSettingRow("安静时间", "之后 AI 更少打扰", new Label
        {
            Text = "23:00",
            FontSize = 12,
            TextColor = (Color)res["TextMuted"],
        }, res);

        var saveButton = new Button
        {
            Text = "保存",
            Style = (Style)res["PrimaryButton"],
        };

        var runNowButton = new Button
        {
            Text = "✦ 现在关心一次",
            Style = (Style)res["QuietButton"],
        };

        var actions = new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { saveButton, runNowButton },
        };

        return new Border
        {
            Margin = new Thickness(42, 0, 42, 12),
            BackgroundColor = (Color)res["PanelBackground"],
            Stroke = (Color)res["Line"],
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7) },
            Padding = new Thickness(18, 16),
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    new VerticalStackLayout
                    {
                        Spacing = 4,
                        Children = { title, desc },
                    },
                    enableToggle,
                    pauseToggle,
                    quietTime,
                    actions,
                },
            },
        };
    }

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
}
