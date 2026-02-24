// Abstraction for plain-text transcription (used by POST /api/audio/analyze).
namespace SpeechInsight.Api.Services;

public interface ITranscriptionService
{
    Task<string> TranscribeAsync(Stream audioStream);
}
