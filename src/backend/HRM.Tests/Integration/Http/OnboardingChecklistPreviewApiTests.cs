using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP integration tests for <c>GET /api/v1/onboarding/checklists/preview</c> (ISSUE-374).
///
/// <para>The route did not exist: <c>checklist-assignment.component.ts</c> called it on every template
/// selection and got a 404, so the HR officer's "preview before assigning" step (US-ONB-002 FR-2/BR-4,
/// TC-ONB-002-01 step 2) silently failed.</para>
///
/// <para><b>The arm that matters most is <see cref="Preview_PersistsNothing"/>.</b> A preview resolves what
/// WOULD be assigned; if it ever creates a checklist row it would (a) trip BR-2's one-active-checklist rule
/// for a template the officer merely clicked on, and (b) push the real assign into the unique-violation
/// idempotency path for no reason. The precedent is <c>AttendancePolicyResolver</c>, which deliberately
/// never lazily creates a policy row because a payroll run must not write policy as a side effect. That arm
/// counts rows across the whole database (query filters ignored) before and after, so it fails on a write to
/// ANY tenant, not just the one under test.</para>
///
/// <para>Runs as <c>admin@hrm.local</c> on the seeded <c>platform</c> tenant over the genuine
/// HTTP → controller → MediatR → Npgsql path against a throwaway Postgres container.</para>
/// </summary>
[Collection("HttpApi")]
public sealed class OnboardingChecklistPreviewApiTests
{
    private const string Subdomain = "platform";
    private const string AdminEmail = "admin@hrm.local";
    private const string AdminPassword = "Admin@123!";

    private readonly ApiTestFactory _factory;

    public OnboardingChecklistPreviewApiTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// AC-1/FR-2/BR-4: the resolved preview for a real employee + template — the FE's <c>IChecklistPreview</c>
    /// shape, with due dates anchored to a FUTURE joining date (so a due date equal to "today" cannot pass by
    /// accident) and the FR-3 responsible party resolved.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ONB-002-01")]
    public async Task Preview_ForEmployeeAndTemplate_ReturnsResolvedTasksWithCalculatedDueDates()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        // Joining 10 days out: BR-4 anchors to the joining date because it is not in the past.
        var joining = DateTime.UtcNow.Date.AddDays(10);
        var employeeId = await CreateEmployeeAsync(client, joining);
        var (templateId, templateName) = await CreateTemplateAsync(client);

        var response = await client.GetAsync(
            $"/api/v1/onboarding/checklists/preview?employeeId={employeeId}&templateId={templateId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(response));

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");

        data.GetProperty("employeeId").GetGuid().Should().Be(employeeId);
        data.GetProperty("employeeName").GetString().Should().StartWith("Ada ");
        data.GetProperty("templateId").GetGuid().Should().Be(templateId);
        data.GetProperty("templateName").GetString().Should().Be(templateName);
        data.GetProperty("startDate").GetString().Should().Be(
            joining.ToString("yyyy-MM-dd"),
            "BR-4 anchors the offsets to the joining date when it is not in the past");

        var tasks = data.GetProperty("tasks").EnumerateArray().ToList();
        tasks.Should().HaveCount(2, "the template defines two tasks and preview creates no others");

        // Ordered by the template's sortOrder, renumbered 0..n like assign does.
        tasks[0].GetProperty("title").GetString().Should().Be("Sign employment contract");
        tasks[0].GetProperty("category").GetString().Should().Be("Paperwork");
        tasks[0].GetProperty("responsibleRole").GetString().Should().Be("HR");
        tasks[0].GetProperty("dueOffsetDays").GetInt32().Should().Be(0);
        tasks[0].GetProperty("dueDate").GetString().Should().Be(joining.ToString("yyyy-MM-dd"));
        tasks[0].GetProperty("isMandatory").GetBoolean().Should().BeTrue();
        tasks[0].GetProperty("sortOrder").GetInt32().Should().Be(0);
        tasks[0].GetProperty("status").GetString().Should().Be(
            "pending", "the FE ChecklistTaskStatus union is lowercase snake, not the PascalCase enum name");

        // FR-2: the second task's due date is start + its own offset, not start + 0.
        tasks[1].GetProperty("title").GetString().Should().Be("Provision laptop and accounts");
        tasks[1].GetProperty("dueOffsetDays").GetInt32().Should().Be(3);
        tasks[1].GetProperty("dueDate").GetString().Should().Be(joining.AddDays(3).ToString("yyyy-MM-dd"));
        tasks[1].GetProperty("sortOrder").GetInt32().Should().Be(1);

        // FR-3: the HR-role task resolves to the assigning officer, so responsible-party resolution really ran.
        tasks[0].GetProperty("responsibleUserId").GetGuid().Should().NotBeEmpty();
        // The FE contract carries responsibleName; it is present (null when the user has no employee record).
        tasks[0].TryGetProperty("responsibleName", out _).Should().BeTrue();

        // "id" is absent in a fresh preview (models/onboarding-checklist.models.ts) — nothing exists to id.
        tasks[0].TryGetProperty("id", out _).Should().BeFalse(
            "a previewed task has no instance id because no instance was created");
    }

    /// <summary>
    /// THE assertion: a preview is a pure read. Counts every onboarding write table across the WHOLE database
    /// (query filters ignored, so a write to any tenant is caught) before and after three preview calls.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ONB-002-01")]
    public async Task Preview_PersistsNothing()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var employeeId = await CreateEmployeeAsync(client, DateTime.UtcNow.Date.AddDays(5));
        var (templateId, _) = await CreateTemplateAsync(client);

        // Assign the SAME template to a DIFFERENT employee first. Two reasons, both load-bearing:
        //   1. It lifts the baseline off zero, so "counts unchanged" cannot pass because the counters are
        //      blind — an assert of 0 == 0 would be green even against a counter wired to nothing.
        //   2. It proves the counters see exactly the rows an assign writes, which is the write the preview
        //      must not perform.
        var otherEmployeeId = await CreateEmployeeAsync(client, DateTime.UtcNow.Date.AddDays(5));
        var assign = await client.PostAsJsonAsync("/api/v1/onboarding/checklists", new
        {
            employeeId = otherEmployeeId,
            templateId,
            additionalTasks = Array.Empty<object>(),
        });
        assign.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(assign));

        var before = await CountOnboardingRowsAsync();
        before.Instances.Should().BeGreaterThan(0, "the assign above must have been counted");
        before.Tasks.Should().BeGreaterThan(0, "the assign above wrote task instances");

        // Three calls, because the failure mode to rule out is "the first one creates it and the rest are
        // idempotent" — a single call could not tell that apart from a genuine no-write.
        for (var i = 0; i < 3; i++)
        {
            var response = await client.GetAsync(
                $"/api/v1/onboarding/checklists/preview?employeeId={employeeId}&templateId={templateId}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(response));
        }

        var after = await CountOnboardingRowsAsync();

        after.Instances.Should().Be(before.Instances, "preview must not create a checklist instance");
        after.Tasks.Should().Be(before.Tasks, "preview must not create task instances");
        after.Outbox.Should().Be(before.Outbox, "preview must not queue assignment notifications");

        // And the employee is still assignable: no active checklist exists, so AC-3's replace/merge prompt
        // stays off and the subsequent assign takes the ordinary create path.
        var existing = await client.GetAsync($"/api/v1/onboarding/checklists/employee/{employeeId}");
        existing.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(existing));
        using var doc = JsonDocument.Parse(await existing.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").ValueKind.Should().Be(
            JsonValueKind.Null, "previewing a template must leave the employee with no assigned checklist");
    }

    /// <summary>BR-4: a joining date in the past clamps the anchor to today — the same rule assign applies.</summary>
    [Fact]
    [Trait("TC", "TC-ONB-002-01")]
    public async Task Preview_WithPastJoiningDate_AnchorsToToday()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var employeeId = await CreateEmployeeAsync(client, DateTime.UtcNow.Date.AddDays(-30));
        var (templateId, _) = await CreateTemplateAsync(client);

        var response = await client.GetAsync(
            $"/api/v1/onboarding/checklists/preview?employeeId={employeeId}&templateId={templateId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(response));

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");

        data.GetProperty("startDate").GetString().Should().Be(today);
        data.GetProperty("tasks").EnumerateArray().First()
            .GetProperty("dueDate").GetString().Should().Be(today);
    }

    /// <summary>
    /// BR-1: preview mirrors assign's refusal of an inactive template, so the screen can never show a task set
    /// that the confirm button would then reject.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ONB-002-01")]
    public async Task Preview_ForInactiveTemplate_Returns409()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var employeeId = await CreateEmployeeAsync(client, DateTime.UtcNow.Date.AddDays(5));
        var (templateId, _) = await CreateTemplateAsync(client);

        var deactivate = await client.PostAsync(
            $"/api/v1/onboarding/templates/{templateId}/deactivate", content: null);
        deactivate.IsSuccessStatusCode.Should().BeTrue(await BodyAsync(deactivate));

        var response = await client.GetAsync(
            $"/api/v1/onboarding/checklists/preview?employeeId={employeeId}&templateId={templateId}");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await BodyAsync(response));
    }

    /// <summary>404 for an employee that does not exist in the caller's tenant — same as assign.</summary>
    [Fact]
    [Trait("TC", "TC-ONB-002-01")]
    public async Task Preview_ForUnknownEmployee_Returns404()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);
        var (templateId, _) = await CreateTemplateAsync(client);

        var response = await client.GetAsync(
            $"/api/v1/onboarding/checklists/preview?employeeId={Guid.NewGuid()}&templateId={templateId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await BodyAsync(response));
    }

    /// <summary>
    /// The preview reveals exactly the task set assign would create, so it is gated by the same
    /// <c>Onboarding.Manage</c> permission. A genuinely permission-less authenticated caller gets 403.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ONB-002-01")]
    public async Task Preview_WithoutOnboardingManage_Returns403()
    {
        var client = await _factory.CreateClientWithPermissionsAsync();

        var response = await client.GetAsync(
            $"/api/v1/onboarding/checklists/preview?employeeId={Guid.NewGuid()}&templateId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await BodyAsync(response));
    }

    // ── helpers ──────────────────────────────────────────────────────

    private sealed record OnboardingRowCounts(int Instances, int Tasks, int Outbox);

    /// <summary>
    /// Counts the three onboarding write tables with query filters IGNORED — a preview that wrote a row under
    /// some other tenant (or a soft-deleted one) must still fail this.
    /// </summary>
    private async Task<OnboardingRowCounts> CountOnboardingRowsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return new OnboardingRowCounts(
            await db.OnboardingChecklistInstances.IgnoreQueryFilters().CountAsync(),
            await db.OnboardingTaskInstances.IgnoreQueryFilters().CountAsync(),
            await db.OnboardingNotificationOutbox.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>Creates a department + job title + employee via the real API (mirrors CoreHrApiTests).</summary>
    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, DateTime dateOfJoining)
    {
        var suffix = Suffix();

        var dept = await client.PostAsJsonAsync("/api/v1/tenant/departments", new
        {
            name = $"Onboarding Preview {suffix}",
            code = $"OPV-{suffix}",
        });
        dept.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(dept));
        var departmentId = await ReadDataIdAsync(dept);

        var title = await client.PostAsJsonAsync("/api/v1/tenant/job-titles", new
        {
            titleName = $"Preview Engineer {suffix}",
        });
        title.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(title));
        var jobTitleId = await ReadDataIdAsync(title);

        var employee = await client.PostAsJsonAsync("/api/v1/tenant/employees", new
        {
            firstName = "Ada",
            lastName = $"Lovelace {suffix}",
            email = $"ada.{suffix}@onb-preview.test",
            dateOfJoining = dateOfJoining.ToString("yyyy-MM-dd"),
            departmentId,
            jobTitleId,
            employmentType = "FullTime",
            status = "Active",
        });
        employee.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(employee));

        return await ReadDataIdAsync(employee);
    }

    /// <summary>
    /// Creates a universal (no department/job-title restriction) active template with two tasks whose offsets
    /// differ, so a preview that returned "start date for everything" cannot pass.
    /// </summary>
    private static async Task<(Guid Id, string Name)> CreateTemplateAsync(HttpClient client)
    {
        var suffix = Suffix();
        var name = $"Preview Template {suffix}";

        var create = await client.PostAsJsonAsync("/api/v1/onboarding/templates", new
        {
            templateName = name,
            description = "Preview endpoint integration test (ISSUE-374)",
            isActive = true,
            tasks = new[]
            {
                new
                {
                    title = "Sign employment contract",
                    description = "Collect the signed contract.",
                    category = "Paperwork",
                    responsibleRole = "HR",
                    dueOffsetDays = 0,
                    isMandatory = true,
                    sortOrder = 0,
                },
                new
                {
                    title = "Provision laptop and accounts",
                    description = "Set up hardware and accounts.",
                    category = "IT",
                    responsibleRole = "IT",
                    dueOffsetDays = 3,
                    isMandatory = false,
                    sortOrder = 1,
                },
            },
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(create));
        return (await ReadDataIdAsync(create), name);
    }

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<string> BodyAsync(HttpResponseMessage response)
        => $"Response body: {await response.Content.ReadAsStringAsync()}";

    private static async Task<Guid> ReadDataIdAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("id").GetGuid();
    }
}
