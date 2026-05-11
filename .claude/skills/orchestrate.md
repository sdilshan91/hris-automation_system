---
name: orchestrate
description: Orchestrate all agents locally - BA writes stories, then dev + QA work in parallel, all push to GitHub via MCP
user_invocable: true
---

# Agent Orchestration Workflow (Local + GitHub MCP)

Execute the full development pipeline for the HRM SaaS platform.
Agents run locally via Claude Code and push to GitHub via MCP server.

## Pipeline Stages

### Stage 1: Business Analysis (Sequential)
1. Launch the `business-analyst` agent to:
   - Read all documents in `docs/` folder
   - Analyze functional requirements
   - Write IEEE 830 compliant user stories to `user-stories/` directory
   - Create the story index file
   - Use GitHub MCP to:
     - Create branch `feature/user-stories-{module}`
     - Push story files
     - Open PR to main
     - Create GitHub Issues for each epic/module

### Stage 2: Development + QA (Parallel)
After user stories are committed, launch THREE agents **in parallel using git worktrees**:

2a. Launch the `frontend-dev` agent (isolation: worktree) to:
   - Read the committed user stories
   - Implement Angular 20 frontend components
   - Write unit tests
   - Use GitHub MCP to create branch `feature/frontend-{module}` + push + open PR

2b. Launch the `backend-dev` agent (isolation: worktree) to:
   - Read the committed user stories
   - Implement ASP.NET Core 10 API
   - Write unit + integration tests
   - Use GitHub MCP to create branch `feature/backend-{module}` + push + open PR

2c. Launch the `qa-engineer` agent (isolation: worktree) to:
   - Read the committed user stories
   - Write IEEE 829 compliant test cases
   - Create test matrices and traceability matrix
   - Use GitHub MCP to create branch `feature/qa-{module}` + push + open PR

### Stage 3: Integration Verification
3. After all Stage 2 agents complete:
   - Verify frontend <> backend API contract alignment
   - Verify test case <> user story traceability
   - Use GitHub MCP to create an integration review Issue with checklist

## Execution

To run the full pipeline for a module:
```
claude /orchestrate
```

To run individual agents:
```
claude @business-analyst "Analyze docs/ and write user stories for {module}, then push to GitHub via MCP"
claude @frontend-dev "Implement stories from user-stories/{module}/, push to GitHub via MCP"
claude @backend-dev "Implement stories from user-stories/{module}/, push to GitHub via MCP"
claude @qa-engineer "Write test cases for user-stories/{module}/, push to GitHub via MCP"
```

## Module Execution Order
Process modules in this priority order:
1. authentication (foundation)
2. core-hr (employees, departments)
3. leave-management
4. attendance
5. recruitment
6. payroll
7. performance
8. admin-console
9. onboarding-offboarding
10. training-benefits
11. reports-analytics
12. notifications-audit

## GitHub MCP Operations Per Agent

| Agent | Branch | PR | Issues |
|-------|--------|----|--------|
| @business-analyst | `feature/user-stories-{module}` | User stories PR | Epic issues per module |
| @frontend-dev | `feature/frontend-{module}` | Frontend impl PR | - |
| @backend-dev | `feature/backend-{module}` | Backend impl PR | - |
| @qa-engineer | `feature/qa-{module}` | Test cases PR | Bug/gap issues if found |
| orchestrator | - | - | Integration review issue |

## No Anthropic API Credits Needed
This pipeline runs entirely on your local Claude Code session.
GitHub MCP handles all remote operations (branches, PRs, issues).
