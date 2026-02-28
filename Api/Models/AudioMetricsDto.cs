namespace SpeechInsight.Api.Models;

/// <summary>
/// Non-AI metrics returned in the analyze/details response: duration, word count, speaking rate (wpm),
/// and clarity score/notes (estimate from fillers and pace). Used by the client Metrics card.
/// </summary>
public sealed class AudioMetricsDto
{
    /// <summary>Audio duration in seconds (from WAV or provider).</summary>
    public double? DurationSeconds { get; set; }
    /// <summary>Server-computed word count (fillers excluded).</summary>
    public int WordCount { get; set; }
    /// <summary>Words per minute (from word count and duration).</summary>
    public double? SpeakingRate { get; set; }
    /// <summary>Clarity estimate 0–100 (from fillers, rate, etc.). Not a judgment.</summary>
    public int? ClarityScore { get; set; }
    /// <summary>Short explanation that this is an estimate.</summary>
    public string? ClarityNotes { get; set; }
}
