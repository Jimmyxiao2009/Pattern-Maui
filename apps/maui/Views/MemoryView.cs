using Microsoft.Maui.Controls.Shapes;

namespace Pattern.Maui.Views;

public static partial class MemoryView
{
    public static View Create()
    {
        var res = Application.Current!.Resources;

        // ========== Page Header ==========
        var eyebrow = new Label
        {
            Text = "长期记忆",
            Style = (Style)res["EyebrowLabel"],
        };

        var title = new Label
        {
            Text = "记得的事",
            Style = (Style)res["PageTitle"],
        };

        var subtitle = new Label
        {
            Text = "每条记忆都可查看来源、修改或撤销。",
            Style = (Style)res["PageSubtitle"],
        };

        var addButton = new Button
        {
            Text = "＋ 添加记忆",
            Style = (Style)res["PrimaryButton"],
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
                    Children = { eyebrow, title, subtitle },
                },
                addButton,
            },
        };
        Grid.SetColumn(addButton, 1);
        addButton.VerticalOptions = LayoutOptions.Start;

        // ========== Search Toolbar ==========
        var searchEntry = new Entry
        {
            Placeholder = "搜索记忆",
            BackgroundColor = Colors.Transparent,
            TextColor = (Color)res["TextPrimary"],
            PlaceholderColor = (Color)res["TextFaint"],
        };

        var filterAll = CreateFilterButton("全部", true, "6", res);
        var filterFact = CreateFilterButton("事实", false, null, res);
        var filterPref = CreateFilterButton("偏好", false, null, res);
        var filterEvent = CreateFilterButton("事件", false, null, res);
        var filterFeedback = CreateFilterButton("反馈", false, null, res);

        var searchToolbar = new HorizontalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(42, 12, 42, 12),
            Children =
            {
                new Border
                {
                    Style = (Style)res["SearchBox"],
                    Content = searchEntry,
                },
                filterAll,
                filterFact,
                filterPref,
                filterEvent,
                filterFeedback,
            },
        };

        // ========== Consolidation Notice ==========
        var consolidationCard = new Border
        {
            Margin = new Thickness(42, 0, 42, 14),
            Padding = new Thickness(13, 10),
            BackgroundColor = (Color)res["InfoWash"],
            Stroke = (Color)res["Info"],
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7) },
            Content = new HorizontalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    new Label { Text = "✦", FontSize = 14, TextColor = (Color)res["Info"], VerticalOptions = LayoutOptions.Center },
                    new VerticalStackLayout
                    {
                        Spacing = 2,
                        Children =
                        {
                            new Label { Text = "昨夜已固化", TextColor = (Color)res["TextPrimary"], FontSize = 13, FontAttributes = FontAttributes.Bold },
                            new Label { Text = "14 条对话流水整理为 3 条长期记忆，27 条低访问条目已衰减。", TextColor = (Color)res["TextMuted"], FontSize = 11 },
                        },
                    },
                    new Label { Text = "03:20", FontFamily = "Consolas", FontSize = 10, TextColor = (Color)res["TextFaint"], VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.End },
                },
            },
        };

        // ========== Memory Grid ==========
        var memoryGrid = new Grid
        {
            Padding = new Thickness(42, 0, 42, 20),
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star),
                new(GridLength.Star),
                new(GridLength.Star),
            },
            ColumnSpacing = 12,
            RowSpacing = 12,
        };

        // Sample memory cards matching old design (3 columns)
        var cards = new[]
        {
            CreateMemoryCard("事件", "2026-07 搬家拉伤右臂，一周内避免提重物；已提醒冷敷和拉伸。", "● ● ●", false, "昨天 · 访问 3 · 来源 #c412", res),
            CreateMemoryCard("事实", "养了一只黄眼睛的黑猫，晚上睡床头；猫粮放阳台储物柜第二层。", "● ● ○", false, "2026-03 · 访问 41 · 来源 #c208", res),
            CreateMemoryCard("反馈", "深夜主动提醒有效，但同一件事当晚最多催一次。", "● ● ●", false, "2026-05 · 访问 18 · 政策已应用", res),
            CreateMemoryCard("偏好", "写作时不要打断；连续工作超过 40 分钟才允许提醒。", "● ○ ○", false, "2026-04 · 访问 26", res),
            CreateMemoryCard("事实", "小说更新日是周三和周日；粉丝群服务运行在 fanwork-api。", "● ● ○", false, "2026-02 · 访问 57", res),
            CreateMemoryCard("事实 · 已取代", "住在城中村老房子，楼道没有灯。", "", true, "已被「2026-07 搬家」取代", res),
        };

        for (int i = 0; i < cards.Length; i++)
        {
            memoryGrid.Children.Add(cards[i]);
            Grid.SetColumn(cards[i], i % 3);
            Grid.SetRow(cards[i], i / 3);
        }

        // ========== Empty State ==========
        var emptyState = new Label
        {
            Text = "没有找到匹配的记忆",
            TextColor = (Color)res["TextFaint"],
            HorizontalOptions = LayoutOptions.Center,
            Padding = new Thickness(0, 70),
            IsVisible = false,
        };

        // ========== Scrollable Content ==========
        var contentStack = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                header,
                searchToolbar,
                consolidationCard,
                memoryGrid,
                emptyState,
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

    private static Button CreateFilterButton(string text, bool active, string? count, ResourceDictionary res)
    {
        var displayText = count != null ? $"{text} {count}" : text;
        return new Button
        {
            Text = displayText,
            FontSize = 11,
            Padding = new Thickness(10, 5),
            CornerRadius = 6,
            BackgroundColor = active ? (Color)res["AccentWash"] : Colors.Transparent,
            BorderColor = active ? (Color)res["AccentLine"] : (Color)res["Line"],
            TextColor = active ? (Color)res["Accent"] : (Color)res["TextMuted"],
            BorderWidth = 1,
        };
    }

    private static Border CreateMemoryCard(string category, string text, string importance, bool expired, string footer, ResourceDictionary res)
    {
        var categoryColors = new Dictionary<string, (Color TextColor, Color WashColor, Color LineColor)>(StringComparer.OrdinalIgnoreCase)
        {
            ["事实"] = ((Color)res["Accent"], (Color)res["AccentWash"], (Color)res["AccentLine"]),
            ["偏好"] = ((Color)res["TextMuted"], (Color)res["SurfaceBackground"], (Color)res["Line"]),
            ["事件"] = ((Color)res["Info"], (Color)res["InfoWash"], (Color)res["Info"]),
            ["反馈"] = ((Color)res["Positive"], (Color)res["PositiveWash"], (Color)res["Positive"]),
        };

        var (catColor, catWash, catLine) = categoryColors.GetValueOrDefault(category, ((Color)res["Accent"], (Color)res["AccentWash"], (Color)res["AccentLine"]));

        var badge = new Border
        {
            Padding = new Thickness(6, 3),
            BackgroundColor = catWash,
            Stroke = catLine,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(99) },
            Content = new Label
            {
                Text = category,
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                TextColor = catColor,
            },
        };

        var importanceLabel = new Label
        {
            Text = importance,
            FontSize = 9,
            TextColor = (Color)res["Accent"],
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star),
                new(GridLength.Auto),
            },
            Children =
            {
                badge,
                importanceLabel,
            },
        };
        Grid.SetColumn(importanceLabel, 1);

        var body = new Label
        {
            Text = text,
            TextColor = (Color)res["TextPrimary"],
            FontSize = 13,
            LineHeight = 1.65,
            LineBreakMode = LineBreakMode.WordWrap,
        };

        var divider = new BoxView
        {
            HeightRequest = 1,
            Color = (Color)res["Line"],
        };

        var footerLabel = new Label
        {
            Text = footer,
            TextColor = (Color)res["TextFaint"],
            FontFamily = "Consolas",
            FontSize = 10,
            LineBreakMode = LineBreakMode.TailTruncation,
        };

        var cardContent = new VerticalStackLayout
        {
            Spacing = 9,
            Children =
            {
                header,
                body,
                divider,
                footerLabel,
            },
        };

        return new Border
        {
            WidthRequest = 280,
            MinimumHeightRequest = 150,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(15),
            BackgroundColor = (Color)res["PanelBackground"],
            Stroke = expired ? (Color)res["Line"] : (Color)res["Line"],
            StrokeThickness = 1,
            Opacity = expired ? 0.55 : 1.0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7) },
            Content = cardContent,
        };
    }
}
