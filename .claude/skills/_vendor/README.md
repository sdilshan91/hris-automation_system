# Vendored third-party skills

`dotnet-skills` (MIT, [Aaronontheweb/dotnet-skills](https://github.com/Aaronontheweb/dotnet-skills)) was
installed as a **plugin** until 2026-08-23. It shipped 36 skills, of which 20 are off-stack for this
repo (Akka.NET, Aspire, Blazor, MJML, R3, ILSpy…). Those 20 were listed in `skillOverrides` as `"off"`
for months — but the Claude Code docs are explicit:

> **"Plugin skills are not affected by `skillOverrides`. Manage those through `/plugin` instead."**

So the mute list never muted anything, and once the plugin was genuinely installed (2026-08-22) all 20
became live — `@backend-dev` was being offered Akka.NET actor-system guidance and Blazor Playwright
patterns on an Angular + ASP.NET Core project.

**Resolution:** the plugin is uninstalled and the **16 on-stack skills are vendored here** at v1.5.0.
Vendored loose skills are NOT plugin skills, so they are precisely controllable — what is in this
directory is exactly what agents are offered, and nothing else.

**Trade-off:** frozen at v1.5.0, no auto-update. To refresh:

```bash
claude plugin marketplace add Aaronontheweb/dotnet-skills
D=~/.claude/plugins/cache/dotnet-skills/dotnet-skills/<version>
# re-copy the 16 directories listed in CLAUDE.md, then remove the marketplace again
```

Skipped deliberately (off-stack): akka-* (5), aspire-* (4), playwright-blazor, playwright-ci-caching,
mjml-email-templates, verify-email-snapshots, r3-reactive-extensions, ilspy-decompile,
dotnet-devcert-trust, local-tools, marketplace-publishing, skills-index-snippets, slopwatch.
Also skipped: the plugin's 6 agents (all Akka/DocFX/benchmark-oriented, none on-stack).

Upstream LICENSE preserved as `dotnet-skills-LICENSE`.
