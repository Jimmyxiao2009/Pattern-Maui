using Microsoft.Maui.Controls.Shapes;

namespace Pattern.Maui.Views;

public static partial class GoalsView
{
    public static View Create()
    {
        var res = Application.Current!.Resources;

        // ========== Page Header ==========
        var eyebrow = new Label
        {
            Text = "Goal",
            Style = (Style)res["EyebrowLabel"],
        };

        var title = new Label
        {
            Text = "目标",
            Style = (Style)res["PageTitle"],
        };

        var subtitle = new Label
        {
            Text = "跨回合的 run-un-done 目标。当前对话里的待办清单请用 /plan，会出现在聊天输入框上方。",
            Style = (Style)res["PageSubtitle"],
        };

        var refreshButton = new Button
        {
            Text = "⟳ 刷新",
            Style = (Style)res["QuietButton"],
        };

        var newGoalButton = new Button
        {
            Text = "＋ 新建目标",
            Style = (Style)res["PrimaryButton"],
        };

        var headerRight = new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { refreshButton, newGoalButton },
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

        // ========== Overview Cards ==========
        var overview = new Grid
        {
            Padding = new Thickness(42, 12, 42, 12),
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star),
                new(GridLength.Star),
                new(GridLength.Star),
            },
            ColumnSpacing = 12,
        };

        var card1 = CreateOverviewCard("0", "进行中 / 暂停", res["Accent"] as Color, res);
        overview.Children.Add(card1);
        Grid.SetColumn(card1, 0);
        var card2 = CreateOverviewCard("0", "阻塞", res["Info"] as Color, res);
        overview.Children.Add(card2);
        Grid.SetColumn(card2, 1);
        var card3 = CreateOverviewCard("0", "已完成", res["Positive"] as Color, res);
        overview.Children.Add(card3);
        Grid.SetColumn(card3, 2);

        // ========== Filter Tabs ==========
        var filterTabs = new HorizontalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(42, 0, 42, 12),
        };

        var currentTab = CreateFilterTab("当前", true, res);
        var allTab = CreateFilterTab("全部", false, res);
        filterTabs.Children.Add(currentTab);
        filterTabs.Children.Add(allTab);

        // ========== Goals List ==========
        var goalsList = new VerticalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(42, 8, 42, 20),
        };

        // Empty state
        var emptyState = new VerticalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            Padding = new Thickness(0, 40),
            Children =
            {
                new Label
                {
                    Text = "◎",
                    FontSize = 32,
                    TextColor = (Color)res["TextFaint"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Label
                {
                    Text = "当前没有进行中的目标",
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = (Color)res["TextPrimary"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Label
                {
                    Text = "用「新建目标」或聊天里的 /goal 设定可验证目标。会话内的分步待办请用 /plan。",
                    FontSize = 11,
                    TextColor = (Color)res["TextMuted"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Button
                {
                    Text = "＋ 新建目标",
                    Style = (Style)res["PrimaryButton"],
                    HorizontalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 8, 0, 0),
                },
            },
        };
        goalsList.Children.Add(emptyState);

        var contentStack = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                header,
                overview,
                filterTabs,
                goalsList,
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
                        FontSize = 24,
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

    private static Button CreateFilterTab(string text, bool active, ResourceDictionary res)
    {
        return new Button
        {
            Text = text,
            FontSize = 11,
            Padding = new Thickness(10, 5),
            CornerRadius = 6,
            BackgroundColor = active ? (Color)res["AccentWash"] : Colors.Transparent,
            BorderColor = active ? (Color)res["AccentLine"] : (Color)res["Line"],
            TextColor = active ? (Color)res["Accent"] : (Color)res["TextMuted"],
            BorderWidth = 1,
        };
    }
}
