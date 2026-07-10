using System.Text.Json.Serialization;
using DeusaldLocalizerBackend;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────────
builder.Services.Configure<BotOptions>(builder.Configuration.GetSection(BotOptions.SECTION_NAME));

CorsOptions corsOptions = builder.Configuration.GetSection(CorsOptions.SECTION_NAME).Get<CorsOptions>() ?? new CorsOptions();

// ── CORS ──────────────────────────────────────────────────────────────────────
// Only the browser-based web client needs this; the API calls carry an X-User-Id header and so always
// trigger a preflight. Bearer tokens in the Authorization header are not CORS "credentials" (those are
// cookies / TLS certs), so wildcard-free named origins with AllowAnyHeader/AllowAnyMethod are enough.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (corsOptions.AllowedOrigins.Length > 0)
        policy.WithOrigins(corsOptions.AllowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
}));

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<GitService>();
builder.Services.AddSingleton<ProjectRegistry>();
builder.Services.AddSingleton<ProjectSerializer>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<RepoPreparer>();
builder.Services.AddSingleton<SyncService>();
builder.Services.AddSingleton<PushService>();

builder.Services
       .AddControllers()
       .AddJsonOptions(options =>
        {
            // Serialize enums (EntryChangeType, SyncStatus, …) as strings on the wire.
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

builder.Services.AddOpenApi();

WebApplication app = builder.Build();

// ── Pipeline ──────────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment()) app.MapOpenApi();

// Answer Chrome's Private Network Access preflight before the CORS middleware terminates it, so a
// deployed HTTPS web build (e.g. GitHub Pages) can reach a backend running on the local machine. Runs
// only for the preflight OPTIONS carrying the PNA request header, and only when explicitly enabled.
if (corsOptions.AllowPrivateNetwork)
    app.Use(async (context, next) =>
    {
        if (HttpMethods.IsOptions(context.Request.Method)
         && context.Request.Headers.ContainsKey("Access-Control-Request-Private-Network"))
            context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
        await next();
    });

app.UseCors();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// ── Startup log ─────────────────────────────────────────────────────────────────
// List the configured projects at boot (never the RemoteUrl — it carries the PAT credential).
{
    ILogger    logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    BotOptions bot    = app.Services.GetRequiredService<IOptions<BotOptions>>().Value;

    if (bot.Projects.Count == 0)
        logger.LogWarning("No projects configured under the 'Bot' section; the bot has nothing to serve.");
    else
    {
        logger.LogInformation("Configured {Count} project(s), repos root '{ReposRoot}':", bot.Projects.Count, bot.ReposRoot);
        foreach (ProjectConfig p in bot.Projects)
            logger.LogInformation("  • '{Slug}' ({ProjectId}) tracking branch '{Branch}'", p.Slug, p.ProjectId, p.Branch);
    }
}

app.Run();