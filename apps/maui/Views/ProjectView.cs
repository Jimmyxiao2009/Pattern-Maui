using Microsoft.Maui.Controls.Shapes;

namespace Pattern.Maui.Views;

public static partial class ProjectView
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
            Text = "项目",
            Style = (Style)res["PageTitle"],
        };

        var subtitle = new Label
        {
            Text = "创建项目工作区，即可在左侧对话、中间聊天、右侧浏览文件夹。",
            Style = (Style)res["PageSubtitle"],
        };

        var newProjectButton = new Button
        {
            Text = "＋ 新建项目",
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
                newProjectButton,
            },
        };
        Grid.SetColumn(newProjectButton, 1);
        newProjectButton.VerticalOptions = LayoutOptions.Start;

        var projectList = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(42, 8, 42, 20) };
        var emptyProjectButton = new Button
        {
            Text = "＋ 新建项目",
            Style = (Style)res["PrimaryButton"],
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };

        // ========== Empty State ==========
        var emptyState = new VerticalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            Padding = new Thickness(0, 60),
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
                    Text = "还没有项目",
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = (Color)res["TextPrimary"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Label
                {
                    Text = "创建一个项目工作区，即可在左侧对话、中间聊天、右侧浏览文件夹。",
                    FontSize = 11,
                    TextColor = (Color)res["TextMuted"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                emptyProjectButton,
            },
        };
        projectList.Children.Add(emptyState);

        async Task CreateProjectAsync()
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null) return;
            var name = await page.DisplayPromptAsync("新建项目", "项目名称");
            if (string.IsNullOrWhiteSpace(name)) return;
            var path = await page.DisplayPromptAsync("项目目录", "输入工作区绝对路径");
            var card = new Border { Padding = new Thickness(16, 14), BackgroundColor = (Color)res["PanelBackground"], Stroke = (Color)res["AccentLine"], StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) }, Content = new VerticalStackLayout { Spacing = 5, Children = { new Label { Text = name.Trim(), FontSize = 15, FontAttributes = FontAttributes.Bold }, new Label { Text = string.IsNullOrWhiteSpace(path) ? "未绑定目录" : path.Trim(), TextColor = (Color)res["TextMuted"], FontFamily = "Consolas", FontSize = 10 }, new Label { Text = "项目工作区已创建，可在聊天中直接引用。", TextColor = (Color)res["TextMuted"], FontSize = 11 } } } };
            projectList.Children.Clear();
            projectList.Children.Add(card);
        }
        newProjectButton.Clicked += async (_, _) => await CreateProjectAsync();
        emptyProjectButton.Clicked += async (_, _) => await CreateProjectAsync();

        var contentStack = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                header,
                projectList,
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
