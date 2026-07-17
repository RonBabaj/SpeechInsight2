namespace SpeechInsight.Api.Services;

/// <summary>Converts browser microphone WAV to MP3 for stricter OpenAI diarization models.</summary>
public interface IAudioTranscodeService
{
    /// <summary>WAV (RIFF) → mono MP3. Returns null when ffmpeg is unavailable or conversion fails.</summary>
    Task<byte[]?> TryConvertWavToMp3Async(byte[] wavBytes, CancellationToken cancellationToken = default);
}
