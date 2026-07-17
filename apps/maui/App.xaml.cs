namespace Pattern.Maui;

public partial class App : Application
{
    private readonly Page _page;

    public App(MainPage page)
    {
        InitializeComponent();
        _page = page;
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(_page);
}
