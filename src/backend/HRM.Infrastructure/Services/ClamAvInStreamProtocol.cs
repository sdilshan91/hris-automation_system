using System.Buffers.Binary;
using System.Text;
using HRM.Application.Common.Interfaces;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Pure, transport-agnostic implementation of the ClamAV <c>INSTREAM</c> wire protocol (ISSUE-101), split
/// out from <see cref="ClamAvVirusScanner"/> so the framing + response parsing can be unit-tested against an
/// in-memory <see cref="Stream"/> without a live clamd daemon.
///
/// INSTREAM is a simple length-prefixed chunk protocol:
///   1. Send the command <c>zINSTREAM\0</c> (the <c>z</c> prefix ⇒ NUL-terminated command).
///   2. For each chunk of file bytes, send a 4-byte BIG-ENDIAN unsigned length, then that many bytes.
///   3. Terminate the stream with a zero-length chunk (4 zero bytes).
///   4. clamd replies with a single NUL-terminated line, e.g. <c>stream: OK\0</c>,
///      <c>stream: Eicar-Test-Signature FOUND\0</c>, or <c>... ERROR\0</c>.
/// </summary>
internal static class ClamAvInStreamProtocol
{
    private static readonly byte[] InStreamCommand = Encoding.ASCII.GetBytes("zINSTREAM\0");

    /// <summary>
    /// Writes the full INSTREAM request (command + length-prefixed chunks + zero-length terminator) for
    /// <paramref name="content"/> to <paramref name="target"/>. Reads <paramref name="content"/> forward from
    /// its current position; the caller is responsible for resetting the position afterwards if it needs to
    /// re-read (e.g. to store the file).
    /// </summary>
    public static async Task WriteInStreamAsync(
        Stream target, Stream content, int chunkSize, CancellationToken cancellationToken)
    {
        if (chunkSize <= 0) chunkSize = 8192;

        await target.WriteAsync(InStreamCommand, cancellationToken);

        var buffer = new byte[chunkSize];
        var lengthPrefix = new byte[4];
        int read;
        while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            // clamd expects the chunk length as a 4-byte network-order (big-endian) unsigned int.
            BinaryPrimitives.WriteUInt32BigEndian(lengthPrefix, (uint)read);
            await target.WriteAsync(lengthPrefix, cancellationToken);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        // Zero-length chunk = end-of-stream marker.
        var terminator = new byte[4];
        await target.WriteAsync(terminator, cancellationToken);
        await target.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Reads clamd's reply from <paramref name="source"/> until the terminating NUL byte (or end of stream)
    /// and returns it as an ASCII string.
    /// </summary>
    public static async Task<string> ReadResponseAsync(Stream source, CancellationToken cancellationToken)
    {
        var buffer = new byte[256];
        using var accumulator = new MemoryStream();
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            accumulator.Write(buffer, 0, read);
            // clamd terminates the response line with a single NUL byte.
            if (buffer[read - 1] == 0)
                break;
        }
        return Encoding.ASCII.GetString(accumulator.ToArray());
    }

    /// <summary>
    /// Maps a raw clamd INSTREAM reply to a <see cref="VirusScanResult"/>:
    /// <list type="bullet">
    /// <item><c>... OK</c> ⇒ <see cref="VirusScanResult.Clean"/>.</item>
    /// <item><c>stream: {signature} FOUND</c> ⇒ <see cref="VirusScanResult.Infected"/> with the signature.</item>
    /// <item><c>... ERROR</c> or an unrecognized reply ⇒ throws <see cref="ClamAvProtocolException"/> so the
    /// caller can apply its fail-open / fail-closed policy (same path as a socket failure).</item>
    /// </list>
    /// </summary>
    public static VirusScanResult ParseResponse(string response)
    {
        var trimmed = (response ?? string.Empty).Replace("\0", string.Empty).Trim();

        if (trimmed.EndsWith("FOUND", StringComparison.Ordinal))
        {
            // Format: "stream: {signature} FOUND". Strip the trailing token + the leading "stream:" label.
            var body = trimmed[..^"FOUND".Length].Trim();
            var colon = body.IndexOf(':');
            if (colon >= 0)
                body = body[(colon + 1)..].Trim();
            return VirusScanResult.Infected(
                string.IsNullOrWhiteSpace(body) ? "Unknown" : body, response?.Trim());
        }

        if (trimmed.EndsWith("OK", StringComparison.Ordinal))
            return VirusScanResult.Clean();

        // "... ERROR" or anything unexpected — surface as a scan failure, not a (mis)detection.
        throw new ClamAvProtocolException(
            string.IsNullOrWhiteSpace(trimmed) ? "Empty response from ClamAV." : trimmed);
    }
}

/// <summary>
/// Raised when clamd returns an <c>ERROR</c> reply or an unparseable response. Treated by
/// <see cref="ClamAvVirusScanner"/> as a scan failure subject to the fail-open / fail-closed policy.
/// </summary>
internal sealed class ClamAvProtocolException : Exception
{
    public ClamAvProtocolException(string message) : base(message) { }
}
