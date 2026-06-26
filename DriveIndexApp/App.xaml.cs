namespace DriveIndexApp;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new Window(new NavigationPage(new MainPage(new MainViewModel())));
}
