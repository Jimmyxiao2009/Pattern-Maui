using Microsoft.Maui.Controls.Shapes;
using Pattern.Maui.Services;

namespace Pattern.Maui.Views;

/// <summary>Compact companion surface opened by Ctrl+Alt+P.</summary>
public static class QuickWindowView
{
    public static View Create(SidecarRuntime runtime, Action close)
    {
        var res = Application.Current!.Resources;
        var draft = new Editor { Placeholder = "有什么事？", AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 46, MaximumHeightRequest = 90 };
        var answer = new Label { Text = "嗨，我在。想聊聊、记一件事，或者直接让我动手。", FontSize = 13, LineHeight = 1.45, TextColor = (Color)res["TextPrimary"] };
        var status = new Label { Text = "随叫随到", FontSize = 10, TextColor = (Color)res["TextMuted"], VerticalTextAlignment = TextAlignment.Center };
        var send = new Button { Text = "↑", Style = (Style)res["PrimaryButton"], WidthRequest = 38, HeightRequest = 34, Padding = new Thickness(0), FontSize = 18 };
        var busy = false;

        async Task SendAsync()
        {
            var text = draft.Text?.Trim();
            if (busy || string.IsNullOrWhiteSpace(text)) return;
            busy = true;
            send.IsEnabled = false;
            draft.Text = string.Empty;
            answer.Text = "正在组织回答…";
            status.Text = "主 Agent · 正在想";
            try { await runtime.SendChatAsync(text, Array.Empty<ChatTurn>(), "quick"); }
            catch (Exception error) { answer.Text = $"运行时未连接：{error.Message}"; status.Text = "运行时未连接"; }
            finally { busy = false; send.IsEnabled = true; }
        }
        send.Clicked += async (_, _) => await SendAsync();
        draft.Completed += async (_, _) => await SendAsync();
        runtime.ChatDelta += delta => MainThread.BeginInvokeOnMainThread(() => { answer.Text = answer.Text == "正在组织回答…" ? delta : answer.Text + delta; });
        runtime.ChatDone += () => MainThread.BeginInvokeOnMainThread(() => { status.Text = "随叫随到"; send.IsEnabled = true; });
        runtime.ChatError += error => MainThread.BeginInvokeOnMainThread(() => { answer.Text = $"回复失败：{error}"; status.Text = "回复失败"; send.IsEnabled = true; });

        var header = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        header.Children.Add(new HorizontalStackLayout { Spacing = 8, Children = { new Border { WidthRequest = 14, HeightRequest = 14, BackgroundColor = (Color)res["Accent"], StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7) } }, new Label { Text = "Pattern", FontSize = 13, FontAttributes = FontAttributes.Bold }, status } });
        var closeButton = new Button { Text = "×", Style = (Style)res["QuietButton"], WidthRequest = 30, HeightRequest = 30, Padding = new Thickness(0), FontSize = 18 };
        closeButton.Clicked += (_, _) => close();
        header.Children.Add(closeButton);
        Grid.SetColumn(closeButton, 1);

        var composer = new Border { Padding = new Thickness(10, 8), BackgroundColor = (Color)res["SurfaceBackground"], Stroke = (Color)res["LineStrong"], StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) }, Content = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8, Children = { draft, send } } };
        Grid.SetColumn(send, 1);
        var footer = new HorizontalStackLayout
        {
            Spacing = 10,
            Children =
            {
                new Button { Text = "打开主窗口", Style = (Style)res["QuietButton"], FontSize = 10, Command = new Command(close) },
                new Label { Text = "Enter 发送 · Esc 隐藏", TextColor = (Color)res["TextFaint"], FontSize = 10, VerticalTextAlignment = TextAlignment.Center },
            },
        };
        return new Border { WidthRequest = 520, Padding = new Thickness(16, 13), BackgroundColor = (Color)res["PanelBackground"], Stroke = (Color)res["LineStrong"], StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) }, Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.35f, Radius = 18, Offset = new Point(0, 8) }, Content = new VerticalStackLayout { Spacing = 12, Children = { header, new Border { Padding = new Thickness(10, 8), BackgroundColor = (Color)res["AccentWash"], Stroke = (Color)res["AccentLine"], StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7) }, Content = answer }, composer, footer } } };
    }
}
