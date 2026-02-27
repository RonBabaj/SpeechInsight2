namespace SpeechInsight.Client.Models;

/// <summary>Single segment of a transcription (speaker, time range, text). Mirrors API response.</summary>
public sealed class TranscriptionSegmentDto
{
    public string? Speaker { get; set; }
    public double? StartSeconds { get; set; }
    public double? EndSeconds { get; set; }
    public string? Text { get; set; }
}
