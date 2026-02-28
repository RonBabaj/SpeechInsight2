// ---------------------------------------------------------------------------------------------------------------------
// AudioDurationService: computes audio duration in seconds from the raw stream.
// Supports WAV only: RIFF header, byte rate at offset 28, "data" chunk size (with validation n <= stream length - 44).
// Other formats return null; the analysis pipeline then uses the transcription provider's duration.
// Stream position is restored before return. See README "Analysis pipeline & metrics".
// ---------------------------------------------------------------------------------------------------------------------
namespace SpeechInsight.Api.Services;

public sealed class AudioDurationService : IAudioDurationService
{
    /// <inheritdoc />
    public double? GetDurationSeconds(Stream audioStream, string? contentType, string? fileName)
    {
        if (audioStream == null || !audioStream.CanRead || !audioStream.CanSeek)
            return null;

        var isWav = IsWavFormat(contentType, fileName);
        if (!isWav)
            return null;

        try
        {
            return TryGetWavDurationSeconds(audioStream);
        }
        catch
        {
            return null;
        }
        finally
        {
            try { audioStream.Position = 0; } catch { /* best-effort restore */ }
        }
    }

    private static bool IsWavFormat(string? contentType, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            var ct = contentType.Trim();
            if (ct.Equals("audio/wav", StringComparison.OrdinalIgnoreCase) ||
                ct.Equals("audio/wave", StringComparison.OrdinalIgnoreCase) ||
                ct.StartsWith("audio/wav;", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        var ext = Path.GetExtension(fileName ?? "");
        return ext.Equals(".wav", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads WAV header: RIFF header, fmt chunk, and optional fact/data. Duration = data length / byte rate.</summary>
    private static double? TryGetWavDurationSeconds(Stream s)
    {
        if (s.Length < 44)
            return null;

        var buf = new byte[44];
        s.Position = 0;
        var read = s.Read(buf, 0, 44);
        if (read < 44)
            return null;

        // RIFF signature
        if (buf[0] != 0x52 || buf[1] != 0x49 || buf[2] != 0x46 || buf[3] != 0x46)
            return null;

        // Byte rate at offset 28 (4 bytes, little-endian)
        int byteRate = buf[28] | (buf[29] << 8) | (buf[30] << 16) | (buf[31] << 24);
        if (byteRate <= 0)
            return null;

        // Data size: find "data" chunk (offset 36 in standard 44-byte WAV) or use file length - 44 as fallback.
        long dataSize;
        if (s.Length >= 44)
        {
            s.Position = 36;
            var chunkId = new byte[4];
            if (s.Read(chunkId, 0, 4) == 4 &&
                chunkId[0] == 0x64 && chunkId[1] == 0x61 && chunkId[2] == 0x74 && chunkId[3] == 0x61) // "data"
            {
                var dataLenBuf = new byte[4];
                if (s.Read(dataLenBuf, 0, 4) == 4)
                {
                    var n = dataLenBuf[0] | (dataLenBuf[1] << 8) | (dataLenBuf[2] << 16) | (dataLenBuf[3] << 24);
                    var maxDataSize = s.Length - 44;
                    if (n > 0 && n <= maxDataSize)
                        dataSize = n;
                    else
                        dataSize = s.Length - 44;
                }
                else
                    dataSize = s.Length - 44;
            }
            else
                dataSize = s.Length - 44;
        }
        else
            dataSize = 0;

        if (dataSize <= 0)
            return null;

        return (double)dataSize / byteRate;
    }
}
