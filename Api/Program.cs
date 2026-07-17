// SpeechInsight API host: loads .env, configures Transcription options, CORS for the Blazor client, and audio/health controllers.
using SpeechInsight.Api.Options;
using SpeechInsight.Api.Services;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

// Render (and similar hosts) set PORT at runtime; bind to 0.0.0.0 so the host can forward.
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.Configure<TranscriptionOptions>(builder.Configuration.GetSection(TranscriptionOptions.SectionName));
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();

// Transcription provider is swappable via Transcription:Provider (e.g. "OpenAI"). Add new providers here when needed.
var transcriptionProvider = builder.Configuration.GetValue<string>("Transcription:Provider") ?? "OpenAI";
if (string.Equals(transcriptionProvider, "OpenAI", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<ITranscriptionService, OpenAITranscriptionService>();
    builder.Services.AddScoped<ITranscriptionDetailsService, OpenAITranscriptionService>();
}
else
{
    throw new InvalidOperationException($"Unknown transcription provider: {transcriptionProvider}. Supported: OpenAI.");
}

builder.Services.AddScoped<IAudioDurationService, AudioDurationService>();
builder.Services.AddScoped<ITextInsightsService, OpenAITextInsightsService>();
builder.Services.AddScoped<IAudioAnalysisService, AudioAnalysisService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(origin => true)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();
app.UseBlazorFrameworkFiles();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Prevent stale Blazor/JS after deploys (old JS + new DLL can mislabel mic audio).
        var path = ctx.Context.Request.Path.Value ?? "";
        if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/", StringComparison.Ordinal) ||
            path.EndsWith("/js/app.js", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("blazor.boot.json", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers.Pragma = "no-cache";
            ctx.Context.Response.Headers.Expires = "0";
        }
    }
});
app.MapControllers();
// SPA fallback: serve Blazor client for non-API routes (when client is served from this host).
app.MapFallbackToFile("index.html");

app.Run();
