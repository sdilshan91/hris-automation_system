namespace HRM.Tests.Unit.Helpers;

/// <summary>
/// BUG-058 fallout: the upload paths (employee document/photo, resume, self-assessment attachment) now
/// sniff real magic bytes server-side via <c>FileSignatureValidator</c>. Pre-existing upload fixtures fed
/// zero/dummy bytes, which the validator now correctly rejects as <c>invalid_file_type</c>. These helpers
/// return minimal payloads whose LEADING bytes are the true signature for the declared content type — just
/// enough to pass the sniffer on the happy path. (Not full valid images: <c>ImageMetadataStripper</c> fails
/// open on a non-decodable image, so a correct header alone is sufficient for the upload flow.)
/// </summary>
public static class UploadTestBytes
{
    public static readonly byte[] Pdf = { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };          // %PDF-1.7
    public static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    public static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
    public static readonly byte[] Zip = { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00 };           // PK.. (docx/xlsx)
    public static readonly byte[] Ole = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };           // OLE2 (doc/xls)

    private const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>The signature header for <paramref name="contentType"/> (defaults to PDF for unmapped types).</summary>
    public static byte[] For(string? contentType) => contentType switch
    {
        "image/png" => Png,
        "image/jpeg" => Jpeg,
        Docx or Xlsx => Zip,
        "application/msword" or "application/vnd.ms-excel" => Ole,
        _ => Pdf,
    };

    /// <summary>Header for <paramref name="contentType"/> followed by the caller's <paramref name="trailing"/>
    /// bytes — keeps size/content round-trip assertions meaningful while passing the sniffer.</summary>
    public static byte[] Prefixed(string? contentType, params byte[] trailing) => [.. For(contentType), .. trailing];

    /// <summary>Header for <paramref name="contentType"/> zero-padded to exactly <paramref name="size"/> bytes
    /// (never shorter than the header itself).</summary>
    public static byte[] Padded(string? contentType, int size)
    {
        var header = For(contentType);
        var data = new byte[Math.Max(size, header.Length)];
        Array.Copy(header, data, header.Length);
        return data;
    }

    /// <summary>A seekable stream of header bytes (optionally padded to <paramref name="size"/>).</summary>
    public static MemoryStream Stream(string? contentType, int size = 0)
        => new(size <= 0 ? For(contentType) : Padded(contentType, size));
}
