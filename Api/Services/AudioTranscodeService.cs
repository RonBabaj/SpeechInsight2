using System.Diagnostics;

namespace SpeechInsight.Api.Services;

/// <summary>
/// Uses ffmpeg to convert MediaRecorder output into mono PCM WAV.
/// Official OpenAI diarize examples use WAV; PCM is the safest container for gpt-4o-transcribe-diarize.
/// Supported input containers match the API list: mp3, mp4, mpeg, mpga, m4a, wav, webm (+ ogg).
/// </summary>
public sealed class AudioTranscodeService : IAudioTranscodeService
{
    // OpenAI docs: mp3, mp4, mpeg, mpga, m4a, wav, webm (+ ogg/flac in API ref).
    private static readonly HashSet<string> AllowedInputExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webm", ".wav", ".mp4", ".m4a", ".ogg", ".oga", ".mp3", ".mpeg", ".mpga", ".flac"
    };

    private readonly ILogger<AudioTranscodeService> _logger;

    public AudioTranscodeService(ILogger<AudioTranscodeService> logger) => _logger = logger;

    public async Task<byte[]?> TryConvertToWavAsync(
        byte[] audioBytes,
        string inputExtension,
        CancellationToken cancellationToken = default)
    {
        if (audioBytes.Length == 0)
            return null;

        var ext = NormalizeExtension(inputExtension);
        if (ext == null || !AllowedInputExtensions.Contains(ext))
        {
            _logger.LogWarning("Unsupported input extension for transcode: {Ext}", inputExtension);
            return null;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "speechinsight-transcode");
        Directory.CreateDirectory(tempDir);
        var id = Guid.NewGuid().ToString("N");
        var inputPath = Path.Combine(tempDir, $"{id}{ext}");
        var wavPath = Path.Combine(tempDir, $"{id}.wav");

        try
        {
            await File.WriteAllBytesAsync(inputPath, audioBytes, cancellationToken);

            // Official diarize examples use WAV. Force PCM s16le mono 24 kHz (no codec plugins required).
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments =
                    $"-hide_banner -loglevel error -y -i \"{inputPath}\" " +
                    $"-vn -ac 1 -ar 24000 -c:a pcm_s16le -f wav \"{wavPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogWarning("ffmpeg process could not start (not installed?).");
                return null;
            }

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stderr = await stderrTask;
            _ = await stdoutTask;

            if (process.ExitCode != 0 || !File.Exists(wavPath))
            {
                _logger.LogWarning(
                    "ffmpeg {Ext}→WAV failed (exit {Code}): {Error}",
                    ext,
                    process.ExitCode,
                    stderr.Trim());
                return null;
            }

            var wav = await File.ReadAllBytesAsync(wavPath, cancellationToken);
            if (!IsPcmWav(wav))
            {
                _logger.LogWarning("ffmpeg produced non-WAV or empty output from {Ext} ({Bytes} bytes).", ext, wav.Length);
                return null;
            }

            _logger.LogInformation(
                "Transcoded {Ext} ({InBytes} bytes) → PCM WAV ({OutBytes} bytes) for diarize.",
                ext,
                audioBytes.Length,
                wav.Length);
            return wav;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audio→WAV transcode failed for {Ext}.", ext);
            return null;
        }
        finally
        {
            TryDelete(inputPath);
            TryDelete(wavPath);
        }
    }

    internal static bool IsPcmWav(byte[] bytes) =>
        bytes.Length >= 44
        && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
        && bytes[8] == (byte)'W' && bytes[9] == (byte)'A' && bytes[10] == (byte)'V' && bytes[11] == (byte)'E';

    private static string? NormalizeExtension(string? inputExtension)
    {
        if (string.IsNullOrWhiteSpace(inputExtension)) return null;
        var ext = inputExtension.Trim();
        if (!ext.StartsWith('.'))
            ext = "." + ext;
        return ext.ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
