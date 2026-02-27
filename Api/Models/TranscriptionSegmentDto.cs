namespace SpeechInsight.Api.Models;

/// <summary>Single segment of a transcription (speaker, time range, text).</summary>
public sealed class TranscriptionSegmentDto
{
    public string? Speaker { get; set; }
    public double? StartSeconds { get; set; }
    public double? EndSeconds { get; set; }
    public string? Text { get; set; }
}
