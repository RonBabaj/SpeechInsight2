namespace SpeechInsight.Api.Services;

/// <summary>
/// AI-based insights from transcribed text: summary, sentiment (label + score), and topics (max 5).
/// Implementations call an LLM (e.g. OpenAI Chat); results are structured only—no free-form narrative.
/// </summary>
public interface ITextInsightsService
{
    /// <summary>
    /// Derives summary, sentiment, and topics from the given transcription.
    /// </summary>
    /// <param name="transcriptionText">Full transcript; may be truncated by the implementation.</param>
    /// <param name="cancellationToken">Optional cancellation.</param>
    /// <returns>Structured result; empty input may return defaults (e.g. Neutral, empty topics).</returns>
    Task<TextInsightsResult> GetInsightsAsync(string transcriptionText, CancellationToken cancellationToken = default);
}

/// <summary>Structured result of text insights: summary text, sentiment label/score, and topic strings.</summary>
public sealed class TextInsightsResult
{
    /// <summary>Short summary (2–4 sentences or bullets) from the model; null if not available.</summary>
    public string? Summary { get; set; }
    /// <summary>One of: Positive, Neutral, Negative, Mixed.</summary>
    public string SentimentLabel { get; set; } = "Neutral";
    /// <summary>Numeric sentiment from -1.0 (negative) to 1.0 (positive).</summary>
    public double SentimentScore { get; set; }
    /// <summary>Up to 5 topic or keyword phrases.</summary>
    public List<string> Topics { get; set; } = new();
}
