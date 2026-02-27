namespace SpeechInsight.Client.Models;

/// <summary>Response from GET /api/audio/limits.</summary>
public sealed class AudioLimitsDto
{
    public long MaxFileSizeBytes { get; set; }
    public int MaxDurationSeconds { get; set; }
    public string[] AllowedExtensions { get; set; } = Array.Empty<string>();
}
