using Microsoft.Maui.Controls.Shapes;
using Pattern.Maui.Services;

namespace Pattern.Maui.Views;

public static partial class WorkflowsView
{
    public static View Create(SidecarRuntime runtime)
    {
        var res = Application.Current!.Resources;

        // ========== Page Header ==========
        var eyebrow = new Label
        {
            Text = "Skills / 技能",
            Style = (Style)res["EyebrowLabel"],
        };

        var title = new Label
        {
            Text = "技能",
            Style = (Style)res["PageTitle"],
        };

        var subtitle = new Label
        {
            Text = "技能描述怎么做；工作流把多项技能编排成一次可追踪的执行。",
            Style = (Style)res["PageSubtitle"],
        };

        var installButton = new Button
        {
            Text = "＋ 安装技能",
            Style = (Style)res["PrimaryButton"],
        };
        async Task InstallSkillAsync()
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null) return;
            var name = await page.DisplayPromptAsync("安装技能", "技能名称");
            if (string.IsNullOrWhiteSpace(name)) return;
            var prompt = await page.DisplayPromptAsync("技能提示词", "告诉 Agent 如何完成这项工作");
            if (string.IsNullOrWhiteSpace(prompt)) return;
            try { await runtime.RequestAsync(new { type = "skill.install", skill = new { id = name.Trim().ToLowerInvariant().Replace(' ', '-'), name = name.Trim(), kind = "coding", description = name.Trim(), permissions = new[] { "workspace.read" }, prompt = prompt.Trim(), builtin = false } }); await page.DisplayAlertAsync("技能已安装", name.Trim(), "知道了"); }
            catch (Exception error) { await page.DisplayAlertAsync("安装失败", error.Message, "知道了"); }
        }
        installButton.Clicked += async (_, _) => await InstallSkillAsync();

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
                installButton,
            },
        };
        Grid.SetColumn(installButton, 1);
        installButton.VerticalOptions = LayoutOptions.Start;

        // ========== Usage Note ==========
        var usageNote = new Border
        {
            Margin = new Thickness(42, 12, 42, 12),
            BackgroundColor = (Color)res["InfoWash"],
            Stroke = (Color)res["Info"],
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7) },
            Padding = new Thickness(14, 11),
            Content = new HorizontalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    new Label
                    {
                        Text = "⚡",
                        FontSize = 18,
                        TextColor = (Color)res["Info"],
                        VerticalOptions = LayoutOptions.Center,
                    },
                    new VerticalStackLayout
                    {
                        Spacing = 2,
                        Children =
                        {
                            new Label
                            {
                                Text = "技能不等于工作流",
                                FontSize = 13,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = (Color)res["TextPrimary"],
                            },
                            new Label
                            {
                                Text = "技能描述「怎么做」，定时任务描述「什么时候做」和「按哪些步骤做」。",
                                FontSize = 11,
                                TextColor = (Color)res["TextMuted"],
                            },
                        },
                    },
                },
            },
        };

        // ========== Skills List ==========
        var skillsList = new VerticalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(42, 8, 42, 20),
        };

        var workflowSection = new VerticalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(42, 8, 42, 4),
            Children =
            {
                new Label { Text = "工作流", TextColor = (Color)res["TextPrimary"], FontSize = 14, FontAttributes = FontAttributes.Bold },
                new Label { Text = "选择一个预设，把审查、测试或发布检查交给可追踪任务。", TextColor = (Color)res["TextMuted"], FontSize = 11 },
            },
        };
        foreach (var item in new[]
        {
            ("审查并验证", "先审查变更，再运行测试并汇总风险。", "串行 · 1 Agent"),
            ("安全重构", "以测试为约束做小步重构，完成后复核差异。", "串行 · 1 Agent"),
            ("发布准备检查", "并行收集只读证据，再生成发布清单。", "只读并行 · 2 Agent"),
        })
        {
            var runButton = new Button { Text = "运行", Style = (Style)res["QuietButton"], FontSize = 10 };
            runButton.Clicked += async (_, _) =>
            {
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                if (page is null) return;
                var input = await page.DisplayPromptAsync("运行工作流", $"{item.Item1}\n输入本次目标");
                if (string.IsNullOrWhiteSpace(input)) return;
                try { await runtime.RequestAsync(new { type = "workflow.run", workflowId = item.Item1 == "审查并验证" ? "review-and-test" : item.Item1 == "安全重构" ? "safe-refactor" : "release-readiness", input = input.Trim(), isolatedWorktree = false, agentCount = item.Item3.Contains("2") ? 2 : 1 }); await page.DisplayAlertAsync("工作流已启动", item.Item1, "知道了"); }
                catch (Exception error) { await page.DisplayAlertAsync("工作流启动失败", error.Message, "知道了"); }
            };
            var cardGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) },
                Children =
                {
                    new VerticalStackLayout { Spacing = 3, Children = { new Label { Text = item.Item1, FontSize = 13, FontAttributes = FontAttributes.Bold }, new Label { Text = item.Item2, TextColor = (Color)res["TextMuted"], FontSize = 11 }, new Label { Text = item.Item3, TextColor = (Color)res["TextFaint"], FontFamily = "Consolas", FontSize = 10 } } },
                    runButton,
                },
            };
            Grid.SetColumn(runButton, 1);
            var card = new Border
            {
                Padding = new Thickness(14, 11),
                Margin = new Thickness(42, 0, 42, 8),
                BackgroundColor = (Color)res["PanelBackground"],
                Stroke = (Color)res["Line"],
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7) },
                Content = cardGrid,
            };
            workflowSection.Children.Add(card);
        }

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
                    Text = "🔧",
                    FontSize = 32,
                    TextColor = (Color)res["TextFaint"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Label
                {
                    Text = "还没有技能",
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = (Color)res["TextPrimary"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Label
                {
                    Text = "安装技能后，主 Agent 可以在对话中调用它们。",
                    FontSize = 11,
                    TextColor = (Color)res["TextMuted"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Button
                {
                    Text = "＋ 安装技能",
                    Style = (Style)res["PrimaryButton"],
                    HorizontalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 8, 0, 0),
                },
            },
        };
        skillsList.Children.Add(emptyState);

        var contentStack = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                header,
                usageNote,
                workflowSection,
                skillsList,
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
