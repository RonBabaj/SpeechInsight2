namespace SpeechInsight.Client.Models;

/// <summary>
/// Sentiment result from the analyze/details API (AI insights). Used by SentimentCard.
/// </summary>
public sealed class SentimentResultDto
{
    /// <summary>One of: Positive, Neutral, Negative, Mixed.</summary>
    public string Label { get; set; } = "";
    /// <summary>Score from -1.0 (negative) to 1.0 (positive).</summary>
    public double Score { get; set; }
}
