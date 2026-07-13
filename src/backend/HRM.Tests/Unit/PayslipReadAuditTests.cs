// ============================================================================
// BUG-083 (P2-2 PII-read audit): the two payslip DOWNLOAD query handlers expose full
// salary (a deliberate sensitive reveal), so after a successful, authorized read each
// must emit a "Payslip.ViewSensitive" audit whose After payload NAMES the exposed
// fields but stores NO monetary values. These tests drive the handlers with a mocked
// IPayslipGenerationService (success) and a REAL PayrollAuditLogger over the InMemory
// AppDbContext, then assert the persisted audit_log row.
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Application.Features.Payroll.Queries;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class PayslipReadAuditTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private ITenantContext Ctx()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(_tenant);
        ctx.IsResolved.Returns(true);
        return ctx;
    }

    private ICurrentUser User()
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(_actor);
        u.IsAuthenticated.Returns(true);
        u.Email.Returns("payroll.officer@acme.com");
        return u;
    }

    private AppDbContext Db(ITenantContext ctx)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, ctx);

    private IPayrollAuditLogger Auditor(AppDbContext db, ITenantContext ctx, ICurrentUser user)
        => new PayrollAuditLogger(db, ctx, user, NullLogger<PayrollAuditLogger>.Instance);

    private static PayslipFileDto Pdf() => new([1, 2, 3], "payslip.pdf", "application/pdf");

    // ── Single payslip download → Payslip.ViewSensitive, field-names only ───────
    [Fact]
    public async Task DownloadPayslip_emits_ViewSensitive_read_audit_with_no_salary_values()
    {
        var ctx = Ctx();
        var user = User();
        var runId = Guid.NewGuid();
        var empId = Guid.NewGuid();

        var service = Substitute.For<IPayslipGenerationService>();
        service.DownloadOneAsync(runId, empId, Arg.Any<CancellationToken>())
            .Returns(Result<PayslipFileDto>.Success(Pdf()));

        using (var db = Db(ctx))
        {
            var handler = new DownloadPayslipQueryHandler(service, Auditor(db, ctx, user));
            var result = await handler.Handle(new DownloadPayslipQuery(runId, empId), CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }

        using var read = Db(ctx);
        var row = await read.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.Action == PayrollAuditAction.PayslipViewSensitive);

        row.ResourceType.Should().Be("Payslip");
        row.ResourceId.Should().Be(empId.ToString());
        row.UserId.Should().Be(_actor);
        row.TenantId.Should().Be(_tenant);
        row.Before.Should().BeNull();

        // The After payload NAMES the exposed fields but carries NO monetary value.
        var map = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.After!)!;
        map.Keys.Should().BeEquivalentTo(new[] { "fields", "runId", "employeeId" });
        map["fields"].EnumerateArray().Select(e => e.GetString())
            .Should().Contain(new[] { "netSalary", "grossSalary", "deductions" });
    }

    // ── Bulk download → ONE run-scoped Payslip.ViewSensitive row (no per-slip flood) ──
    [Fact]
    public async Task DownloadAllPayslips_emits_single_run_scoped_ViewSensitive_audit()
    {
        var ctx = Ctx();
        var user = User();
        var runId = Guid.NewGuid();

        var service = Substitute.For<IPayslipGenerationService>();
        service.DownloadAllZipAsync(runId, Arg.Any<CancellationToken>())
            .Returns(Result<PayslipFileDto>.Success(new PayslipFileDto([9], "run.zip", "application/zip")));

        using (var db = Db(ctx))
        {
            var handler = new DownloadAllPayslipsQueryHandler(service, Auditor(db, ctx, user));
            var result = await handler.Handle(new DownloadAllPayslipsQuery(runId), CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }

        using var read = Db(ctx);
        var rows = await read.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.Action == PayrollAuditAction.PayslipViewSensitive)
            .ToListAsync();

        rows.Should().ContainSingle(); // audited ONCE per bulk call, not per payslip.
        var row = rows[0];
        row.ResourceType.Should().Be("Payslip");
        row.ResourceId.Should().Be(runId.ToString());
        row.UserId.Should().Be(_actor);

        var map = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.After!)!;
        map.Keys.Should().BeEquivalentTo(new[] { "fields", "runId", "scope" });
        map["scope"].GetString().Should().Be("run-all");
    }

    // ── A FAILED read does NOT write a sensitive-reveal audit ───────────────────
    [Fact]
    public async Task Failed_download_writes_no_read_audit()
    {
        var ctx = Ctx();
        var user = User();
        var runId = Guid.NewGuid();
        var empId = Guid.NewGuid();

        var service = Substitute.For<IPayslipGenerationService>();
        service.DownloadOneAsync(runId, empId, Arg.Any<CancellationToken>())
            .Returns(Result<PayslipFileDto>.Failure("Payslip not generated.", 404));

        using (var db = Db(ctx))
        {
            var handler = new DownloadPayslipQueryHandler(service, Auditor(db, ctx, user));
            var result = await handler.Handle(new DownloadPayslipQuery(runId, empId), CancellationToken.None);
            result.IsSuccess.Should().BeFalse();
        }

        using var read = Db(ctx);
        (await read.AuditLogs.IgnoreQueryFilters()
            .AnyAsync(a => a.Action == PayrollAuditAction.PayslipViewSensitive))
            .Should().BeFalse();
    }
}
