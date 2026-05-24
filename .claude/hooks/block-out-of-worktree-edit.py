#!/usr/bin/env python3
"""PreToolUse hook: deny Edit/Write/NotebookEdit if file_path is outside the project root.

When this repo is opened via a git worktree, $CLAUDE_PROJECT_DIR points at the worktree.
Edits whose resolved file_path escapes that directory (e.g. into the main checkout on
develop) are denied — the model gets a clear reason and can retry with the right path.
"""

import json
import os
import sys


def main() -> None:
    try:
        data = json.load(sys.stdin)
    except json.JSONDecodeError:
        sys.exit(0)

    file_path = data.get("tool_input", {}).get("file_path", "")
    if not file_path:
        sys.exit(0)

    project_dir = os.environ.get("CLAUDE_PROJECT_DIR", "")
    if not project_dir:
        sys.exit(0)

    abs_fp = os.path.realpath(file_path)
    abs_proj = os.path.realpath(project_dir)

    if abs_fp == abs_proj or abs_fp.startswith(abs_proj + os.sep):
        sys.exit(0)

    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": (
                f"Refusing to edit {abs_fp}: it is outside the current worktree "
                f"({abs_proj}). Edit files under $CLAUDE_PROJECT_DIR instead — "
                "you are likely on the wrong checkout."
            ),
        }
    }))


if __name__ == "__main__":
    main()
