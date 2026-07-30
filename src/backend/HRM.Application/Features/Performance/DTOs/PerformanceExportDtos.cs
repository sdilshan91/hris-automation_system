namespace HRM.Application.Features.Performance.DTOs;

/// <summary>
/// A rendered performance-export file (bytes + name + content type). Shared by the deferred PDF exports
/// (360 report, review-meeting record, PIP) so each carries the same file-download shape as the dashboard /
/// recommendation exports. The renderer lives in Infrastructure (QuestPDF); the read path that produces the
/// source DTO enforces the same authorization + tenant query filter as the JSON route it sits beside.
/// </summary>
public sealed record PerformanceExportFile(byte[] FileContent, string FileName, string ContentType);
