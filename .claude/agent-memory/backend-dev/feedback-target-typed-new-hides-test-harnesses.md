---
name: target-typed-new-hides-test-harnesses
description: Grepping "new <ServiceName>(" misses test harnesses that construct the service with target-typed `new(...)` — search the type name alone before concluding no tests exist
metadata:
  type: feedback
---

Never conclude "this service has no unit tests" from `grep "new SomeService("`. Many harnesses in
`HRM.Tests/Unit` build the SUT through a typed factory method, e.g.

    private AssetService Service(ICurrentUser? user = null) =>
        new(Db(), _tenantContext, user ?? _hrUser, _fileStorage, _scanner, ...);

which contains the type name only in the return type. Grep for the bare type name
(`grep -rln "AssetService" src/backend/HRM.Tests/`) or for `Glob **/<Type>Tests.cs`.

**Why:** on BUG-075 this cost a whole duplicate test class. `grep -rl "new AssetService"` returned only an
integration test, so I wrote a fresh `AssetServiceUploadTests.cs` with its own tenant/user/storage/scanner
harness — while a 423-line `AssetServiceTests.cs` with exactly that harness (and an existing acknowledgment
happy-path test) was sitting next to it. Had to delete the new file and fold the arms in, which also meant
re-running the mutation proof, because the first proof exercised tests that no longer shipped.

**How to apply:** before creating any new `*Tests.cs`, run the bare-type-name grep AND a
`ls src/backend/HRM.Tests/Unit | grep -i <domain>`. If a harness exists, extend it — the reuse-over-
duplication rule covers test fixtures, not just production helpers. And re-run any mutation proof whose
tests you subsequently moved: a proof is only valid against the code that ships.
See [[mutation-check-revert-before-report]] and [[guards-must-be-mutation-proven]].
