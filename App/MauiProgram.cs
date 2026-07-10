using CommunityToolkit.Maui;
using DeusaldLocalizerWeb;
using Microsoft.Extensions.Logging;
#if WINDOWS
using Velopack;
#endif

namespace App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Install process-wide exception handlers first, so anything that blows up
        // during the rest of startup still lands in the log file.
        AppLog.HookGlobalExceptions();

        #if WINDOWS
        // Must run before any other startup code: handles Velopack's install / update / uninstall
        // hooks (the app is relaunched with special args during those) and exits the process early
        // when one is being serviced, so it never reaches the MAUI window. Windows only — macOS
        // uses manual download from GitHub Releases (Velopack in-place update is unsupported there).
        try
        {
            VelopackApp.Build().Run();
        }
        catch (Exception ex)
        {
            // Never let a Velopack init problem block app launch; auto-update simply stays disabled.
            System.Diagnostics.Debug.WriteLine($"Velopack init skipped: {ex.Message}");
        }
        #endif

        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder
           .UseMauiApp<App>()
           .UseMauiCommunityToolkit()
           .ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });

        builder.Services.AddMauiBlazorWebView();

        // File log on every build (not just DEBUG) so shipped builds leave a trace too.
        builder.Logging.AddAppFileLog();

        #if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
        #endif

        // ── App services ──────────────────────────────────────────────────
        // Singleton: shared state that must survive page navigation
        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<LocalizerApiClient>();
        builder.Services.AddSingleton<UpdateService>();

        // Platform implementations of the WebCommon abstractions (disc / MAUI storage).
        builder.Services.AddSingleton<IAuthTokenStore, MauiAuthTokenStore>();
        builder.Services.AddSingleton<IPreferencesStore, MauiPreferencesStore>();
        builder.Services.AddSingleton<IProjectStoreFactory, DiscProjectStoreFactory>();
        builder.Services.AddSingleton<IProjectLocationService, MauiProjectLocationService>();
        builder.Services.AddSingleton<IExcelInterop, MauiExcelInterop>();
        builder.Services.AddSingleton<RecentProjectsStore>();
        builder.Services.AddSingleton<ProjectStateService>();

        return builder.Build();
    }
}