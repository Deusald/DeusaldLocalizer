using System.Text.Json.Serialization;
using DeusaldLocalizerBackend;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────────
builder.Services.Configure<BotOptions>(builder.Configuration.GetSection(BotOptions.SECTION_NAME));

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

var app = builder.Build();

// ── Pipeline ──────────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
