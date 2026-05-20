# Handoffs

Short-lived context drops between agents during a pipeline run. This is how `@business-analyst` tells `@frontend-dev` and `@backend-dev` "watch out for X" without it needing to live in the user story itself.

## When to write a handoff

- BA → dev: ambiguities in the spec the agent resolved one way (so dev doesn't re-resolve differently)
- Dev → QA: edge cases the dev specifically engineered for (so QA tests them)
- QA → dev: failing scenarios that need a code fix

## When NOT to write a handoff

- If the info belongs in the user story → put it in the story
- If the info belongs in a module note → put it in the module note
- If it's a permanent decision → use [[../decisions/README|decisions/]]

Handoffs are **transient**. Delete or archive them after the relevant PRs merge.

## Format

`YYYY-MM-DD-<from-agent>-to-<to-agent>-<topic>.md`. Use [[_template]].

## Active handoffs
*(list any open handoffs here)*
