---
name: implement-story
description: Implement a specific user story with frontend + backend + QA agents in parallel
user_invocable: true
---

# Implement User Story

Run frontend-dev, backend-dev, and qa-engineer agents in parallel to implement a user story.

## Usage
```
claude /implement-story US-{MODULE}-{NUMBER}
```

## Process
1. Read the specified user story from `user-stories/`
2. Launch three agents in parallel:
   - `@frontend-dev` - Implement the UI components
   - `@backend-dev` - Implement the API and business logic
   - `@qa-engineer` - Write test cases
3. After all agents complete, verify API contract alignment between frontend and backend
4. Generate implementation summary
