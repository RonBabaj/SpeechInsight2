// Handles file upload validation, plain and detailed transcription, and returns structured errors (JSON message/detail).
using Microsoft.AspNetCore.Mvc;
using SpeechInsight.Api.Models;
using SpeechInsight.Api.Options;
using SpeechInsight.Api.Services;

namespace SpeechInsight.Api.Controllers;

[Route("api/audio")]
[ApiController]
public class AudioController : ControllerBase
{
    private readonly ITranscriptionService _transcriptionService;
    private readonly IAudioAnalysisService _audioAnalysisService;
    private readonly TranscriptionOptions _options;
    private readonly ILogger<AudioController> _logger;

    public AudioController(
        ITranscriptionService transcriptionService,
        IAudioAnalysisService audioAnalysisService,
        Microsoft.Extensions.Options.IOptions<TranscriptionOptions> options,
        ILogger<AudioController> logger)
    {
        _transcriptionService = transcriptionService;
        _audioAnalysisService = audioAnalysisService;
        _options = options.Value;
        _logger = logger;
    }

    [HttpGet("limits")]
    public IActionResult GetLimits() => Ok(new AudioLimitsDto
    {
        MaxFileSizeBytes = _options.MaxFileSizeBytes,
        MaxDurationSeconds = _options.MaxDurationSeconds,
        AllowedExtensions = _options.AllowedExtensions
    });

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromForm] IFormFile? audioFile)
    {
        var validation = ValidateFile(audioFile);
        if (validation != null) return validation;

        try
        {
            await using var stream = audioFile!.OpenReadStream();
            var text = await _transcriptionService.TranscribeAsync(stream);
            return Content(text, "text/plain");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("OPENAI_API_KEY"))
        {
            return Error(500, "Server is missing OPENAI_API_KEY. Add it to the API .env file.");
        }
        catch (OpenAITranscriptionException ex)
        {
            return Error((int)ex.StatusCode, FriendlyMessage(ex.StatusCode), ex.ResponseBody);
        }
        catch (HttpRequestException ex)
        {
            return Error(502, "Transcription service error.", ex.Message);
        }
    }

    [HttpPost("analyze/details")]
    public async Task<IActionResult> AnalyzeDetails([FromForm] IFormFile? audioFile, [FromQuery] bool diarize = true)
    {
        var validation = ValidateFile(audioFile);
        if (validation != null) return validation;

        try
        {
            await using var sourceStream = audioFile!.OpenReadStream();
            using var copy = new MemoryStream();
            await sourceStream.CopyToAsync(copy);
            copy.Position = 0;

            var response = await _audioAnalysisService.AnalyzeAsync(
                copy,
                audioFile.FileName,
                audioFile.ContentType,
                diarize);

            _logger.LogInformation(
                "Analysis success: model={Model}, durationSec={Duration}, words={Words}, language={Lang}",
                response.Model, response.DurationSeconds, response.WordCount, response.DetectedLanguage);

            return Ok(response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("OPENAI_API_KEY"))
        {
            return Error(500, "Server is missing OPENAI_API_KEY. Add it to the API .env file.");
        }
        catch (OpenAITranscriptionException ex)
        {
            return Error((int)ex.StatusCode, FriendlyMessage(ex.StatusCode), ex.ResponseBody);
        }
        catch (HttpRequestException ex)
        {
            return Error(502, "Transcription service error.", ex.Message);
        }
    }

    private IActionResult? ValidateFile(IFormFile? audioFile)
    {
        if (audioFile == null || audioFile.Length == 0)
            return Error(400, "No file uploaded or file is empty. Use form field name: audioFile.");

        if (audioFile.Length > _options.MaxFileSizeBytes)
            return Error(400, $"File too large. Maximum size is {_options.MaxFileSizeBytes / (1024 * 1024)} MB.");

        var ext = Path.GetExtension(audioFile.FileName);
        if (string.IsNullOrEmpty(ext) || !_options.AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return Error(400, $"File type not allowed. Allowed: {string.Join(", ", _options.AllowedExtensions)}");

        return null;
    }

    private static string FriendlyMessage(System.Net.HttpStatusCode status)
    {
        return status switch
        {
            System.Net.HttpStatusCode.BadRequest => "Invalid request to transcription service.",
            System.Net.HttpStatusCode.Unauthorized => "Invalid or missing API key. Check OPENAI_API_KEY.",
            System.Net.HttpStatusCode.Forbidden => "Access denied. Check your OpenAI plan and API key.",
            System.Net.HttpStatusCode.TooManyRequests => "Rate limit or quota exceeded. Check your OpenAI plan and billing.",
            System.Net.HttpStatusCode.InternalServerError => "Transcription service error. Try again later.",
            _ => $"Transcription failed ({(int)status})."
        };
    }

    private IActionResult Error(int code, string message, string? detail = null)
    {
        var body = new { message, detail };
        return StatusCode(code, body);
    }
}
