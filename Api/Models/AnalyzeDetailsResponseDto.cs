namespace SpeechInsight.Api.Models;

/// <summary>Structured response for POST /api/audio/analyze/details.</summary>
public sealed class AnalyzeDetailsResponseDto
{
    public string? Text { get; set; }
    public string? Model { get; set; }
    public double? DurationSeconds { get; set; }
    public List<TranscriptionSegmentDto>? Segments { get; set; }
    public bool Diarized { get; set; }
    public bool DurationExceedsRecommended { get; set; }
    /// <summary>Number of words in the transcription (server-computed).</summary>
    public int WordCount { get; set; }
    /// <summary>Detected language code (e.g. "en"). From provider when available, else heuristic.</summary>
    public string? DetectedLanguage { get; set; }
    /// <summary>Confidence score 0–1. Heuristic from transcription length, duration, word count (see TranscriptionTextAnalyzer and README).</summary>
    public double? ConfidenceScore { get; set; }
    /// <summary>Short summary (3–5 bullets or paragraph) from AI analysis.</summary>
    public string? Summary { get; set; }
    /// <summary>Sentiment: label and score (-1 to 1).</summary>
    public SentimentResultDto? Sentiment { get; set; }
    /// <summary>Key topics or keywords (max 5).</summary>
    public List<string>? Topics { get; set; }
    /// <summary>Metrics: duration, word count, speaking rate, clarity score and notes.</summary>
    public AudioMetricsDto? Metrics { get; set; }
    /// <summary>Optional confidence or provider-specific metadata.</summary>
    public Dictionary<string, object>? Metadata { get; set; }
}
