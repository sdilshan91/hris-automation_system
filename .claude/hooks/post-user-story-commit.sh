#!/bin/bash
# Hook: Triggered after user story commits
# Detects user-stories/ changes and triggers dev + QA agents in parallel

# Read PostToolUse payload from stdin and only proceed for git commit invocations
HOOK_INPUT=$(cat 2>/dev/null || true)
if [ -n "$HOOK_INPUT" ] && ! echo "$HOOK_INPUT" | grep -q '"command"[^"]*"[^"]*git[[:space:]]\+commit'; then
    exit 0
fi

CHANGED_FILES=$(git diff --name-only HEAD~1 HEAD 2>/dev/null)

# Check if user stories were committed
if echo "$CHANGED_FILES" | grep -q "^user-stories/"; then
    echo "========================================="
    echo " USER STORIES COMMITTED - TRIGGERING AGENTS"
    echo "========================================="

    # Extract which modules had story changes
    MODULES=$(echo "$CHANGED_FILES" | grep "^user-stories/" | sed 's|user-stories/||' | cut -d'/' -f1 | sort -u)

    echo "Modified modules: $MODULES"
    echo ""

    for MODULE in $MODULES; do
        echo ">>> Triggering agents for module: $MODULE"

        # Get list of new/modified story files
        STORY_FILES=$(echo "$CHANGED_FILES" | grep "^user-stories/$MODULE/")

        echo "  Stories changed:"
        echo "$STORY_FILES" | sed 's/^/    - /'
        echo ""
        echo "  [FRONTEND-DEV] Ready to implement stories in: src/frontend/src/app/features/$MODULE/"
        echo "  [BACKEND-DEV]  Ready to implement stories in: src/backend/HRM.Application/Features/$MODULE/"
        echo "  [QA-ENGINEER]  Ready to write test cases in: test-cases/$MODULE/"
        echo ""
    done

    echo "========================================="
    echo " Run: claude /orchestrate to start all agents"
    echo "========================================="
fi
