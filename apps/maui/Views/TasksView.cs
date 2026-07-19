using Microsoft.Maui.Controls.Shapes;

using Pattern.Maui.Services;

namespace Pattern.Maui.Views;

public static partial class TasksView
{
    public static View Create(SidecarRuntime runtime)
    {
        var res = Application.Current!.Resources;

        // ========== Page Header ==========
        var eyebrow = new Label
        {
            Text = "提醒 · 定时 · 执行",
            Style = (Style)res["EyebrowLabel"],
        };

        var title = new Label
        {
            Text = "任务",
            Style = (Style)res["PageTitle"],
        };

        var subtitle = new Label
        {
            Text = "每日提醒、循环和定时执行都在这里。AI 会不会主动找你，去「主动」页。完成的会自动进「已完成」。",
            Style = (Style)res["PageSubtitle"],
        };

        var remindButton = new Button
        {
            Text = "🔔 新建提醒",
            Style = (Style)res["QuietButton"],
        };

        var newTaskButton = new Button
        {
            Text = "＋ 新建定时任务",
            Style = (Style)res["PrimaryButton"],
        };

        var headerRight = new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { remindButton, newTaskButton },
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

        // ========== Filter Tabs ==========
        var filterTabs = new HorizontalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(42, 0, 42, 12),
        };

        var filters = new[] { "未完成", "执行中", "定时", "提醒", "已完成" };
        for (int i = 0; i < filters.Length; i++)
        {
            filterTabs.Children.Add(CreateFilterTab(filters[i], i == 0, res));
        }

        // ========== Tasks List ==========
        var tasksList = new VerticalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(42, 8, 42, 20),
        };

        var emptyTaskButton = new Button
        {
            Text = "＋ 新建定时任务",
            Style = (Style)res["PrimaryButton"],
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 8, 0, 0),
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
                    Text = "当前没有未完成项",
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = (Color)res["TextPrimary"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Label
                {
                    Text = "可新建定时任务、每日提醒，或在聊天里用 /task /loop /remind。",
                    FontSize = 11,
                    TextColor = (Color)res["TextMuted"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                emptyTaskButton,
            },
        };
        tasksList.Children.Add(emptyState);

        async Task CreateTaskAsync()
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null) return;
            var taskTitle = await page.DisplayPromptAsync("新建定时任务", "任务标题");
            if (string.IsNullOrWhiteSpace(taskTitle)) return;
            var detail = await page.DisplayPromptAsync("任务详情", "描述要执行的内容") ?? string.Empty;
            try
            {
                await runtime.RequestAsync(new { type = "task.create", title = taskTitle.Trim(), detail = detail.Trim() });
                tasksList.Children.Clear();
                tasksList.Children.Add(new Border { Padding = new Thickness(16, 13), BackgroundColor = (Color)res["PanelBackground"], Stroke = (Color)res["Line"], StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(7) }, Content = new VerticalStackLayout { Spacing = 6, Children = { new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "排队中", TextColor = (Color)res["Accent"], FontSize = 10, FontAttributes = FontAttributes.Bold }, new Label { Text = "刚刚创建", TextColor = (Color)res["TextFaint"], FontFamily = "Consolas", FontSize = 10 } } }, new Label { Text = taskTitle.Trim(), FontSize = 15, FontAttributes = FontAttributes.Bold }, new Label { Text = detail.Trim(), TextColor = (Color)res["TextMuted"], FontSize = 11 } } } });
            }
            catch (Exception error) { await page.DisplayAlertAsync("任务创建失败", error.Message, "知道了"); }
        }
        newTaskButton.Clicked += async (_, _) => await CreateTaskAsync();
        emptyTaskButton.Clicked += async (_, _) => await CreateTaskAsync();
        remindButton.Clicked += async (_, _) => await CreateTaskAsync();

        var contentStack = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                header,
                filterTabs,
                tasksList,
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
