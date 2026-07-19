using Microsoft.Maui.Controls.Shapes;

namespace Pattern.Maui.Views;

public static partial class ConversationsView
{
    public static View Create()
    {
        var res = Application.Current!.Resources;

        // ========== Page Header ==========
        var eyebrow = new Label
        {
            Text = "工作区",
            Style = (Style)res["EyebrowLabel"],
        };

        var title = new Label
        {
            Text = "对话管理",
            Style = (Style)res["PageTitle"],
        };

        var subtitle = new Label
        {
            Text = "全局与项目对话分开保存；归档不会删除本地记录。",
            Style = (Style)res["PageSubtitle"],
        };

        var archiveButton = new Button
        {
            Text = "查看归档",
            Style = (Style)res["QuietButton"],
        };

        var newChatButton = new Button
        {
            Text = "＋ 新对话",
            Style = (Style)res["PrimaryButton"],
        };

        var headerRight = new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { archiveButton, newChatButton },
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

        // ========== Conversation List ==========
        var conversationList = new VerticalStackLayout
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
                    Text = "⌁",
                    FontSize = 32,
                    TextColor = (Color)res["TextFaint"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Label
                {
                    Text = "没有当前对话",
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = (Color)res["TextPrimary"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Label
                {
                    Text = "新建一个对话，开始独立的上下文。",
                    FontSize = 11,
                    TextColor = (Color)res["TextMuted"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Button
                {
                    Text = "＋ 新对话",
                    Style = (Style)res["PrimaryButton"],
                    HorizontalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 8, 0, 0),
                },
            },
        };
        conversationList.Children.Add(emptyState);

        var contentStack = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                header,
                conversationList,
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
}
