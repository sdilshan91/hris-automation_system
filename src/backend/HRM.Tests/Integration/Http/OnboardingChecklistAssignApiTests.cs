using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// BUG-441 — real HTTP integration tests for <c>POST /api/v1/onboarding/checklists</c> over a throwaway
/// Postgres container.
///
/// <para><b>The defect.</b> The assignment screen holds the full previewed task list and posted all of it
/// back in <c>additionalTasks</c>, while the service created <c>template.Tasks</c> PLUS
/// <c>input.AdditionalTasks</c> — so every template task was created twice. The duplicates landed on
/// <c>startDate + 0</c> because the payload carries <c>dueDate</c> while the legacy ad-hoc contract binds
/// <c>dueOffsetDays</c>, which the client never sent: the HR officer's inline due-date edits (FR-6) were
/// silently discarded on every assignment. It was dormant only while <c>/checklists/preview</c> 404'd.</para>
///
/// <para><b>The fix under test.</b> An explicit replace mode: the new <c>resolvedTasks</c> field is the
/// AUTHORITATIVE task set — created verbatim, with the template NOT expanded a second time, carrying
/// concrete due dates. <c>additionalTasks</c> keeps its old meaning ("extras on top of the template") and
/// is covered here too, because the whole point of adding a field rather than reinterpreting one is that
/// existing callers must not move.</para>
///
/// <para>Runs as <c>admin@hrm.local</c> on the seeded <c>platform</c> tenant through the genuine
/// HTTP → controller → MediatR → Npgsql path, and re-reads every assignment with
/// <c>GET /checklists/{id}</c> so the assertions are about ROWS, not about the response object the write
/// path happened to build.</para>
/// </summary>
[Collection("HttpApi")]
public sealed class OnboardingChecklistAssignApiTests
{
    private const string Subdomain = "platform";
    private const string AdminEmail = "admin@hrm.local";
    private const string AdminPassword = "Admin@123!";

    private readonly ApiTestFactory _factory;

    public OnboardingChecklistAssignApiTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// THE regression arm (BUG-441). Previews, edits one due date exactly as the HR officer would, posts the
    /// resolved set back, and asserts the persisted checklist holds EXACTLY ONE instance per task with the
    /// SUPPLIED dates.
    ///
    /// <para>Against unmodified code this fails twice over: four tasks instead of two, and the extra pair on
    /// <c>startDate + 0</c> instead of the edited dates.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ONB-002-01")]
    public async Task Assign_WithResolvedTasks_CreatesEachTaskOnce_AndKeepsTheSuppliedDueDates()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        // A future joining date, so BR-4 anchors to it and a due date that accidentally equals "today"
        // cannot pass by coincidence.
        var joining = DateTime.UtcNow.Date.AddDays(10);
        var employeeId = await CreateEmployeeAsync(client, joining);
        var (templateId, _) = await CreateTemplateAsync(client);

        var preview = await GetPreviewAsync(client, employeeId, templateId);
        preview.Tasks.Should().HaveCount(2, "the template defines two tasks");

        // FR-6: the officer pushes the IT task out from start+3 to start+7. This is the edit the old route
        // threw away — it must survive the round trip.
        var editedDue = joining.AddDays(7).ToString("yyyy-MM-dd");
        var resolved = preview.Tasks.Select(t => new
        {
            templateTaskId = t.TemplateTaskId,
            title = t.Title,
            description = t.Description,
            category = t.Category,
            responsibleRole = t.ResponsibleRole,
            dueDate = t.Title == "Provision laptop and accounts" ? editedDue : t.DueDate,
            isMandatory = t.IsMandatory,
            sortOrder = t.SortOrder,
        }).ToList();

        var assign = await client.PostAsJsonAsync("/api/v1/onboarding/checklists", new
        {
            employeeId,
            templateId,
            overrideStartDate = preview.StartDate,
            resolvedTasks = resolved,
        });
        assign.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(assign));

        // Re-read from the database through the API: what matters is the rows, not the write path's echo.
        var tasks = await ReadPersistedTasksAsync(client, await ReadDataIdAsync(assign));

        tasks.Should().HaveCount(2,
            "replace mode creates exactly the supplied set — the template must NOT be expanded a second " +
            "time on top of it (BUG-441 created every task twice)");
        tasks.Select(t => t.Title).Should().OnlyHaveUniqueItems(
            "a duplicated template task shows up as the same title twice");

        var contract = tasks.Single(t => t.Title == "Sign employment contract");
        contract.DueDate.Should().Be(joining.ToString("yyyy-MM-dd"));
        contract.IsMandatory.Should().BeTrue();
        contract.SourceTemplateTaskId.Should().NotBeNull(
            "a replace-mode row that names a template task keeps its provenance, so it is not mistaken " +
            "for an ad-hoc task later");

        var laptop = tasks.Single(t => t.Title == "Provision laptop and accounts");
        laptop.DueDate.Should().Be(editedDue,
            "the officer's inline FR-6 edit must be persisted verbatim");
        laptop.DueDate.Should().NotBe(preview.StartDate,
            "startDate + 0 is the exact wrong answer BUG-441 produced");

        tasks.Should().OnlyContain(t => t.Status == "Pending");
    }

    /// <summary>
    /// Preview and assign must agree BY CONSTRUCTION: echoing the preview back unedited has to produce
    /// tasks identical to the preview, field for field. This is the property that keeps the confirmation
    /// screen honest — what the officer approved is what gets created.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ONB-002-01")]
    public async Task Assign_EchoingThePreviewUnedited_ProducesExactlyThePreviewedTasks()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var employeeId = await CreateEmployeeAsync(client, DateTime.UtcNow.Date.AddDays(6));
        var (templateId, _) = await CreateTemplateAsync(client);

        var preview = await GetPreviewAsync(client, employeeId, templateId);

        var assign = await client.PostAsJsonAsync("/api/v1/onboarding/checklists", new
        {
            employeeId,
            templateId,
            overrideStartDate = preview.StartDate,
            resolvedTasks = preview.Tasks.Select(t => new
            {
                templateTaskId = t.TemplateTaskId,
                title = t.Title,
                description = t.Description,
                category = t.Category,
                responsibleRole = t.ResponsibleRole,
                dueDate = t.DueDate,
                isMandatory = t.IsMandatory,
                sortOrder = t.SortOrder,
            }).ToList(),
        });
        assign.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(assign));

        var tasks = await ReadPersistedTasksAsync(client, await ReadDataIdAsync(assign));
        tasks.Should().HaveCount(preview.Tasks.Count);

        foreach (var previewed in preview.Tasks)
        {
            var created = tasks.Single(t => t.Title == previewed.Title);
            created.DueDate.Should().Be(previewed.DueDate, "preview promised this date");
            created.SourceTemplateTaskId.Should().Be(previewed.TemplateTaskId);
            created.ResponsibleRole.Should().Be(previewed.ResponsibleRole);
            created.IsMandatory.Should().Be(previewed.IsMandatory);
            created.SortOrder.Should().Be(previewed.SortOrder);
            // FR-3 ownership is resolved server-side, never taken from the client, but an UNEDITED row must
            // still land on the same person the preview displayed.
            created.ResponsibleUserId.Should().Be(previewed.ResponsibleUserId);
        }
    }

    /// <summary>
    /// The legacy contract is untouched: with no <c>resolvedTasks</c>, <c>additionalTasks</c> still means
    /// "extras ON TOP of the template", and their offsets still anchor to the start date. Existing callers
    /// (including the hire-conversion path, which posts an empty extras list) must not move.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ONB-002-01")]
    public async Task Assign_WithLegacyAdditionalTasks_StillExpandsTemplateAndAppendsTheExtras()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var joining = DateTime.UtcNow.Date.AddDays(10);
        var employeeId = await CreateEmployeeAsync(client, joining);
        var (templateId, _) = await CreateTemplateAsync(client);

        var assign = await client.PostAsJsonAsync("/api/v1/onboarding/checklists", new
        {
            employeeId,
            templateId,
            additionalTasks = new[]
            {
                new
                {
                    title = "Order security badge",
                    description = (string?)null,
                    category = "Facilities",
                    responsibleRole = "HR",
                    dueOffsetDays = 5,
                    isMandatory = false,
                    sortOrder = 0,
                },
            },
        });
        assign.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(assign));

        var tasks = await ReadPersistedTasksAsync(client, await ReadDataIdAsync(assign));

        tasks.Should().HaveCount(3,
            "legacy semantics: the template's two tasks PLUS the one extra — unchanged by BUG-441's fix");
        tasks.Should().Contain(t => t.Title == "Sign employment contract");
        tasks.Should().Contain(t => t.Title == "Provision laptop and accounts");

        var extra = tasks.Single(t => t.Title == "Order security badge");
        extra.SourceTemplateTaskId.Should().BeNull("an ad-hoc task has no template provenance (FR-5)");
        extra.DueDate.Should().Be(joining.AddDays(5).ToString("yyyy-MM-dd"),
            "the legacy field is still an OFFSET from the anchored start date");

        // And the template rows still land on their own offsets, as before the fix.
        tasks.Single(t => t.Title == "Provision laptop and accounts").DueDate
            .Should().Be(joining.AddDays(3).ToString("yyyy-MM-dd"));
    }

    /// <summary>
    /// Precedence is explicit, not guessed: sending BOTH task sets is a 400. Silently letting one win would
    /// discard a set of tasks the officer entered — the same invisible data loss BUG-441 was.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ONB-002-01")]
    public async Task Assign_WithBothTaskSets_Returns400()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var employeeId = await CreateEmployeeAsync(client, DateTime.UtcNow.Date.AddDays(4));
        var (templateId, _) = await CreateTemplateAsync(client);
        var preview = await GetPreviewAsync(client, employeeId, templateId);

        var response = await client.PostAsJsonAsync("/api/v1/onboarding/checklists", new
        {
            employeeId,
            templateId,
            resolvedTasks = preview.Tasks.Select(t => new
            {
                templateTaskId = t.TemplateTaskId,
                title = t.Title,
                responsibleRole = t.ResponsibleRole,
                dueDate = t.DueDate,
                isMandatory = t.IsMandatory,
                sortOrder = t.SortOrder,
            }).ToList(),
            additionalTasks = new[]
            {
                new { title = "Order security badge", responsibleRole = "HR", dueOffsetDays = 2, isMandatory = false, sortOrder = 0 },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await BodyAsync(response));
    }

    /// <summary>
    /// BR-3: replace mode must not become the one write path where a mandatory task can be dropped —
    /// <c>ModifyAsync</c> already refuses to remove one, and an authoritative task set that simply omits it
    /// would be the same removal by another name.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ONB-002-01")]
    public async Task Assign_WithResolvedTasksOmittingAMandatoryTask_Returns400()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var employeeId = await CreateEmployeeAsync(client, DateTime.UtcNow.Date.AddDays(4));
        var (templateId, _) = await CreateTemplateAsync(client);
        var preview = await GetPreviewAsync(client, employeeId, templateId);

        // Drop "Sign employment contract" (isMandatory = true) and keep only the optional row.
        var response = await client.PostAsJsonAsync("/api/v1/onboarding/checklists", new
        {
            employeeId,
            templateId,
            resolvedTasks = preview.Tasks.Where(t => !t.IsMandatory).Select(t => new
            {
                templateTaskId = t.TemplateTaskId,
                title = t.Title,
                responsibleRole = t.ResponsibleRole,
                dueDate = t.DueDate,
                isMandatory = t.IsMandatory,
                sortOrder = t.SortOrder,
            }).ToList(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await BodyAsync(response));
        (await response.Content.ReadAsStringAsync()).Should().Contain("Sign employment contract");
    }

    /// <summary>
    /// A resolved row may add an ad-hoc task (no <c>templateTaskId</c>) alongside the template's rows — the
    /// officer can still add work on the assignment screen without reopening the duplication hole.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ONB-002-01")]
    public async Task Assign_WithResolvedTasksIncludingAnAdHocRow_CreatesItOnceWithItsOwnDate()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var joining = DateTime.UtcNow.Date.AddDays(8);
        var employeeId = await CreateEmployeeAsync(client, joining);
        var (templateId, _) = await CreateTemplateAsync(client);
        var preview = await GetPreviewAsync(client, employeeId, templateId);

        var adHocDue = joining.AddDays(2).ToString("yyyy-MM-dd");
        var rows = preview.Tasks.Select(t => new
        {
            templateTaskId = (Guid?)t.TemplateTaskId,
            title = t.Title,
            responsibleRole = t.ResponsibleRole,
            dueDate = t.DueDate,
            isMandatory = t.IsMandatory,
            sortOrder = t.SortOrder,
        }).ToList();
        rows.Add(new
        {
            templateTaskId = (Guid?)null,
            title = "Order security badge",
            responsibleRole = "HR",
            dueDate = adHocDue,
            isMandatory = false,
            sortOrder = 2,
        });

        var assign = await client.PostAsJsonAsync("/api/v1/onboarding/checklists", new
        {
            employeeId,
            templateId,
            overrideStartDate = preview.StartDate,
            resolvedTasks = rows,
        });
        assign.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(assign));

        var tasks = await ReadPersistedTasksAsync(client, await ReadDataIdAsync(assign));
        tasks.Should().HaveCount(3);

        var extra = tasks.Single(t => t.Title == "Order security badge");
        extra.SourceTemplateTaskId.Should().BeNull();
        extra.DueDate.Should().Be(adHocDue, "an ad-hoc replace-mode row carries a concrete date too");
    }

    /// <summary>
    /// A <c>templateTaskId</c> that is not part of the template being assigned is rejected rather than
    /// quietly demoted to an ad-hoc task — a stale or foreign id must not change what gets assigned.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ONB-002-01")]
    public async Task Assign_WithResolvedTaskReferencingAForeignTemplateTask_Returns400()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var employeeId = await CreateEmployeeAsync(client, DateTime.UtcNow.Date.AddDays(4));
        var (templateId, _) = await CreateTemplateAsync(client);
        var preview = await GetPreviewAsync(client, employeeId, templateId);

        var rows = preview.Tasks.Select(t => new
        {
            templateTaskId = (Guid?)t.TemplateTaskId,
            title = t.Title,
            responsibleRole = t.ResponsibleRole,
            dueDate = t.DueDate,
            isMandatory = t.IsMandatory,
            sortOrder = t.SortOrder,
        }).ToList();
        rows.Add(new
        {
            templateTaskId = (Guid?)Guid.NewGuid(),
            title = "Task from somewhere else",
            responsibleRole = "HR",
            dueDate = preview.StartDate,
            isMandatory = false,
            sortOrder = 2,
        });

        var response = await client.PostAsJsonAsync("/api/v1/onboarding/checklists", new
        {
            employeeId,
            templateId,
            resolvedTasks = rows,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await BodyAsync(response));
    }

    // ── helpers ──────────────────────────────────────────────────────

    private sealed record PreviewTask(
        Guid TemplateTaskId, string Title, string? Description, string? Category, string ResponsibleRole,
        Guid? ResponsibleUserId, string DueDate, bool IsMandatory, int SortOrder);

    private sealed record Preview(string StartDate, IReadOnlyList<PreviewTask> Tasks);

    private sealed record PersistedTask(
        string Title, Guid? SourceTemplateTaskId, string ResponsibleRole, Guid? ResponsibleUserId,
        string DueDate, string Status, bool IsMandatory, int SortOrder);

    private static async Task<Preview> GetPreviewAsync(HttpClient client, Guid employeeId, Guid templateId)
    {
        var response = await client.GetAsync(
            $"/api/v1/onboarding/checklists/preview?employeeId={employeeId}&templateId={templateId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(response));

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");

        return new Preview(
            data.GetProperty("startDate").GetString()!,
            data.GetProperty("tasks").EnumerateArray().Select(t => new PreviewTask(
                t.GetProperty("templateTaskId").GetGuid(),
                t.GetProperty("title").GetString()!,
                t.GetProperty("description").GetString(),
                t.GetProperty("category").GetString(),
                t.GetProperty("responsibleRole").GetString()!,
                t.GetProperty("responsibleUserId").ValueKind == JsonValueKind.Null
                    ? null
                    : t.GetProperty("responsibleUserId").GetGuid(),
                t.GetProperty("dueDate").GetString()!,
                t.GetProperty("isMandatory").GetBoolean(),
                t.GetProperty("sortOrder").GetInt32())).ToList());
    }

    /// <summary>Re-reads the assigned checklist so assertions are about persisted rows, not the echo.</summary>
    private static async Task<IReadOnlyList<PersistedTask>> ReadPersistedTasksAsync(
        HttpClient client, Guid checklistInstanceId)
    {
        var response = await client.GetAsync($"/api/v1/onboarding/checklists/{checklistInstanceId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(response));

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("tasks").EnumerateArray()
            .Select(t => new PersistedTask(
                t.GetProperty("title").GetString()!,
                t.GetProperty("sourceTemplateTaskId").ValueKind == JsonValueKind.Null
                    ? null
                    : t.GetProperty("sourceTemplateTaskId").GetGuid(),
                t.GetProperty("responsibleRole").GetString()!,
                t.GetProperty("responsibleUserId").ValueKind == JsonValueKind.Null
                    ? null
                    : t.GetProperty("responsibleUserId").GetGuid(),
                t.GetProperty("dueDate").GetString()!,
                t.GetProperty("statusName").GetString()!,
                t.GetProperty("isMandatory").GetBoolean(),
                t.GetProperty("sortOrder").GetInt32()))
            .ToList();
    }

    /// <summary>Creates a department + job title + employee via the real API (mirrors the preview tests).</summary>
    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, DateTime dateOfJoining)
    {
        var suffix = Suffix();

        var dept = await client.PostAsJsonAsync("/api/v1/tenant/departments", new
        {
            name = $"Onboarding Assign {suffix}",
            code = $"OAS-{suffix}",
        });
        dept.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(dept));
        var departmentId = await ReadDataIdAsync(dept);

        var title = await client.PostAsJsonAsync("/api/v1/tenant/job-titles", new
        {
            titleName = $"Assign Engineer {suffix}",
        });
        title.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(title));
        var jobTitleId = await ReadDataIdAsync(title);

        var employee = await client.PostAsJsonAsync("/api/v1/tenant/employees", new
        {
            firstName = "Grace",
            lastName = $"Hopper {suffix}",
            email = $"grace.{suffix}@onb-assign.test",
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
    /// A universal active template with two tasks whose offsets DIFFER (0 and 3) and whose mandatory flags
    /// differ — so "everything on the start date" and "mandatory rules never exercised" both fail.
    /// </summary>
    private static async Task<(Guid Id, string Name)> CreateTemplateAsync(HttpClient client)
    {
        var suffix = Suffix();
        var name = $"Assign Template {suffix}";

        var create = await client.PostAsJsonAsync("/api/v1/onboarding/templates", new
        {
            templateName = name,
            description = "Assign replace-mode integration test (BUG-441)",
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
