namespace HRM.Application.Common.Models;

/// <summary>
/// GAP-027 — the bytes of a stored file, streamed from an authenticated endpoint.
/// </summary>
/// <remarks>
/// <para>
/// One shape for every file-serving surface, rather than a per-feature record each time. The alternative
/// is what this codebase already paid for twice: BUG-307 was ten hand-written copies of one lookup, and
/// BUG-311 a second hand-written description of one wire contract.
/// </para>
/// <para>
/// Exists because <c>IFileStorage.GetSignedUrl</c> fabricates <c>/files/{tenantId}/{path}</c> — a scheme no
/// route has ever served. Streaming is also the only way these stay authenticated: a bare URL navigation
/// carries no Authorization header, which is precisely why real deployments use pre-signed URLs and why a
/// half-built signing scheme is worse than none.
/// </para>
/// </remarks>
public sealed record StoredFileResult(
    byte[] Content,
    string ContentType,
    string FileName);
