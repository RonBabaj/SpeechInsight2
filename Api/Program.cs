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
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
// SPA fallback: serve Blazor client for non-API routes (when client is served from this host).
app.MapFallbackToFile("index.html");

app.Run();
