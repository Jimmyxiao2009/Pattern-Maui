namespace Pattern.Maui;

public partial class App : Application
{
    private readonly Page _page;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _page = services.GetRequiredService<MainPage>();
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(_page);
}
