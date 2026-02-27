namespace SpeechInsight.Client.Models;

/// <summary>Structured response from POST /api/audio/analyze/details. Mirrors API DTO.</summary>
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
    public Dictionary<string, object>? Metadata { get; set; }
}
