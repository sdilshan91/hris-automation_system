---
name: onb-001-fe-not-on-branch
description: US-ONB-002 branch lacked the US-ONB-001 frontend code despite STATUS marking it done; had to restore it from the impl commit
metadata:
  type: project
---

When building US-ONB-002 (assign onboarding checklist), the `feature/US-ONB-002`
branch's STATUS.md said US-ONB-001 was "done (PR #95)" but the **onboarding
frontend feature did not exist on the branch** — `git merge-base --is-ancestor
<onb-001 impl commit> HEAD` was false. The status-marking commit was on the
branch, but the actual implementation commit (`feat(US-ONB-001)…`) had never been
merged into this branch's base.

Resolution: restored the self-contained US-ONB-001 FE files
(`features/onboarding/**`) from the implementation commit via
`git show <commit>:<path> > <path>`, then re-applied its additions to the two
shared files (`app.routes.ts` onboarding lazy route, `main-layout.component.ts`
Onboarding nav item — permission `Onboarding.Manage`, route `/onboarding`).

**Why:** the /implement-all loop opens stacked PRs without merging between stories
(see user-memory "Merge each PR before next story" + FE↔BE contract debt note), so
a later story's branch can miss an earlier story's unmerged code even when STATUS
claims it's done.

**How to apply:** for any onboarding follow-up story, FIRST verify
`src/frontend/src/app/features/onboarding/` actually exists on the branch before
building on it; if absent, find the impl commit (`git log --all --oneline | grep
US-ONB-00X`) and restore the feature files + re-apply shared-file edits, rather
than reinventing the patterns. Onboarding service convention:
`environment.apiBaseUrl + '/onboarding/...'`, `withCredentials: true`, bare-`T`
responses (apiEnvelopeInterceptor unwraps), static `parseErrorMessage`.
