using System.Net.Http.Json;
using System.Text.Json;
using SpeechInsight.Client.Models;

namespace SpeechInsight.Client.Services;

/// <summary>
/// Central client for all UI → API communication. All HTTP calls to the audio/transcription API go through this service.
/// Components do not build URLs or use HttpClient directly. Handles multipart/form-data (file and microphone recording),
/// returns typed DTOs (AnalyzeDetailsResponseDto, AudioLimitsDto), and throws AudioApiException with user-readable messages.
/// </summary>
public sealed class AudioApiClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AudioApiClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>Gets upload limits and allowed extensions from the API.</summary>
    public async Task<AudioLimitsDto> GetLimitsAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("api/audio/limits", ct);
        await EnsureSuccessAsync(response);
        var result = await response.Content.ReadFromJsonAsync<AudioLimitsDto>(JsonOptions, ct);
        return result ?? new AudioLimitsDto();
    }

    /// <summary>Uploads audio (file or recording) and returns detailed transcription. Uses the same endpoint as file uploads.</summary>
    /// <param name="audioContent">Audio stream (from file or microphone recording).</param>
    /// <param name="fileName">Display name for the part (e.g. "recording.webm" or original file name).</param>
    /// <param name="contentType">Optional MIME type (e.g. "audio/webm", "audio/wav").</param>
    /// <param name="diarize">Whether to use speaker-diarization model.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<AnalyzeDetailsResponseDto> AnalyzeDetailsAsync(
        Stream audioContent,
        string fileName,
        string? contentType,
        bool diarize,
        CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var streamContent = new StreamContent(audioContent);
        if (!string.IsNullOrWhiteSpace(contentType))
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(streamContent, "audioFile", fileName);

        var url = $"api/audio/analyze/details?diarize={diarize.ToString().ToLowerInvariant()}";
        var response = await _http.PostAsync(url, form, ct);

        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response);
            return null!; // unreachable
        }

        var result = await response.Content.ReadFromJsonAsync<AnalyzeDetailsResponseDto>(JsonOptions, ct);
        if (result == null)
            throw new AudioApiException(0, "The server returned an unexpected response. Please try again.");
        return result;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        await ThrowApiExceptionAsync(response);
    }

    private static async Task ThrowApiExceptionAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var statusCode = (int)response.StatusCode;
        string message = statusCode switch
        {
            400 => "Invalid request. Check file type and size.",
            401 => "API key invalid or missing.",
            403 => "Access denied. Check your OpenAI plan.",
            429 => "Rate limit or quota exceeded. Check billing.",
            500 => "Server error. Try again later.",
            502 => "Transcription service error.",
            _ => string.IsNullOrEmpty(body) ? response.ReasonPhrase ?? "Request failed." : body
        };

        string? detail = null;
        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var msgEl))
                message = msgEl.GetString() ?? message;
            if (doc.RootElement.TryGetProperty("detail", out var detEl))
                detail = detEl.GetString();
        }
        catch { /* use default message */ }

        throw new AudioApiException(statusCode, message, detail);
    }
}
