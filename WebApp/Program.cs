using DeusaldLocalizerWeb;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// ── Shared client + web platform implementations ──────────────────────────
builder.Services.AddScoped<LocalizerApiClient>();

// Browser interop
builder.Services.AddScoped<IndexedDbInterop>();
builder.Services.AddScoped<WebFileDownloadInterop>();
builder.Services.AddScoped<WebProjectArchive>();

// Platform abstractions (see WebCommon)
builder.Services.AddScoped<IAuthTokenStore, LocalStorageAuthTokenStore>();
builder.Services.AddScoped<IPreferencesStore, LocalStoragePreferencesStore>();
builder.Services.AddScoped<IProjectStoreFactory, IndexedDbProjectStoreFactory>();
builder.Services.AddScoped<IProjectLocationService, WebProjectLocationService>();
builder.Services.AddScoped<IExcelInterop, WebExcelInterop>();
builder.Services.AddScoped<RecentProjectsStore>();

// Session state (one per app in WASM)
builder.Services.AddScoped<ProjectStateService>();

await builder.Build().RunAsync();
