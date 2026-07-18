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
    private readonly IAudioTranscodeService _transcodeService;
    private readonly ILogger<OpenAITranscriptionService> _logger;

    public OpenAITranscriptionService(
        IHttpClientFactory httpClientFactory,
        Microsoft.Extensions.Options.IOptions<Api.Options.TranscriptionOptions> options,
        IAudioTranscodeService transcodeService,
        ILogger<OpenAITranscriptionService> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _options = options.Value;
        _transcodeService = transcodeService;
        _logger = logger;
    }

    public async Task<string> TranscribeAsync(Stream audioStream)
    {
        var bytes = await ReadAllBytesAsync(audioStream);
        var (fileName, contentType) = ResolveAudioIdentity(bytes, "audio.wav", null);

        var (status, body) = await CallOpenAIAsync(
            bytes,
            fileName,
            contentType,
            model: _options.DefaultModel,
            responseFormat: "text",
            chunkingStrategy: null);

        if (status is < 200 or >= 300)
            throw new OpenAITranscriptionException((HttpStatusCode)status, body);

        return body;
    }

    public async Task<TranscriptionDetails> TranscribeDetailedAsync(Stream audioStream, string fileName, string? contentType, bool diarize)
    {
        var bytes = await ReadAllBytesAsync(audioStream);
        var isMicRecording = IsMicrophoneRecording(fileName);
        var (resolvedName, resolvedType) = ResolveAudioIdentity(bytes, fileName, contentType);

        _logger.LogInformation(
            "OpenAI upload identity: mic={IsMic}, diarizeRequested={Diarize}, requested={RequestedName}/{RequestedType}, resolved={ResolvedName}/{ResolvedType}, bytes={Bytes}, magic={Magic}",
            isMicRecording,
            diarize,
            fileName,
            contentType,
            resolvedName,
            resolvedType,
            bytes.Length,
            DescribeMagic(bytes));

        if (!diarize)
        {
            return await TranscribeWithWhisperAsync(bytes, resolvedName, resolvedType);
        }

        // OpenAI-supported containers: mp3, mp4, mpeg, mpga, m4a, wav, webm.
        // Browser MediaRecorder encodings of those are often rejected by gpt-4o-transcribe-diarize
        // even when the extension is "supported". Official diarize examples use PCM WAV — convert to that.
        var diarizeBytes = bytes;
        var diarizeName = resolvedName;
        var diarizeType = resolvedType;

        var inputExt = Path.GetExtension(resolvedName);
        if (string.IsNullOrEmpty(inputExt))
            inputExt = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(inputExt))
            inputExt = ".webm";

        var converted = await _transcodeService.TryConvertToWavAsync(bytes, inputExt);
        if (converted != null)
        {
            diarizeBytes = converted;
            diarizeName = "audio.wav";
            diarizeType = "audio/wav";
        }
        else if (isMicRecording)
        {
            _logger.LogWarning("Mic→WAV conversion unavailable; using {Model} without speaker labels.", _options.DefaultModel);
            return await TranscribeWithWhisperAsync(bytes, resolvedName, resolvedType);
        }

        var (status, body) = await CallOpenAIAsync(
            diarizeBytes,
            diarizeName,
            diarizeType,
            _options.DiarizeModel,
            "diarized_json",
            chunkingStrategy: "auto");

        if (status is >= 200 and < 300)
            return ParseTranscriptionDetails(body, _options.DiarizeModel, diarize: true);

        _logger.LogWarning(
            "Diarize model failed (status={Status}, mic={IsMic}, file={File}, bytes={Bytes}). Falling back to {Fallback}. Body={Body}",
            status,
            isMicRecording,
            diarizeName,
            diarizeBytes.Length,
            _options.DefaultModel,
            Truncate(body, 500));

        // Always fall back for mic or unsupported-file errors so the user still gets a transcript.
        if (isMicRecording || LooksLikeUnsupportedAudio(body))
        {
            try
            {
                return await TranscribeWithWhisperAsync(bytes, resolvedName, resolvedType);
            }
            catch (OpenAITranscriptionException) when (converted != null)
            {
                // Original browser bytes failed whisper too — retry with the converted PCM WAV.
                _logger.LogWarning("Whisper failed on original mic bytes; retrying with converted WAV.");
                return await TranscribeWithWhisperAsync(converted, "audio.wav", "audio/wav");
            }
        }

        throw new OpenAITranscriptionException((HttpStatusCode)status, body);
    }

    private async Task<TranscriptionDetails> TranscribeWithWhisperAsync(byte[] bytes, string fileName, string contentType)
    {
        var (status, body) = await CallOpenAIAsync(
            bytes,
            fileName,
            contentType,
            _options.DefaultModel,
            "verbose_json",
            chunkingStrategy: null);

        if (status is < 200 or >= 300)
            throw new OpenAITranscriptionException((HttpStatusCode)status, body);

        return ParseTranscriptionDetails(body, _options.DefaultModel, diarize: false);
    }

    private static TranscriptionDetails ParseTranscriptionDetails(string body, string model, bool diarize)
    {
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
        byte[] audioBytes,
        string fileName,
        string contentType,
        string model,
        string responseFormat,
        string? chunkingStrategy)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OPENAI_API_KEY is not set. Add it to .env or environment.");

        // Build multipart like: curl -F file=@audio.wav -F model=... -F response_format=... -F chunking_strategy=auto
        using var content = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(audioBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);

        content.Add(CreateFormField(model), "model");
        content.Add(CreateFormField(responseFormat), "response_format");
        if (!string.IsNullOrWhiteSpace(chunkingStrategy))
            content.Add(CreateFormField(chunkingStrategy), "chunking_strategy");

        using var request = new HttpRequestMessage(HttpMethod.Post, WhisperEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = content;

        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if ((int)response.StatusCode is < 200 or >= 300)
        {
            _logger.LogWarning(
                "OpenAI transcription error: status={Status}, model={Model}, file={File}, type={Type}, bytes={Bytes}, body={Body}",
                (int)response.StatusCode,
                model,
                fileName,
                contentType,
                audioBytes.Length,
                Truncate(body, 500));
        }

        return ((int)response.StatusCode, body);
    }

    /// <summary>Form field without charset=utf-8 (matches curl -F field=value).</summary>
    private static ByteArrayContent CreateFormField(string value)
    {
        var part = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(value));
        return part;
    }

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "…";

    private static async Task<byte[]> ReadAllBytesAsync(Stream audioStream)
    {
        if (audioStream == null) throw new ArgumentNullException(nameof(audioStream));
        if (audioStream.CanSeek)
            audioStream.Position = 0;

        using var ms = new MemoryStream();
        await audioStream.CopyToAsync(ms);
        var bytes = ms.ToArray();

        if (audioStream.CanSeek)
            audioStream.Position = 0;

        if (bytes.Length == 0)
            throw new InvalidOperationException("Audio stream is empty.");

        return bytes;
    }

    /// <summary>
    /// gpt-4o transcription models require the filename extension to match the real container.
    /// Sniff magic bytes and override a mismatched client-provided name/type.
    /// </summary>
    internal static (string fileName, string contentType) ResolveAudioIdentity(byte[] bytes, string? fileName, string? contentType)
    {
        var detected = DetectAudioFormat(bytes);
        if (detected != null)
            return detected.Value;

        var mediaType = NormalizeMediaType(contentType);
        var ext = Path.GetExtension(fileName ?? "");
        if (!string.IsNullOrEmpty(ext))
        {
            var fromExt = ext.ToLowerInvariant() switch
            {
                ".wav" => ("audio.wav", "audio/wav"),
                ".webm" => ("audio.webm", "audio/webm"),
                ".mp3" => ("audio.mp3", "audio/mpeg"),
                ".mpga" => ("audio.mpga", "audio/mpeg"),
                ".mpeg" => ("audio.mpeg", "audio/mpeg"),
                ".mp4" => ("audio.mp4", "audio/mp4"),
                ".m4a" => ("audio.m4a", "audio/mp4"),
                ".ogg" => ("audio.ogg", "audio/ogg"),
                ".oga" => ("audio.oga", "audio/ogg"),
                _ => ((string, string)?)null
            };
            if (fromExt != null)
                return fromExt.Value;
        }

        if (mediaType != null)
        {
            if (mediaType.Contains("wav", StringComparison.OrdinalIgnoreCase)) return ("audio.wav", "audio/wav");
            if (mediaType.Contains("webm", StringComparison.OrdinalIgnoreCase)) return ("audio.webm", "audio/webm");
            if (mediaType.Contains("mpeg", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("mp3", StringComparison.OrdinalIgnoreCase))
                return ("audio.mp3", "audio/mpeg");
            if (mediaType.Contains("mp4", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("m4a", StringComparison.OrdinalIgnoreCase))
                return ("audio.m4a", "audio/mp4");
            if (mediaType.Contains("ogg", StringComparison.OrdinalIgnoreCase)) return ("audio.ogg", "audio/ogg");
        }

        // Last resort: keep a valid whisper-friendly name.
        return ("audio.wav", mediaType ?? "audio/wav");
    }

    private static (string fileName, string contentType)? DetectAudioFormat(byte[] bytes)
    {
        if (bytes.Length >= 12 &&
            bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
            bytes[8] == (byte)'W' && bytes[9] == (byte)'A' && bytes[10] == (byte)'V' && bytes[11] == (byte)'E')
        {
            return ("audio.wav", "audio/wav");
        }

        // EBML / WebM / Matroska
        if (bytes.Length >= 4 && bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3)
            return ("audio.webm", "audio/webm");

        // Ogg
        if (bytes.Length >= 4 && bytes[0] == (byte)'O' && bytes[1] == (byte)'g' && bytes[2] == (byte)'g' && bytes[3] == (byte)'S')
            return ("audio.ogg", "audio/ogg");

        // ID3 MP3
        if (bytes.Length >= 3 && bytes[0] == (byte)'I' && bytes[1] == (byte)'D' && bytes[2] == (byte)'3')
            return ("audio.mp3", "audio/mpeg");

        // MPEG frame sync
        if (bytes.Length >= 2 && bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0)
            return ("audio.mp3", "audio/mpeg");

        // ISO BMFF (mp4 / m4a): ....ftyp
        if (bytes.Length >= 12 &&
            bytes[4] == (byte)'f' && bytes[5] == (byte)'t' && bytes[6] == (byte)'y' && bytes[7] == (byte)'p')
        {
            return ("audio.m4a", "audio/mp4");
        }

        return null;
    }

    private static string DescribeMagic(byte[] bytes)
    {
        var n = Math.Min(bytes.Length, 8);
        return Convert.ToHexString(bytes.AsSpan(0, n));
    }

    private static bool IsMicrophoneRecording(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && fileName.StartsWith("recording.", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeUnsupportedAudio(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) return false;
        // Only treat file-format rejections as retryable — not chunking_strategy / other invalid_value errors.
        return responseBody.Contains("corrupted or unsupported", StringComparison.OrdinalIgnoreCase)
            || responseBody.Contains("unsupported_format", StringComparison.OrdinalIgnoreCase)
            || responseBody.Contains("does not support the format", StringComparison.OrdinalIgnoreCase)
            || (responseBody.Contains("\"param\": \"file\"", StringComparison.OrdinalIgnoreCase)
                && responseBody.Contains("invalid_value", StringComparison.OrdinalIgnoreCase))
            || (responseBody.Contains("\"param\":\"file\"", StringComparison.OrdinalIgnoreCase)
                && responseBody.Contains("invalid_value", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Bare MIME type without parameters (e.g. audio/webm, not audio/webm;codecs=opus).</summary>
    private static string? NormalizeMediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        var mediaType = contentType.Split(';', 2)[0].Trim();
        return string.IsNullOrWhiteSpace(mediaType) ? null : mediaType;
    }
}
