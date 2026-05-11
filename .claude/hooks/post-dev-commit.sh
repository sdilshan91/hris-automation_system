#!/bin/bash
# Hook: Triggered after dev commits (frontend or backend)
# Notifies QA agent that implementation is ready for test execution

CHANGED_FILES=$(git diff --name-only HEAD~1 HEAD 2>/dev/null)

# Check if frontend or backend code was committed
FE_CHANGES=$(echo "$CHANGED_FILES" | grep "^src/frontend/" | head -20)
BE_CHANGES=$(echo "$CHANGED_FILES" | grep "^src/backend/" | head -20)

if [ -n "$FE_CHANGES" ] || [ -n "$BE_CHANGES" ]; then
    echo "========================================="
    echo " DEV CODE COMMITTED - QA NOTIFICATION"
    echo "========================================="

    if [ -n "$FE_CHANGES" ]; then
        echo ""
        echo "[FRONTEND] Changed files:"
        echo "$FE_CHANGES" | sed 's/^/  - /'
    fi

    if [ -n "$BE_CHANGES" ]; then
        echo ""
        echo "[BACKEND] Changed files:"
        echo "$BE_CHANGES" | sed 's/^/  - /'
    fi

    # Extract commit message for context
    COMMIT_MSG=$(git log -1 --pretty=%B)
    echo ""
    echo "Commit: $COMMIT_MSG"

    # Check if related test cases exist
    echo ""
    echo "[QA-ENGINEER] Review and update test cases for changed modules"
    echo "========================================="
fi
