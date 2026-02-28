namespace SpeechInsight.Client.Services;

/// <summary>Thrown when the audio API returns an error (validation, transcription failure, etc.).</summary>
public sealed class AudioApiException : Exception
{
    public int StatusCode { get; }

    public AudioApiException(int statusCode, string message, string? detail = null)
        : base(string.IsNullOrWhiteSpace(detail) ? message : $"{message} {detail}")
    {
        StatusCode = statusCode;
    }
}
