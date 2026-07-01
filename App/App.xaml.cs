namespace App;

public partial class App : Application
{
    private readonly ProjectStateService _ProjectState;
    private readonly DlocFileService     _DlocService;
    
    public App(ProjectStateService projectState, DlocFileService dlocService)
    {
        _ProjectState = projectState;
        _DlocService  = dlocService;
        
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new AppWindow(new MainPage(), _ProjectState, _DlocService) { Title = "Deusald Localizer" };
    }
}