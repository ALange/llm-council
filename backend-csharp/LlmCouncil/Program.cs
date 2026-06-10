using dotenv.net;
using LlmCouncil.Services;

// Load .env file from the project root (two levels up from the build output)
DotEnv.Load(options: new DotEnvOptions(probeForEnv: true, probeLevelsToSearch: 6));

var builder = WebApplication.CreateBuilder(args);

// Bind CouncilOptions; allow individual keys to be overridden via env vars
// e.g.  Council__OpenRouterApiKey=sk-or-...  or  OPENROUTER_API_KEY=...
builder.Configuration.AddEnvironmentVariables();

builder.Services.Configure<CouncilOptions>(opts =>
{
    builder.Configuration.GetSection(CouncilOptions.SectionName).Bind(opts);

    // Support the plain OPENROUTER_API_KEY env var used by the Python version
    var envKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
    if (!string.IsNullOrWhiteSpace(envKey))
        opts.OpenRouterApiKey = envKey;

    var provider = Environment.GetEnvironmentVariable("LLM_PROVIDER");
    if (!string.IsNullOrWhiteSpace(provider))
        opts.LlmProvider = provider;

    var liteLlmUrl = Environment.GetEnvironmentVariable("LITELLM_API_URL");
    if (!string.IsNullOrWhiteSpace(liteLlmUrl))
        opts.LiteLlmApiUrl = liteLlmUrl;

    var liteLlmKey = Environment.GetEnvironmentVariable("LITELLM_API_KEY");
    if (!string.IsNullOrWhiteSpace(liteLlmKey))
        opts.LiteLlmApiKey = liteLlmKey;

    var councilModels = Environment.GetEnvironmentVariable("COUNCIL_MODELS");
    if (!string.IsNullOrWhiteSpace(councilModels))
    {
        var parsedCouncilModels = councilModels
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (parsedCouncilModels.Count > 0)
            opts.CouncilModels = parsedCouncilModels;
    }

    var chairmanModel = Environment.GetEnvironmentVariable("CHAIRMAN_MODEL");
    if (!string.IsNullOrWhiteSpace(chairmanModel))
        opts.ChairmanModel = chairmanModel;

    if (opts.CouncilModels.Count == 0)
        throw new InvalidOperationException("Council models are not configured. Set COUNCIL_MODELS in .env or Council:CouncilModels in appsettings.");

    if (string.IsNullOrWhiteSpace(opts.ChairmanModel))
        throw new InvalidOperationException("Chairman model is not configured. Set CHAIRMAN_MODEL in .env or Council:ChairmanModel in appsettings.");
});

builder.Services.AddHttpClient<OpenRouterService>();
builder.Services.AddSingleton<StorageService>();
builder.Services.AddScoped<CouncilService>();

builder.Services.AddControllers();

// CORS – mirror the Python backend's allowed origins
builder.Services.AddCors(o => o.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
          .AllowAnyMethod()
          .AllowAnyHeader()
          .AllowCredentials()));

var app = builder.Build();

app.UseCors();

// Health check
app.MapGet("/", () => new { status = "ok", service = "LLM Council API (C#)" });

app.UseAuthorization();
app.MapControllers();

app.Run();
