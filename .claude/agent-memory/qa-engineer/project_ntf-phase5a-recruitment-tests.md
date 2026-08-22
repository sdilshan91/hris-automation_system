---
name: ntf-phase5a-recruitment-tests
description: Phase 5a RealRecruitmentNotificationService test conventions + the offer-placeholder catalog bug the tests caught
metadata:
  type: project
---

# Notifications Phase 5a (recruitment seam) tests

Two xUnit files under `HRM.Tests/Unit/`: `RealRecruitmentNotificationServiceTests.cs` (14 cases) +
`NotificationEventCatalogPhase5aTests.cs` (25 cases). 12 new event keys, all `RecruitmentUpdates` / not-mandatory.

**Fake patterns** (reused from Phase 4): hand `RecordingDispatcher` (INotificationDispatcher, records InApp/Email
legs, opt-throw); hand `FakeEmailSender` (IEmailSender, captures `EmailMessage` list, opt-throw); hand
`FakeFileStorage` (IFileStorage, returns bytes / null / throws on `OpenReadAsync`); NSubstitute
`IEmailTemplateService` (resolving vs failing). InMemory `AppDbContext` via `TestDbContextFactory.Create(tenantCtx,
dbName)` — recruiter pool is a REAL `UserTenants⋈UserTenantRoles⋈RolePermissions` query on `Recruitment.Manage`
(seed 2 holders + 1 non-holder decoy, mirror `RealPayrollNotificationServiceTests.SeedApproverPoolAsync`).

**offer_sent PDF-vs-fallback split** (the load-bearing behaviour): with PDF bytes the candidate leg goes through
`IEmailSender` (assert single `EmailAttachment`, `application/pdf`, exact bytes) and the dispatcher is UNUSED;
on PDF throw/absent OR unresolvable template it FALLS BACK to a dispatcher `offer_sent` email (no attachment),
never throws. offer-sent does NOT copy the recruiter pool; offer-expiry-reminder/offer-expired DO.

## LIVE BUG the catalog test found (NOT fixed — test-files-only lane)
`OfferPlaceholders` (NotificationEventCatalog.cs ~L120-124) declares only `applicant.email`, but all four offer
templates (offer_sent/withdrawn/expiry_reminder/expired) greet `{{applicant.firstName}}`. Violates the
token⊆declared invariant every other event honors (application/scorecard use `ApplicantPlaceholders` which has
firstName/lastName/email). Runtime email renders fine (service passes firstName in payloadData) — impact is the
FR-3 variable-reference panel omitting firstName. **Fix = add `applicant.firstName` (+`.lastName`) to
OfferPlaceholders.** Until then the 4 `Phase5aEvent_TemplateTokens_AreAllDeclaredPlaceholders` offer cases fail
by design — do NOT weaken the guard to silence them.
