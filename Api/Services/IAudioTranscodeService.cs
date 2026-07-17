namespace SpeechInsight.Api.Services;

/// <summary>Converts browser microphone audio to a clean PCM WAV for OpenAI diarization.</summary>
public interface IAudioTranscodeService
{
    /// <summary>
    /// Transcodes audio bytes to mono 24 kHz 16-bit PCM WAV via ffmpeg.
    /// <paramref name="inputExtension"/> must include the dot (e.g. ".webm", ".m4a").
    /// Returns null when ffmpeg is unavailable or conversion fails.
    /// </summary>
    Task<byte[]?> TryConvertToWavAsync(byte[] audioBytes, string inputExtension, CancellationToken cancellationToken = default);
}
