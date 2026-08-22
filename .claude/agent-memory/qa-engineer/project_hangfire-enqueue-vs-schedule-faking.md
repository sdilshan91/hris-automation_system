---
name: hangfire-enqueue-vs-schedule-faking
description: How to unit-test Hangfire enqueue-vs-schedule-vs-neither with an NSubstitute IBackgroundJobClient
metadata:
  type: project
---

Testing whether code enqueued / scheduled / did-nothing with Hangfire: fake `IBackgroundJobClient`
and assert on the lowered `Create(Job, IState)` call — `Enqueue<T>(...)` and `Schedule<T>(..., delay)`
are extension methods that translate to `client.Create(Job.FromExpression(...), new EnqueuedState())`
and `... new ScheduledState(delay))` respectively.

**Why:** you cannot verify the extension methods directly; NSubstitute only sees the real interface member
`Create`. Discriminate by the `IState` runtime type.

**How to apply** (US-NTF-006 SendEmailJob/RealNotificationDispatcher tests):
- enqueued: `jobs.Received(1).Create(Arg.Is<Job>(j => j.Method.Name == "RunAsync"), Arg.Is<IState>(s => s is EnqueuedState))`
- scheduled: `Arg.Is<IState>(s => s is ScheduledState)`
- neither: `jobs.DidNotReceive().Create(Arg.Any<Job>(), Arg.Any<IState>())`
- usings: `Hangfire`, `Hangfire.Common` (Job), `Hangfire.States` (EnqueuedState/ScheduledState). Hangfire flows
  transitively via the HRM.Api project reference — no extra PackageReference in HRM.Tests.

A positive `Received(1)` on the state type validates the negative `DidNotReceive` in sibling tests (proves the
lowering is what you think). Dispatcher/job open their own DI scope via `IServiceScopeFactory`: register a real
`ITenantContext`+InMemory `AppDbContext` (shared db-name, [[attendance-tc-conventions]]-style) so the scope's
tenant context and DbContext are the same pair; fake `IEmailSender`/`INotificationPreferenceService`/
`IEmailTemplateService` as singletons to control decisions. Job loads by id + EXPLICIT tenant id (query filter is
bypassed because ITenantContext is unresolved in the job scope) — the explicit predicate is the isolation guard.
