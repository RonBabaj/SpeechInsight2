using Microsoft.Extensions.Options;
using SpeechInsight.Api.Models;
using SpeechInsight.Api.Options;

namespace SpeechInsight.Api.Services;

/// <summary>
/// Orchestrates the full analysis pipeline: (1) duration from audio stream when format is supported (e.g. WAV),
/// (2) transcription via ITranscriptionDetailsService, (3) word count and language/confidence from TranscriptionTextAnalyzer,
/// (4) map to AnalyzeDetailsResponseDto. Controllers only validate input and call this service. See README "Analysis pipeline & metrics".
/// </summary>
public sealed class AudioAnalysisService : IAudioAnalysisService
{
    private readonly IAudioDurationService _durationService;
    private readonly ITranscriptionDetailsService _transcriptionService;
    private readonly TranscriptionOptions _options;

    public AudioAnalysisService(
        IAudioDurationService durationService,
        ITranscriptionDetailsService transcriptionService,
        IOptions<TranscriptionOptions> options)
    {
        _durationService = durationService;
        _transcriptionService = transcriptionService;
        _options = options.Value;
    }

    public async Task<AnalyzeDetailsResponseDto> AnalyzeAsync(
        Stream audioStream,
        string fileName,
        string? contentType,
        bool diarize,
        CancellationToken cancellationToken = default)
    {
        if (audioStream == null)
            throw new ArgumentNullException(nameof(audioStream));

        if (!audioStream.CanSeek)
            throw new InvalidOperationException("Audio stream must be seekable for analysis.");

        audioStream.Position = 0;

        // 1. Real duration from audio when format is supported (e.g. WAV); otherwise we use provider duration later.
        double? durationSeconds = _durationService.GetDurationSeconds(audioStream, contentType, fileName);
        audioStream.Position = 0;

        // 2. Transcription from provider (may also return duration for unsupported formats).
        var details = await _transcriptionService.TranscribeDetailedAsync(
            audioStream,
            fileName,
            contentType,
            diarize);

        if (durationSeconds == null && details.DurationSeconds.HasValue)
            durationSeconds = details.DurationSeconds.Value;

        var text = details.Text ?? "";
        var wordCount = TranscriptionTextAnalyzer.CountWords(text);
        var detectedLanguage = details.Language ?? TranscriptionTextAnalyzer.DetectLanguageHeuristic(text);
        var confidenceScore = TranscriptionTextAnalyzer.ComputeConfidenceHeuristic(
            text.Length,
            durationSeconds,
            wordCount,
            hadProviderError: false);

        var durationExceedsRecommended = durationSeconds.HasValue &&
            durationSeconds.Value > _options.MaxDurationSeconds;

        var response = new AnalyzeDetailsResponseDto
        {
            Text = details.Text,
            Model = details.Model,
            DurationSeconds = durationSeconds,
            Segments = details.Segments
                .Select(s => new TranscriptionSegmentDto
                {
                    Speaker = s.Speaker,
                    StartSeconds = s.StartSeconds,
                    EndSeconds = s.EndSeconds,
                    Text = s.Text
                })
                .ToList(),
            Diarized = details.Diarized,
            DurationExceedsRecommended = durationExceedsRecommended,
            WordCount = wordCount,
            DetectedLanguage = detectedLanguage,
            ConfidenceScore = confidenceScore
        };

        return response;
    }
}
