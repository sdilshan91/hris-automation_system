namespace HRM.Application.Common.Security;

/// <summary>
/// The on-disk envelope that marks a stored file as encrypted (ISSUE-359).
///
/// <para><b>Why an envelope is mandatory rather than nice-to-have.</b> The rollout tolerates legacy plaintext
/// on read — there are years of existing files — so read-time must be able to tell "this is ciphertext" from
/// "this was written before encryption" with certainty. Without a marker that decision becomes guesswork:
/// heuristics like "does it parse as a PDF" would mis-classify a legitimately encrypted file whose ciphertext
/// happens to start with plausible bytes, and would hand the caller garbage instead of a document.</para>
///
/// <para><b>Layout:</b> <c>MAGIC(5) ‖ VERSION(1) ‖ PAYLOAD</c>. The magic is deliberately non-printable-ish and
/// specific to this platform so it cannot collide with a real document's leading bytes; PDFs start
/// <c>%PDF-</c>, PNGs <c>\x89PNG</c>, ZIP/DOCX/XLSX <c>PK\x03\x04</c>, none of which match.</para>
///
/// <para><b>The version byte is the important part.</b> The current payload is a single buffered
/// Data-Protection blob, which is the right shape while every caller already buffers whole files. If streaming
/// (chunked AEAD) is ever needed, it lands as version 2 and <b>files already written as version 1 keep
/// decrypting untouched</b> — which is what makes starting buffered a reversible decision rather than a
/// shortcut. Never reuse a version number for a different layout.</para>
///
/// <para>The key id is NOT carried here: the Data-Protection payload already embeds which ring key sealed it,
/// so duplicating it in the header would create two sources of truth that could disagree.</para>
/// </summary>
public static class FileEnvelope
{
    /// <summary>Magic prefix: 'H','R','M','F', 0x1A. The trailing 0x1A (SUB) keeps it out of text documents.</summary>
    public static readonly byte[] Magic = [0x48, 0x52, 0x4D, 0x46, 0x1A];

    /// <summary>Version 1: a single buffered Data-Protection payload covering the whole file.</summary>
    public const byte Version1 = 1;

    /// <summary>Bytes before the payload begins.</summary>
    public static int HeaderLength => Magic.Length + 1;

    /// <summary>
    /// True when <paramref name="content"/> begins with a recognised envelope. A file shorter than the header
    /// cannot be one, and is therefore legacy — never treat "too short to tell" as encrypted, or a small legacy
    /// file would be handed to the decryptor and fail.
    /// </summary>
    public static bool IsEncrypted(ReadOnlySpan<byte> content)
    {
        if (content.Length < HeaderLength)
            return false;

        for (var i = 0; i < Magic.Length; i++)
        {
            if (content[i] != Magic[i])
                return false;
        }

        return true;
    }

    /// <summary>The envelope version, or null when this is not an envelope.</summary>
    public static byte? VersionOf(ReadOnlySpan<byte> content) =>
        IsEncrypted(content) ? content[Magic.Length] : null;

    /// <summary>Prefixes a sealed payload with the envelope header.</summary>
    public static byte[] Wrap(byte[] payload, byte version = Version1)
    {
        var result = new byte[HeaderLength + payload.Length];
        Magic.CopyTo(result, 0);
        result[Magic.Length] = version;
        payload.CopyTo(result, HeaderLength);
        return result;
    }

    /// <summary>The sealed payload, without the header. Caller must have checked <see cref="IsEncrypted"/>.</summary>
    public static byte[] Unwrap(byte[] content) => content[HeaderLength..];
}
