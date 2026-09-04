---
name: reference-architecture-tests-project
description: HRM.ArchitectureTests (build-queue E2) — why two of its four rules deliberately do NOT use NetArchTest, and the two compiler facts that forced that
metadata:
  type: reference
---

`src/backend/HRM.ArchitectureTests` holds the Clean-Architecture guards. Two compiler facts drove
its design; both are non-obvious and both were proven by mutation, so don't re-litigate them:

**1. Compiled metadata is blind to a declared-but-unused `PackageReference`.** Adding
`<PackageReference Include="Microsoft.EntityFrameworkCore" />` to `HRM.Domain.csproj` does NOT put
EF in the assembly's reference table until some type actually *uses* it. Mutation A proved this:
the csproj arm went red while the `GetReferencedAssemblies()` arm stayed green. So "Domain has no
framework deps" needs **both** a csproj-XML arm (catches the declaration — the earliest catch) and
a metadata arm (backstop for transitive/FrameworkReference arrivals). One arm alone has a hole.

**2. Optional-parameter inertness is destroyed by compilation.** When C# omits an optional argument
the compiler *materialises the default at the call site*, so `Compute(a,b,c,d)` and
`Compute(a,b,c,d,1.5m)` are identical IL. Neither reflection nor NetArchTest (which reasons about
type→type dependencies only) can express ISSUE-439. It requires **Roslyn syntax analysis of the
source tree** — that is not a workaround, it is the only level where the information still exists.

**Most layer-direction rules are tautologies — don't write them.** `Application ↛ Infrastructure`
and `Domain ↛ Application` are unrepresentable: Infrastructure references Application, so the
reverse edge is a circular project reference MSBuild rejects. Only rules targeting changes that
**compile cleanly** are worth writing (controllers→`AppDbContext` compiles because Api legitimately
references Infrastructure as the composition root; Application→EF compiles because there's no cycle).

**The `KnownInert` baseline is a findings list, not an amnesty list.** It shipped with 3 entries the
rule caught on its first run, and is paired with a stale-entry test so it can only shrink. One entry
is a genuine false-positive class (a structural default like `residualFloor: 0` that is correct
*because* nobody overrides it) — that class is the rule's known cost.

Related: [[reference-payroll-fte-overtime-plumbing]] (GAP-022, the incident this encodes),
[[feedback-guards-must-be-mutation-proven]], [[feedback-mutation-check-revert-before-report]].
