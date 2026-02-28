namespace SpeechInsight.Client.Models;

/// <summary>
/// Non-AI metrics from the analyze/details API: duration, word count, speaking rate (wpm),
/// and clarity score/notes (estimate). Used by MetricsCard.
/// </summary>
public sealed class AudioMetricsDto
{
    public double? DurationSeconds { get; set; }
    public int WordCount { get; set; }
    /// <summary>Words per minute (word count / duration).</summary>
    public double? SpeakingRate { get; set; }
    /// <summary>Clarity estimate 0–100 (from fillers and pace).</summary>
    public int? ClarityScore { get; set; }
    /// <summary>Short explanation that clarity is an estimate.</summary>
    public string? ClarityNotes { get; set; }
}
