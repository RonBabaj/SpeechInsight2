namespace SpeechInsight.Api.Services;

/// <summary>Computes audio duration in seconds from the raw stream (e.g. WAV via header). Returns null for unsupported or invalid formats; caller uses provider duration as fallback. See README "Analysis pipeline & metrics".</summary>
public interface IAudioDurationService
{
    /// <summary>Attempts to get duration in seconds from the audio stream. Does not consume the stream beyond reading; caller must reset position if needed.</summary>
    /// <param name="audioStream">Seekable stream (e.g. MemoryStream). Position is restored before returning.</param>
    /// <param name="contentType">Optional MIME type (e.g. audio/wav, audio/mpeg).</param>
    /// <param name="fileName">Optional file name for extension-based format detection.</param>
    /// <returns>Duration in seconds, or null if format is unsupported or invalid.</returns>
    double? GetDurationSeconds(Stream audioStream, string? contentType, string? fileName);
}
