using System.Diagnostics;

namespace SpeechInsight.Api.Services;

/// <summary>Uses ffmpeg to transcode browser-recorded WAV into MP3 accepted by gpt-4o-transcribe-diarize.</summary>
public sealed class AudioTranscodeService : IAudioTranscodeService
{
    private readonly ILogger<AudioTranscodeService> _logger;

    public AudioTranscodeService(ILogger<AudioTranscodeService> logger) => _logger = logger;

    public async Task<byte[]?> TryConvertWavToMp3Async(byte[] wavBytes, CancellationToken cancellationToken = default)
    {
        if (wavBytes.Length < 44 || !IsRiffWav(wavBytes))
            return null;

        var tempDir = Path.Combine(Path.GetTempPath(), "speechinsight-transcode");
        Directory.CreateDirectory(tempDir);
        var id = Guid.NewGuid().ToString("N");
        var wavPath = Path.Combine(tempDir, $"{id}.wav");
        var mp3Path = Path.Combine(tempDir, $"{id}.mp3");

        try
        {
            await File.WriteAllBytesAsync(wavPath, wavBytes, cancellationToken);

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                // 24 kHz mono CBR MP3 — format used in working diarize examples.
                Arguments = $"-hide_banner -loglevel error -y -i \"{wavPath}\" -ac 1 -ar 24000 -codec:a libmp3lame -b:a 64k \"{mp3Path}\"",
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

            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0 || !File.Exists(mp3Path))
            {
                _logger.LogWarning("ffmpeg WAV→MP3 failed (exit {Code}): {Error}", process.ExitCode, stderr.Trim());
                return null;
            }

            var mp3 = await File.ReadAllBytesAsync(mp3Path, cancellationToken);
            return mp3.Length > 0 ? mp3 : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WAV→MP3 transcode failed.");
            return null;
        }
        finally
        {
            TryDelete(wavPath);
            TryDelete(mp3Path);
        }
    }

    private static bool IsRiffWav(byte[] bytes) =>
        bytes.Length >= 12
        && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
        && bytes[8] == (byte)'W' && bytes[9] == (byte)'A' && bytes[10] == (byte)'V' && bytes[11] == (byte)'E';

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
