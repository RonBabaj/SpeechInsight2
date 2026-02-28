namespace SpeechInsight.Api.Models;

/// <summary>Sentiment analysis result: label and numeric score.</summary>
public sealed class SentimentResultDto
{
    /// <summary>One of: Positive, Neutral, Negative, Mixed.</summary>
    public string Label { get; set; } = "";

    /// <summary>Score from -1.0 (negative) to 1.0 (positive).</summary>
    public double Score { get; set; }
}
