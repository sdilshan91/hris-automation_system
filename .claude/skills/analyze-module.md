---
name: analyze-module
description: Run business analysis on a specific module and generate user stories
user_invocable: true
---

# Analyze Module

Analyze a specific module from the technical documentation and generate IEEE 830 compliant user stories.

## Usage
```
claude /analyze-module {module-name}
```

## Process
1. Read `docs/hrm_technical_document_v4.0.md` and extract requirements for the specified module
2. Identify all user personas that interact with this module
3. Generate user stories following the IEEE 830 template defined in the business-analyst agent
4. Write stories to `user-stories/{module-name}/` directory
5. Update `user-stories/INDEX.md`
6. Commit the stories

## Available Modules
- authentication, core-hr, leave, attendance, recruitment, payroll,
  performance, admin-console, onboarding, offboarding, training,
  benefits, reports, notifications, audit, settings
