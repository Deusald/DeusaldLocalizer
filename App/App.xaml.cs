namespace App;

public partial class App : Application
{
    private readonly ProjectStateService _ProjectState;

    public App(ProjectStateService projectState)
    {
        _ProjectState = projectState;

        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new AppWindow(new MainPage(), _ProjectState) { Title = "Deusald Localizer" };
    }
}