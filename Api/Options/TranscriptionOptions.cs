// Binds to appsettings.json "Transcription" section: file limits, allowed extensions, provider, and model names.
namespace SpeechInsight.Api.Options;

public class TranscriptionOptions
{
    public const string SectionName = "Transcription";

    /// <summary>Provider identifier; e.g. "OpenAI". Used to select ITranscriptionService / ITranscriptionDetailsService implementation.</summary>
    public string Provider { get; set; } = "OpenAI";

    public long MaxFileSizeBytes { get; set; } = 25 * 1024 * 1024;
    public int MaxDurationSeconds { get; set; } = 900;
    public string[] AllowedExtensions { get; set; } = { ".mp3", ".mpga", ".m4a", ".wav", ".webm", ".mp4", ".mpeg" };
    public string DefaultModel { get; set; } = "whisper-1";
    public string DiarizeModel { get; set; } = "gpt-4o-transcribe-diarize";
}
