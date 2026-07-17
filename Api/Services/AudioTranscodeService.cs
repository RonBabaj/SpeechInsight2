using System.Diagnostics;

namespace SpeechInsight.Api.Services;

/// <summary>
/// Uses ffmpeg to transcode browser MediaRecorder output (webm/mp4/ogg/wav)
/// into MP3 that gpt-4o-transcribe-diarize accepts reliably.
/// </summary>
public sealed class AudioTranscodeService : IAudioTranscodeService
{
    private static readonly HashSet<string> AllowedInputExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webm", ".wav", ".mp4", ".m4a", ".ogg", ".oga", ".mp3", ".mpeg", ".mpga"
    };

    private readonly ILogger<AudioTranscodeService> _logger;

    public AudioTranscodeService(ILogger<AudioTranscodeService> logger) => _logger = logger;

    public async Task<byte[]?> TryConvertToMp3Async(
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

        // Already MP3 — still re-encode to a known-good profile for the diarize model.
        var tempDir = Path.Combine(Path.GetTempPath(), "speechinsight-transcode");
        Directory.CreateDirectory(tempDir);
        var id = Guid.NewGuid().ToString("N");
        var inputPath = Path.Combine(tempDir, $"{id}{ext}");
        var mp3Path = Path.Combine(tempDir, $"{id}.mp3");

        try
        {
            await File.WriteAllBytesAsync(inputPath, audioBytes, cancellationToken);

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                // Force a clean mono 24 kHz CBR MP3 — format used in working diarize examples.
                Arguments =
                    $"-hide_banner -loglevel error -y -i \"{inputPath}\" " +
                    $"-vn -ac 1 -ar 24000 -codec:a libmp3lame -b:a 64k \"{mp3Path}\"",
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

            if (process.ExitCode != 0 || !File.Exists(mp3Path))
            {
                _logger.LogWarning(
                    "ffmpeg {Ext}→MP3 failed (exit {Code}): {Error}",
                    ext,
                    process.ExitCode,
                    stderr.Trim());
                return null;
            }

            var mp3 = await File.ReadAllBytesAsync(mp3Path, cancellationToken);
            if (mp3.Length == 0)
            {
                _logger.LogWarning("ffmpeg produced empty MP3 from {Ext}.", ext);
                return null;
            }

            _logger.LogInformation(
                "Transcoded {Ext} ({InBytes} bytes) → MP3 ({OutBytes} bytes).",
                ext,
                audioBytes.Length,
                mp3.Length);
            return mp3;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audio→MP3 transcode failed for {Ext}.", ext);
            return null;
        }
        finally
        {
            TryDelete(inputPath);
            TryDelete(mp3Path);
        }
    }

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
