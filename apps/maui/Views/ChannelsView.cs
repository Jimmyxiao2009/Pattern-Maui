using Microsoft.Maui.Controls.Shapes;

namespace Pattern.Maui.Views;

public static partial class ChannelsView
{
    public static View Create()
    {
        var res = Application.Current!.Resources;

        // ========== Page Header ==========
        var eyebrow = new Label
        {
            Text = "消息中继",
            Style = (Style)res["EyebrowLabel"],
        };

        var title = new Label
        {
            Text = "通道",
            Style = (Style)res["PageTitle"],
        };

        var subtitle = new Label
        {
            Text = "在电脑之外，也能收到消息和下达任务。",
            Style = (Style)res["PageSubtitle"],
        };

        var addButton = new Button
        {
            Text = "＋ 添加通道",
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

        // ========== Channel List ==========
        var channelList = new VerticalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(42, 8, 42, 20),
        };

        // System notifications channel
        channelList.Children.Add(CreateChannelRow(
            "▣",
            "系统通知",
            "本机主动提醒与任务结果",
            true,
            true,
            res));

        // Pattern Mobile channel
        channelList.Children.Add(CreateChannelRow(
            "⌁",
            "Pattern Mobile",
            "WebDAV 端到端加密中继 · 上次同步 12 秒前",
            true,
            true,
            res));

        // Telegram channel
        channelList.Children.Add(CreateChannelRow(
            "↗",
            "Telegram",
            "备用远程消息通道",
            false,
            false,
            res));

        // Email channel
        channelList.Children.Add(CreateChannelRow(
            "＠",
            "邮件",
            "日报与低频正式消息",
            false,
            false,
            res));

        var contentStack = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                header,
                channelList,
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

    private static Border CreateChannelRow(string iconGlyph, string name, string description, bool enabled, bool isOnline, ResourceDictionary res)
    {
        // Channel icon
        var iconBorder = new Border
        {
            WidthRequest = 38,
            HeightRequest = 38,
            BackgroundColor = (Color)res["ElevatedBackground"],
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7) },
            Content = new Label
            {
                Text = iconGlyph,
                FontSize = 18,
                TextColor = (Color)res["Accent"],
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            },
        };

        var nameLabel = new Label
        {
            Text = name,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = (Color)res["TextPrimary"],
        };

        var descLabel = new Label
        {
            Text = description,
            TextColor = (Color)res["TextMuted"],
            FontSize = 11,
        };

        var statusLabel = new Label
        {
            Text = isOnline ? "已连接" : "未配置",
            FontSize = 11,
            TextColor = isOnline ? (Color)res["Positive"] : (Color)res["TextFaint"],
        };

        var toggleSwitch = new Switch
        {
            IsToggled = enabled,
            OnColor = (Color)res["Accent"],
            ThumbColor = (Color)res["TextPrimary"],
        };

        var infoStack = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                nameLabel,
                descLabel,
            },
        };

        var leftRow = new HorizontalStackLayout
        {
            Spacing = 14,
            Children =
            {
                iconBorder,
                infoStack,
            },
        };

        var rightRow = new HorizontalStackLayout
        {
            Spacing = 12,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                statusLabel,
                toggleSwitch,
            },
        };
        if (!string.Equals(name, "系统通知", StringComparison.Ordinal))
        {
            rightRow.Children.Add(new Button
            {
                Text = "配置",
                Style = (Style)res["QuietButton"],
                FontSize = 10,
                Padding = new Thickness(8, 4),
            });
        }

        var cardContent = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star),
                new(GridLength.Auto),
            },
            ColumnSpacing = 16,
            Padding = new Thickness(14, 16),
            Children =
            {
                leftRow,
                rightRow,
            },
        };
        Grid.SetColumn(rightRow, 1);

        return new Border
        {
            Stroke = (Color)res["Line"],
            StrokeThickness = 1,
            BackgroundColor = (Color)res["SurfaceBackground"],
            Content = cardContent,
        };
    }
}
