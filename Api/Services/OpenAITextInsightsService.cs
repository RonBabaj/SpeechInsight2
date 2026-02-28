// ---------------------------------------------------------------------------------------------------------------------
// OpenAITextInsightsService: AI-based insights from transcription text.
// Calls OpenAI Chat Completions (gpt-4o-mini) to derive summary, sentiment label/score, and up to 5 topics.
// Uses conservative prompts and response_format=json_object; no psychological or medical claims.
// Used by AudioAnalysisService after transcription; if the call fails, insights are omitted (no mock fallback).
// ---------------------------------------------------------------------------------------------------------------------
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SpeechInsight.Api.Services;

public sealed class OpenAITextInsightsService : ITextInsightsService
{
    private const string ChatEndpoint = "https://api.openai.com/v1/chat/completions";
    private const string DefaultModel = "gpt-4o-mini";
    private readonly HttpClient _httpClient;

    public OpenAITextInsightsService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient();
    }

    /// <summary>
    /// Calls OpenAI Chat Completions with the given transcription and parses the response into summary,
    /// sentiment (label + score), and topics (max 5). Empty or whitespace input returns safe defaults.
    /// </summary>
    /// <param name="transcriptionText">Full transcript text; truncated to 12000 chars if longer.</param>
    /// <param name="cancellationToken">Cancellation token for the HTTP request.</param>
    /// <returns>TextInsightsResult with Summary, SentimentLabel, SentimentScore, and Topics (or defaults on parse failure).</returns>
    /// <exception cref="InvalidOperationException">When OPENAI_API_KEY is missing or the API returns a non-success status.</exception>
    public async Task<TextInsightsResult> GetInsightsAsync(string transcriptionText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transcriptionText))
            return new TextInsightsResult { Summary = null, SentimentLabel = "Neutral", SentimentScore = 0, Topics = new List<string>() };

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OPENAI_API_KEY is not set.");

        var systemPrompt = """
You are an analysis assistant. Given a transcription, return ONLY valid JSON with no markdown or explanation.
Use this exact shape:
{"summary": "2-4 sentence summary or 3-5 bullet points. Be factual and conservative. Do not diagnose or judge the speaker.", "sentimentLabel": "Positive"|"Neutral"|"Negative"|"Mixed", "sentimentScore": number from -1.0 to 1.0, "topics": ["topic1", "topic2", ...] max 5 short phrases}
Rules: No psychological or medical claims. No absolute claims. sentimentLabel must be one of the four values. topics: max 5 items.
""";

        var userContent = "Transcription:\n\n" + (transcriptionText.Length > 12000 ? transcriptionText.AsSpan(0, 12000).ToString() + "…" : transcriptionText);

        var requestBody = new
        {
            model = DefaultModel,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            },
            response_format = new { type = "json_object" },
            max_tokens = 500
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ChatEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Insights API error: {response.StatusCode}. {body}");

        return ParseInsightsResponse(body);
    }

    /// <summary>
    /// Parses the Chat Completions JSON response: extracts summary, sentimentLabel (mapped to Positive/Neutral/Negative/Mixed),
    /// sentimentScore (clamped to -1..1), and topics (up to 5). On any parse error, returns safe defaults without throwing.
    /// </summary>
    private static TextInsightsResult ParseInsightsResponse(string json)
    {
        var result = new TextInsightsResult();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
                return result;
            var content = choices[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
                return result;

            using var contentDoc = JsonDocument.Parse(content);
            var root = contentDoc.RootElement;

            if (root.TryGetProperty("summary", out var sum))
                result.Summary = sum.GetString();

            if (root.TryGetProperty("sentimentLabel", out var lbl))
            {
                var label = lbl.GetString() ?? "";
                if (label.Equals("Positive", StringComparison.OrdinalIgnoreCase)) result.SentimentLabel = "Positive";
                else if (label.Equals("Negative", StringComparison.OrdinalIgnoreCase)) result.SentimentLabel = "Negative";
                else if (label.Equals("Mixed", StringComparison.OrdinalIgnoreCase)) result.SentimentLabel = "Mixed";
                else result.SentimentLabel = "Neutral";
            }

            if (root.TryGetProperty("sentimentScore", out var sc) && sc.ValueKind == JsonValueKind.Number && sc.TryGetDouble(out var score))
                result.SentimentScore = Math.Clamp(score, -1.0, 1.0);

            if (root.TryGetProperty("topics", out var top) && top.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in top.EnumerateArray())
                {
                    var s = t.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        result.Topics.Add(s.Trim());
                    if (result.Topics.Count >= 5) break;
                }
            }
        }
        catch
        {
            // Return safe defaults
        }
        return result;
    }
}
