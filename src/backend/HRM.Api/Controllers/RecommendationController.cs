using HRM.Application.DTOs;
using HRM.Application.Features.Performance.Commands;
using HRM.Application.Features.Performance.DTOs;
using HRM.Application.Features.Performance.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped endpoints for performance-based recommendations (US-PRF-010). The full workspace + auto-generate +
/// save/override + submit + summary + budget/rule config require <c>Performance.Publish.All</c> (HR). The workspace
/// GET + single-recommendation GET also admit <c>Performance.Review.Team</c> (a manager), and the service hard-scopes
/// a manager to their direct reports (AC-5/NFR-5 — no general employee access). Approve/reject admits either (the
/// service enforces the caller IS the current approver). All operations are tenant-scoped via the EF global query
/// filter (NFR-2). On final approval a clearly-marked downstream-integration seam is raised (BR-6 — Core HR /
/// Payroll / Training; real wiring deferred). Compensation fields are a documented encryption seam (NFR-3 — no
/// pgcrypto/PII-encryption mechanism exists; stored plain numeric today, same decision as US-PRF-008).
/// </summary>
[ApiController]
[Route("api/v1/tenant/performance/recommendations")]
[Authorize]
public sealed class RecommendationController : ControllerBase
{
    private readonly IMediator _mediator;

    public RecommendationController(IMediator mediator) => _mediator = mediator;

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    private IActionResult RecResult(HRM.Application.Common.Models.Result<RecommendationDto> result)
    {
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<RecommendationDto>.Ok(result.Value!));
    }

    // ── Completed-cycle picker (BR-1) ───────────────────────────────────

    /// <summary>
    /// GET /api/v1/tenant/performance/recommendations/cycles/completed — the completed-cycle picker (BR-1 /
    /// BUG-244 #6). Returns cycles with published final ratings (Completed status) for the recommendation
    /// workspace/team cycle selector. Admits HR (<c>Performance.Publish.All</c>) OR a manager
    /// (<c>Performance.Review.Team</c>) — same gate as the workspace GET, since both personas pick a cycle.
    /// </summary>
    [HttpGet("cycles/completed")]
    [RequirePermission("Performance.Publish.All", "Performance.Review.Team")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CompletedCycleOptionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCompletedCycles(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetCompletedCyclesForRecommendationsQuery(), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<IReadOnlyList<CompletedCycleOptionDto>>.Ok(result.Value!));
    }

    // ── Workspace (AC-1, NFR-4) ─────────────────────────────────────────

    /// <summary>
    /// GET /api/v1/tenant/performance/recommendations/workspace?cycleId=&amp;page=1&amp;pageSize=25
    /// The recommendation workspace (AC-1): in-scope employees with final score, current grade/title/compensation,
    /// tenure, manager flag and existing recommendation. Paginated (NFR-4). HR sees all; a manager sees only their
    /// direct reports (AC-5). Defaults to the tenant's most recent cycle.
    /// </summary>
    [HttpGet("workspace")]
    [RequirePermission("Performance.Publish.All", "Performance.Review.Team")]
    [ProducesResponseType(typeof(ApiResponse<RecommendationWorkspaceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWorkspace(
        [FromQuery] Guid? cycleId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RecommendationWorkspaceQuery(new RecommendationWorkspaceQueryInput(cycleId, page ?? 1, pageSize ?? 25)),
            cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<RecommendationWorkspaceDto>.Ok(result.Value!));
    }

    /// <summary>GET /api/v1/tenant/performance/recommendations/{recommendationId} — one full recommendation (AC-5 scoped).</summary>
    [HttpGet("{recommendationId:guid}")]
    [RequirePermission("Performance.Publish.All", "Performance.Review.Team")]
    [ProducesResponseType(typeof(ApiResponse<RecommendationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid recommendationId, CancellationToken cancellationToken)
        => RecResult(await _mediator.Send(new GetRecommendationQuery(recommendationId), cancellationToken));

    // ── Auto-generate (AC-2/FR-2/BR-3) ──────────────────────────────────

    /// <summary>
    /// POST /api/v1/tenant/performance/recommendations/auto-generate — applies the tenant's active rating-threshold
    /// rules across the cycle and creates Draft SUGGESTIONS for HR review (AC-2/FR-2/BR-3). HR-only.
    /// </summary>
    [HttpPost("auto-generate")]
    [RequirePermission("Performance.Publish.All")]
    [ProducesResponseType(typeof(ApiResponse<AutoGenerateResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AutoGenerate(
        [FromBody] AutoGenerateRecommendationsRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new AutoGenerateRecommendationsCommand(request.CycleId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<AutoGenerateResultDto>.Ok(result.Value!));
    }

    // ── Manual create / override (FR-1/FR-3/BR-5) ───────────────────────

    /// <summary>
    /// POST /api/v1/tenant/performance/recommendations — create OR override a recommendation (FR-1/FR-3/BR-5).
    /// HR-only. A justification is MANDATORY when overriding an existing recommendation; a promotion requires a
    /// target grade + effective date.
    /// </summary>
    [HttpPost]
    [RequirePermission("Performance.Publish.All")]
    [ProducesResponseType(typeof(ApiResponse<RecommendationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Save([FromBody] SaveRecommendationRequest request, CancellationToken cancellationToken)
    {
        var d = request.Details;
        var input = new SaveRecommendationInput(
            request.EmployeeId, request.CycleId, request.Type,
            new RecommendationDetailsInput(d.TargetGrade, d.TargetTitle, d.EffectiveDate, d.BonusAmount, d.BonusPercent,
                d.IncrementAmount, d.IncrementPercent, d.TrainingCourse, d.CustomTypeLabel, d.BudgetId),
            request.Justification, ClientIp);
        return RecResult(await _mediator.Send(new SaveRecommendationCommand(input), cancellationToken));
    }

    // ── Submit + approve/reject (AC-3/FR-4) ─────────────────────────────

    /// <summary>
    /// POST /api/v1/tenant/performance/recommendations/{recommendationId}/submit — submit into the approval
    /// workflow (AC-3/FR-4). HR-only. Gated by BR-1 (final ratings published) + BR-2 (calibration complete if
    /// enabled). Builds the ordered approver chain and charges the budget (FR-8 soft warning).
    /// </summary>
    [HttpPost("{recommendationId:guid}/submit")]
    [RequirePermission("Performance.Publish.All")]
    [ProducesResponseType(typeof(ApiResponse<RecommendationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Submit(
        Guid recommendationId, [FromBody] SubmitRecommendationRequest request, CancellationToken cancellationToken)
    {
        var input = new SubmitRecommendationInput(recommendationId, request.ApproverEmployeeIds ?? [], ClientIp);
        return RecResult(await _mediator.Send(new SubmitRecommendationCommand(input), cancellationToken));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/recommendations/{recommendationId}/approve — the current approver approves
    /// their step (FR-4). On the last approval the downstream-integration seam is raised (BR-6).
    /// </summary>
    [HttpPost("{recommendationId:guid}/approve")]
    [RequirePermission("Performance.Publish.All", "Performance.Review.Team")]
    [ProducesResponseType(typeof(ApiResponse<RecommendationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(
        Guid recommendationId, [FromBody] DecideRecommendationRequest request, CancellationToken cancellationToken)
        => RecResult(await _mediator.Send(
            new DecideRecommendationCommand(new DecideRecommendationInput(recommendationId, true, request.Comment, ClientIp)),
            cancellationToken));

    /// <summary>
    /// POST /api/v1/tenant/performance/recommendations/{recommendationId}/reject — the current approver rejects the
    /// recommendation (FR-4). Reverses the budget charge.
    /// </summary>
    [HttpPost("{recommendationId:guid}/reject")]
    [RequirePermission("Performance.Publish.All", "Performance.Review.Team")]
    [ProducesResponseType(typeof(ApiResponse<RecommendationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(
        Guid recommendationId, [FromBody] DecideRecommendationRequest request, CancellationToken cancellationToken)
        => RecResult(await _mediator.Send(
            new DecideRecommendationCommand(new DecideRecommendationInput(recommendationId, false, request.Comment, ClientIp)),
            cancellationToken));

    // ── Summary + export (AC-4/FR-6) ────────────────────────────────────

    /// <summary>
    /// GET /api/v1/tenant/performance/recommendations/summary?cycleId= — aggregate stats (AC-4): total promotions,
    /// total bonus pool allocated, increment distribution by department, prior-cycle comparison + budget. HR-only.
    /// </summary>
    [HttpGet("summary")]
    [RequirePermission("Performance.Publish.All")]
    [ProducesResponseType(typeof(ApiResponse<RecommendationSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSummary([FromQuery] Guid? cycleId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RecommendationSummaryQuery(cycleId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<RecommendationSummaryDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/recommendations/summary/export?format=csv|xlsx&amp;cycleId= — exports the
    /// summary as a file download (FR-6, ClosedXML; PDF deferred). HR-only.
    /// </summary>
    [HttpGet("summary/export")]
    [RequirePermission("Performance.Publish.All")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportSummary(
        [FromQuery] string? format, [FromQuery] Guid? cycleId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ExportRecommendationSummaryQuery(cycleId, format ?? "csv"), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        var export = result.Value!;
        return File(export.FileContent, export.ContentType, export.FileName);
    }

    // ── Budget config (FR-8) ────────────────────────────────────────────

    /// <summary>GET /api/v1/tenant/performance/recommendations/budget?cycleId= — the cycle budget tracker (FR-8). HR-only.</summary>
    [HttpGet("budget")]
    [RequirePermission("Performance.Publish.All")]
    [ProducesResponseType(typeof(ApiResponse<RecommendationBudgetDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetBudget([FromQuery] Guid cycleId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBudgetQuery(cycleId), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<RecommendationBudgetDto?>.Ok(result.Value));
    }

    /// <summary>POST /api/v1/tenant/performance/recommendations/budget — create/update the cycle budget (FR-8). HR-only.</summary>
    [HttpPost("budget")]
    [RequirePermission("Performance.Publish.All")]
    [ProducesResponseType(typeof(ApiResponse<RecommendationBudgetDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SaveBudget([FromBody] SaveBudgetRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SaveBudgetCommand(new SaveBudgetInput(request.CycleId, request.Name, request.AllocatedAmount, request.Currency)),
            cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<RecommendationBudgetDto>.Ok(result.Value!));
    }

    // ── Rule config (FR-2) ──────────────────────────────────────────────

    /// <summary>GET /api/v1/tenant/performance/recommendations/rules — the tenant's auto-generation rules (FR-2). HR-only.</summary>
    [HttpGet("rules")]
    [RequirePermission("Performance.Publish.All")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RecommendationRuleDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListRules(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListRulesQuery(), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<IReadOnlyList<RecommendationRuleDto>>.Ok(result.Value!));
    }

    /// <summary>POST /api/v1/tenant/performance/recommendations/rules — create/update an auto-generation rule (FR-2). HR-only.</summary>
    [HttpPost("rules")]
    [RequirePermission("Performance.Publish.All")]
    [ProducesResponseType(typeof(ApiResponse<RecommendationRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SaveRule([FromBody] SaveRuleRequest request, CancellationToken cancellationToken)
    {
        var input = new SaveRuleInput(null, request.Name, request.MinFinalScore, request.RecommendedType,
            request.DefaultBonusPercent, request.DefaultIncrementPercent, request.IsActive);
        var result = await _mediator.Send(new SaveRuleCommand(input), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<RecommendationRuleDto>.Ok(result.Value!));
    }

    /// <summary>PUT /api/v1/tenant/performance/recommendations/rules/{ruleId} — update an existing rule (FR-2). HR-only.</summary>
    [HttpPut("rules/{ruleId:guid}")]
    [RequirePermission("Performance.Publish.All")]
    [ProducesResponseType(typeof(ApiResponse<RecommendationRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRule(
        Guid ruleId, [FromBody] SaveRuleRequest request, CancellationToken cancellationToken)
    {
        var input = new SaveRuleInput(ruleId, request.Name, request.MinFinalScore, request.RecommendedType,
            request.DefaultBonusPercent, request.DefaultIncrementPercent, request.IsActive);
        var result = await _mediator.Send(new SaveRuleCommand(input), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<RecommendationRuleDto>.Ok(result.Value!));
    }
}
