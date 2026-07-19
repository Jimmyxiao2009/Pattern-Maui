using System.Text.Json;
using Microsoft.Maui.Controls.Shapes;
using Pattern.Maui.Services;

namespace Pattern.Maui.Views;

public static partial class McpView
{
    public static View Create(SidecarRuntime runtime)
    {
        var res = Application.Current!.Resources;

        // ========== Page Header ==========
        var eyebrow = new Label
        {
            Text = "工具连接",
            Style = (Style)res["EyebrowLabel"],
        };

        var title = new Label
        {
            Text = "MCP 管理",
            Style = (Style)res["PageTitle"],
        };

        var subtitle = new Label
        {
            Text = "仅启动你明确配置的本地 MCP 进程；可发现工具并在此试调调用。",
            Style = (Style)res["PageSubtitle"],
        };

        var refreshButton = new Button
        {
            Text = "⟳ 刷新",
            Style = (Style)res["QuietButton"],
        };

        var addButton = new Button
        {
            Text = "＋ 添加 MCP",
            Style = (Style)res["PrimaryButton"],
        };

        var headerRight = new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { refreshButton, addButton },
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

        // ========== MCP List ==========
        var mcpList = new VerticalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(42, 8, 42, 20),
        };

        var emptyAddButton = new Button
        {
            Text = "＋ 添加 MCP",
            Style = (Style)res["PrimaryButton"],
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };

        async Task AddMcpAsync()
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null) return;
            var name = await page.DisplayPromptAsync("添加 MCP", "服务名称");
            if (string.IsNullOrWhiteSpace(name)) return;
            var command = await page.DisplayPromptAsync("启动命令", "例如 npx / python / uvx");
            if (string.IsNullOrWhiteSpace(command)) return;
            try
            {
                var current = await runtime.RequestAsync(new { type = "mcp.list" });
                var servers = current.TryGetProperty("servers", out var values) && values.ValueKind == JsonValueKind.Array ? values.EnumerateArray().Select(item => item.Clone()).ToList() : [];
                servers.Add(JsonSerializer.SerializeToElement(new { id = Guid.NewGuid().ToString("N"), name = name.Trim(), command = command.Trim(), args = Array.Empty<string>(), enabled = true, permissions = new[] { "workspace.read", "mcp.call" } }));
                await runtime.RequestAsync(new { type = "mcp.set", servers = servers.ToArray() });
                mcpList.Children.Clear();
                mcpList.Children.Add(new Border { Margin = new Thickness(0, 8), Padding = new Thickness(14, 11), BackgroundColor = (Color)res["PanelBackground"], Stroke = (Color)res["Line"], StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7) }, Content = new VerticalStackLayout { Spacing = 5, Children = { new Label { Text = name.Trim(), FontSize = 14, FontAttributes = FontAttributes.Bold }, new Label { Text = command.Trim(), FontFamily = "Consolas", TextColor = (Color)res["TextFaint"], FontSize = 10 }, new Label { Text = "已启用 · 尚未发现工具", TextColor = (Color)res["TextMuted"], FontSize = 11 } } } });
            }
            catch (Exception error) { await page.DisplayAlertAsync("MCP 添加失败", error.Message, "知道了"); }
        }
        addButton.Clicked += async (_, _) => await AddMcpAsync();
        emptyAddButton.Clicked += async (_, _) => await AddMcpAsync();

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
                    Text = "还没有 MCP 服务",
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = (Color)res["TextPrimary"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Label
                {
                    Text = "添加本地 stdio MCP 后可发现工具并试调；执行授权仍由工作流权限控制。",
                    FontSize = 11,
                    TextColor = (Color)res["TextMuted"],
                    HorizontalOptions = LayoutOptions.Center,
                },
                emptyAddButton,
            },
        };
        mcpList.Children.Add(emptyState);

        var contentStack = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                header,
                mcpList,
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
