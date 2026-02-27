using SpeechInsight.Api.Models;

namespace SpeechInsight.Api.Services;

/// <summary>Orchestrates audio analysis: duration from audio, transcription from provider, and derived metrics. No business logic in controllers.</summary>
public interface IAudioAnalysisService
{
    /// <summary>Runs the full analysis pipeline and returns a DTO for the client.</summary>
    Task<AnalyzeDetailsResponseDto> AnalyzeAsync(
        Stream audioStream,
        string fileName,
        string? contentType,
        bool diarize,
        CancellationToken cancellationToken = default);
}
