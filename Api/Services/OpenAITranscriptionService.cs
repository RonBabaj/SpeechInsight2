// Calls OpenAI /v1/audio/transcriptions (raw HttpClient). Supports text, verbose_json, and diarized_json.
// Parses duration (usage/segments) and language (when present in response) for the analysis pipeline.
using System.Net.Http.Headers;
using System.Net;
using System.Text.Json;

namespace SpeechInsight.Api.Services;

public sealed class OpenAITranscriptionException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }

    public OpenAITranscriptionException(HttpStatusCode statusCode, string responseBody)
        : base($"OpenAI transcription failed: {(int)statusCode} {statusCode}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

public sealed record TranscriptionSegment(string? Speaker, double? StartSeconds, double? EndSeconds, string Text);

public sealed record TranscriptionDetails(
    string Text,
    string Model,
    double? DurationSeconds,
    IReadOnlyList<TranscriptionSegment> Segments,
    bool Diarized,
    string? Language = null);

public interface ITranscriptionDetailsService
{
    Task<TranscriptionDetails> TranscribeDetailedAsync(Stream audioStream, string fileName, string? contentType, bool diarize);
}

public class OpenAITranscriptionService : ITranscriptionService, ITranscriptionDetailsService
{
    private const string WhisperEndpoint = "https://api.openai.com/v1/audio/transcriptions";
    private readonly HttpClient _httpClient;
    private readonly Api.Options.TranscriptionOptions _options;

    public OpenAITranscriptionService(IHttpClientFactory httpClientFactory, Microsoft.Extensions.Options.IOptions<Api.Options.TranscriptionOptions> options)
    {
        _httpClient = httpClientFactory.CreateClient();
        _options = options.Value;
    }

    public async Task<string> TranscribeAsync(Stream audioStream)
    {
        var (status, body) = await CallOpenAIAsync(
            audioStream,
            fileName: "audio",
            contentType: null,
            model: _options.DefaultModel,
            responseFormat: "text",
            chunkingStrategy: null);

        if (status is < 200 or >= 300)
            throw new OpenAITranscriptionException((HttpStatusCode)status, body);

        return body;
    }

    public async Task<TranscriptionDetails> TranscribeDetailedAsync(Stream audioStream, string fileName, string? contentType, bool diarize)
    {
        var model = diarize ? _options.DiarizeModel : _options.DefaultModel;
        var responseFormat = diarize ? "diarized_json" : "verbose_json";
        var chunkingStrategy = diarize ? "auto" : null;

        var (status, body) = await CallOpenAIAsync(
            audioStream,
            fileName,
            contentType,
            model,
            responseFormat,
            chunkingStrategy);

        if (status is < 200 or >= 300)
            throw new OpenAITranscriptionException((HttpStatusCode)status, body);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var text = root.TryGetProperty("text", out var textEl) ? (textEl.GetString() ?? "") : "";
        string? language = root.TryGetProperty("language", out var langEl) ? langEl.GetString() : null;
        double? duration = null;
        if (root.TryGetProperty("duration", out var rootDurationEl) &&
            rootDurationEl.ValueKind == JsonValueKind.Number &&
            rootDurationEl.TryGetDouble(out var rootDuration))
        {
            duration = rootDuration;
        }
        if (root.TryGetProperty("usage", out var usageEl) && usageEl.ValueKind == JsonValueKind.Object)
        {
            // Different models/shapes:
            // - { "duration": 191.0 }
            // - { "duration_seconds": 191.0 }
            // - { "type": "duration", "seconds": 191 }
            if (usageEl.TryGetProperty("seconds", out var secondsEl) && secondsEl.TryGetDouble(out var seconds))
            {
                duration = seconds;
            }
            else if (usageEl.TryGetProperty("duration", out var durEl))
            {
                if (durEl.ValueKind == JsonValueKind.Number && durEl.TryGetDouble(out var d))
                    duration = d;
                else if (durEl.ValueKind == JsonValueKind.Object &&
                         durEl.TryGetProperty("seconds", out var innerSecondsEl) &&
                         innerSecondsEl.TryGetDouble(out var innerSeconds))
                    duration = innerSeconds;
            }
            else if (usageEl.TryGetProperty("duration_seconds", out var dur2El) && dur2El.TryGetDouble(out var d2))
            {
                duration = d2;
            }
        }

        var segments = new List<TranscriptionSegment>();
        if (root.TryGetProperty("segments", out var segmentsEl) && segmentsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var seg in segmentsEl.EnumerateArray())
            {
                var segText = seg.TryGetProperty("text", out var st) ? (st.GetString() ?? "") : "";
                string? speaker = seg.TryGetProperty("speaker", out var sp) ? sp.GetString() : null;

                double? start = null;
                if (seg.TryGetProperty("start", out var startEl) && startEl.TryGetDouble(out var s))
                    start = s;

                double? end = null;
                if (seg.TryGetProperty("end", out var endEl) && endEl.TryGetDouble(out var e))
                    end = e;

                if (!string.IsNullOrWhiteSpace(segText))
                    segments.Add(new TranscriptionSegment(speaker, start, end, segText.Trim()));
            }
        }

        if (duration == null && segments.Count > 0)
        {
            // Some response formats omit usage/duration; derive it from segments.
            var maxEnd = segments.Max(s => s.EndSeconds ?? 0);
            if (maxEnd > 0)
                duration = maxEnd;
        }

        return new TranscriptionDetails(
            Text: text,
            Model: model,
            DurationSeconds: duration,
            Segments: segments,
            Diarized: diarize,
            Language: language);
    }

    private async Task<(int statusCode, string body)> CallOpenAIAsync(
        Stream audioStream,
        string fileName,
        string? contentType,
        string model,
        string responseFormat,
        string? chunkingStrategy)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OPENAI_API_KEY is not set. Add it to .env or environment.");

        using var content = new MultipartFormDataContent();

        var fileContent = new StreamContent(audioStream);
        if (!string.IsNullOrWhiteSpace(contentType))
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        content.Add(fileContent, "file", fileName);
        content.Add(new StringContent(model), "model");
        content.Add(new StringContent(responseFormat), "response_format");

        if (!string.IsNullOrWhiteSpace(chunkingStrategy))
            content.Add(new StringContent(chunkingStrategy), "chunking_strategy");

        using var request = new HttpRequestMessage(HttpMethod.Post, WhisperEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = content;

        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, body);
    }
}
