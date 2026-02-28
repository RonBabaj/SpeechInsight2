using Microsoft.Extensions.Options;
using SpeechInsight.Api.Models;
using SpeechInsight.Api.Options;

namespace SpeechInsight.Api.Services;

// ---------------------------------------------------------------------------------------------------------------------
// AudioAnalysisService: orchestrates the full analysis pipeline for POST /api/audio/analyze/details.
// (1) Duration from audio stream when format is supported (e.g. WAV) via IAudioDurationService.
// (2) Transcription via ITranscriptionDetailsService (OpenAI); provider duration used if step 1 had none.
// (3) Word count, detected language, and confidence heuristic from TranscriptionTextAnalyzer.
// (4) AI insights (summary, sentiment, topics) via ITextInsightsService; on failure, insights are omitted.
// (5) Non-AI clarity and metrics: filler count, clarity score/notes, speaking rate (words per minute).
// (6) Build AnalyzeDetailsResponseDto with all fields. Controllers only validate input and call AnalyzeAsync.
// See README "Analysis pipeline & metrics" and "Analysis and insights: feature overview".
// ---------------------------------------------------------------------------------------------------------------------

/// <summary>
/// Orchestrates the full analysis pipeline: duration → transcription → word count/language/confidence
/// → AI insights (summary, sentiment, topics) → clarity and metrics → response DTO.
/// </summary>
public sealed class AudioAnalysisService : IAudioAnalysisService
{
    private readonly IAudioDurationService _durationService;
    private readonly ITranscriptionDetailsService _transcriptionService;
    private readonly ITextInsightsService _insightsService;
    private readonly TranscriptionOptions _options;

    public AudioAnalysisService(
        IAudioDurationService durationService,
        ITranscriptionDetailsService transcriptionService,
        ITextInsightsService insightsService,
        IOptions<TranscriptionOptions> options)
    {
        _durationService = durationService;
        _transcriptionService = transcriptionService;
        _insightsService = insightsService;
        _options = options.Value;
    }

    /// <summary>
    /// Runs the full pipeline on the given audio stream and returns the analysis DTO.
    /// </summary>
    /// <param name="audioStream">Seekable stream (e.g. uploaded file); position is reset and may be read multiple times.</param>
    /// <param name="fileName">Original file name (for provider and logging).</param>
    /// <param name="contentType">MIME type when known (e.g. audio/wav); used for duration parsing.</param>
    /// <param name="diarize">When true, use diarization model and return segments with speaker labels.</param>
    /// <param name="cancellationToken">Cancellation for transcription and insights calls.</param>
    /// <returns>AnalyzeDetailsResponseDto with text, segments, summary, sentiment, topics, metrics, and core analysis fields.</returns>
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

        // 3. AI-based insights (summary, sentiment, topics)
        TextInsightsResult? insights = null;
        try
        {
            insights = await _insightsService.GetInsightsAsync(text, cancellationToken);
        }
        catch
        {
            // Continue without insights; Summary/Sentiment/Topics remain null/empty
        }

        // 4. Non-AI clarity (speaking rate, fillers → ClarityScore and notes)
        var fillerCount = TranscriptionTextAnalyzer.CountFillerWords(text);
        var (clarityScore, clarityNotes) = TranscriptionTextAnalyzer.ComputeClarityEstimate(wordCount, durationSeconds, fillerCount);
        double? speakingRate = null;
        if (durationSeconds.HasValue && durationSeconds.Value > 0 && wordCount > 0)
            speakingRate = wordCount / (durationSeconds.Value / 60.0);

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
            ConfidenceScore = confidenceScore,
            Summary = insights?.Summary,
            Sentiment = insights != null ? new SentimentResultDto { Label = insights.SentimentLabel, Score = insights.SentimentScore } : null,
            Topics = insights?.Topics?.Take(5).ToList(),
            Metrics = new AudioMetricsDto
            {
                DurationSeconds = durationSeconds,
                WordCount = wordCount,
                SpeakingRate = speakingRate,
                ClarityScore = clarityScore,
                ClarityNotes = clarityNotes
            }
        };

        return response;
    }
}
