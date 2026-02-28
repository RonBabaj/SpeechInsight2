namespace SpeechInsight.Client.Models;

/// <summary>
/// Structured response from POST /api/audio/analyze/details. Mirrors the API DTO.
/// Includes transcription, segments, core analysis (duration, word count, language, confidence),
/// and advanced insights (summary, sentiment, topics, metrics with clarity).
/// </summary>
public sealed class AnalyzeDetailsResponseDto
{
    public string? Text { get; set; }
    public string? Model { get; set; }
    public double? DurationSeconds { get; set; }
    public List<TranscriptionSegmentDto>? Segments { get; set; }
    public bool Diarized { get; set; }
    public bool DurationExceedsRecommended { get; set; }
    public int WordCount { get; set; }
    public string? DetectedLanguage { get; set; }
    public double? ConfidenceScore { get; set; }
    /// <summary>Short summary from AI (2–4 sentences or bullets); null if insights failed.</summary>
    public string? Summary { get; set; }
    /// <summary>Sentiment label and score from AI; null if insights failed.</summary>
    public SentimentResultDto? Sentiment { get; set; }
    /// <summary>Up to 5 topic/keyword strings from AI; null or empty if insights failed.</summary>
    public List<string>? Topics { get; set; }
    /// <summary>Metrics: duration, word count, speaking rate, clarity score and notes.</summary>
    public AudioMetricsDto? Metrics { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
