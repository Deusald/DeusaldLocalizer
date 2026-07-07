using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Velopack;

namespace App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Must run before any other startup code: handles Velopack's install / update /
        // uninstall hooks (the app is relaunched with special args during those) and exits
        // the process early when one is being serviced, so it never reaches the MAUI window.
        VelopackApp.Build().Run();

        var builder = MauiApp.CreateBuilder();
        builder
           .UseMauiApp<App>()
           .UseMauiCommunityToolkit()
           .ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });

        builder.Services.AddMauiBlazorWebView();

        #if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
        #endif

        // ── App services ──────────────────────────────────────────────────
        // Singleton: shared state that must survive page navigation
        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<LocalizerApiClient>();
        builder.Services.AddSingleton<UpdateService>();
        builder.Services.AddSingleton<ProjectStateService>();

        return builder.Build();
    }
}