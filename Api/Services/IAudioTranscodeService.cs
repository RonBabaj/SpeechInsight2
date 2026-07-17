namespace SpeechInsight.Api.Services;

/// <summary>Converts browser microphone audio to MP3 for OpenAI diarization models.</summary>
public interface IAudioTranscodeService
{
    /// <summary>
    /// Transcodes audio bytes to mono 24 kHz MP3 via ffmpeg.
    /// <paramref name="inputExtension"/> must include the dot (e.g. ".webm", ".wav").
    /// Returns null when ffmpeg is unavailable or conversion fails.
    /// </summary>
    Task<byte[]?> TryConvertToMp3Async(byte[] audioBytes, string inputExtension, CancellationToken cancellationToken = default);
}
