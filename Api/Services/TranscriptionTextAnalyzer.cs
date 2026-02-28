// ---------------------------------------------------------------------------------------------------------------------
// TranscriptionTextAnalyzer: reusable, testable text analysis for transcription.
// - Word count: tokenization with filler artifacts (um, uh, [inaudible], etc.) excluded.
// - Filler count: used for clarity scoring (ComputeClarityEstimate).
// - Language heuristic: script-based (Hebrew/Cyrillic/Latin) when provider does not supply language.
// - Confidence heuristic: evidence-based 0–1 from length, duration, word count (see README).
// - Clarity estimate: 0–100 score and notes from speaking rate and filler ratio; described as estimate only.
// Used by AudioAnalysisService. See README "Analysis pipeline & metrics" and "Analysis and insights: feature overview".
// ---------------------------------------------------------------------------------------------------------------------
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

    /// <summary>Count of filler-like tokens (um, uh, eh, [inaudible], etc.) for clarity scoring.</summary>
    public static int CountFillerWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var tokens = text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        var count = 0;
        foreach (var t in tokens)
        {
            var trimmed = t.Trim();
            if (trimmed.Length > 0 && IsFillerArtifact(trimmed))
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
    /// Low WPM (&lt;20) → 0.7; high WPM (&gt;250) → 0.85; very short text (&lt;10 chars) → 0.6; empty/zero words/error → 0.
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

        if (transcriptionLength == 0 || wordCount == 0)
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

    /// <summary>
    /// Computes a clarity score 0–100 and short notes from speaking rate and filler ratio.
    /// This is an estimate for delivery clarity (pace and fillers), not a judgment of content.
    /// </summary>
    /// <param name="wordCount">Total meaningful words (e.g. from CountWords).</param>
    /// <param name="durationSeconds">Audio duration when available; used for words-per-minute.</param>
    /// <param name="fillerCount">Number of filler tokens (e.g. from CountFillerWords).</param>
    /// <returns>ClarityScore 0–100 and ClarityNotes (e.g. "Estimate based on pace and filler words (some fillers). Not a judgment of content.").</returns>
    public static (int ClarityScore, string ClarityNotes) ComputeClarityEstimate(
        int wordCount,
        double? durationSeconds,
        int fillerCount)
    {
        if (wordCount <= 0)
            return (0, "No speech detected. Clarity is an estimate based on word count, pace, and fillers.");

        double? wordsPerMinute = null;
        if (durationSeconds.HasValue && durationSeconds.Value > 0)
            wordsPerMinute = wordCount / (durationSeconds.Value / 60.0);

        int score = 100;
        var reasons = new List<string>();

        if (fillerCount > 0)
        {
            var fillerRatio = (double)fillerCount / wordCount;
            if (fillerRatio > 0.15) { score -= 25; reasons.Add("many fillers"); }
            else if (fillerRatio > 0.08) { score -= 15; reasons.Add("some fillers"); }
        }

        if (wordsPerMinute.HasValue)
        {
            if (wordsPerMinute.Value < 60) { score -= 10; reasons.Add("slow pace"); }
            else if (wordsPerMinute.Value > 180) { score -= 5; reasons.Add("fast pace"); }
        }

        score = Math.Clamp(score, 0, 100);
        var notes = reasons.Count > 0
            ? $"Estimate based on pace and filler words ({string.Join(", ", reasons)}). Not a judgment of content."
            : "Estimate based on pace and filler words. Not a judgment of content.";
        return (score, notes);
    }
}
