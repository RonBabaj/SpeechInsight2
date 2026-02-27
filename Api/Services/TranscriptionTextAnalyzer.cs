// Reusable, testable text analysis for transcription: word count, language heuristic, and confidence heuristic.
// Used by AudioAnalysisService to produce server-side metrics. See README "Analysis pipeline & metrics" for behavior.
namespace SpeechInsight.Api.Services;

public static class TranscriptionTextAnalyzer
{
    private static readonly char[] WordSeparators = { ' ', '\n', '\r', '\t', '\u00A0' };

    /// <summary>Counts words in transcription text. Normalizes whitespace and ignores empty tokens.</summary>
    public static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        var tokens = text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        var count = 0;
        foreach (var t in tokens)
        {
            var trimmed = t.Trim();
            if (trimmed.Length > 0 && !IsFillerArtifact(trimmed))
                count++;
        }
        return count;
    }

    /// <summary>Common filler or non-word artifacts that some engines emit; exclude from word count.</summary>
    private static bool IsFillerArtifact(string token)
    {
        if (token.Length <= 1) return false;
        var lower = token.ToLowerInvariant();
        return lower is "um" or "uh" or "eh" or "[inaudible]" or "[silence]" or "..." or "…";
    }

    /// <summary>Heuristic language hint from script/characters when provider does not supply it. Returns ISO 639-1 style code or null.</summary>
    public static string? DetectLanguageHeuristic(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return null;

        int hebrew = 0, cyrillic = 0, latin = 0, other = 0;
        foreach (var c in trimmed)
        {
            if (char.IsWhiteSpace(c)) continue;
            if (c >= '\u0590' && c <= '\u05FF') hebrew++;
            else if (c >= '\u0400' && c <= '\u04FF') cyrillic++;
            else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= 0x00C0 && c <= 0x024F)) latin++;
            else other++;
        }

        var total = hebrew + cyrillic + latin + other;
        if (total == 0) return null;
        if (hebrew > total / 3) return "he";
        if (cyrillic > total / 3) return "ru";
        if (latin > total / 2) return "en";
        return null;
    }

    /// <summary>
    /// Heuristic confidence 0–1, computed from evidence (transcription length, duration, word count).
    /// Not a mock: the value changes per run. See README "Analysis pipeline & metrics" for the exact patterns.
    /// Low WPM (&lt;20) → 0.7; high WPM (&gt;250) → 0.85; very short text (&lt;10 chars) → 0.6; empty/error → 0.
    /// </summary>
    /// <param name="transcriptionLength">Length of full transcription text.</param>
    /// <param name="durationSeconds">Audio duration in seconds (optional).</param>
    /// <param name="wordCount">Word count (optional).</param>
    /// <param name="hadProviderError">Whether the provider reported an error flag.</param>
    public static double ComputeConfidenceHeuristic(
        int transcriptionLength,
        double? durationSeconds,
        int wordCount,
        bool hadProviderError = false)
    {
        if (hadProviderError || (durationSeconds.HasValue && durationSeconds.Value <= 0))
            return 0.0;

        if (transcriptionLength == 0)
            return 0.0;

        double score = 1.0;
        if (durationSeconds.HasValue && durationSeconds.Value > 0 && wordCount > 0)
        {
            var wordsPerMinute = wordCount / (durationSeconds.Value / 60.0);
            if (wordsPerMinute < 20)
                score *= 0.7;
            else if (wordsPerMinute > 250)
                score *= 0.85;
        }
        if (transcriptionLength < 10)
            score *= 0.6;
        return Math.Clamp(score, 0.0, 1.0);
    }
}
