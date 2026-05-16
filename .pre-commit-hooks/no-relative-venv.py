#!/usr/bin/env python3
"""Pre-commit hook: block introduction of relative .venv/bin paths.

Prevents regression of Wave 0.1 Makefile fix. Allows $(VENV)/bin/... and
absolute /path/to/.venv/bin/... patterns; blocks relative `.venv/bin/...`
which breaks in worktrees and when make is invoked from subdirs.
"""
import re
import sys

# Matches `.venv/bin` NOT preceded by `/`, `$`, `(`, or alnum (which would
# indicate $(VENV)/bin or /abs/path/.venv/bin or repo.venv/bin etc.)
PATTERN = re.compile(r'(?<![/\w$(])\.venv/bin')


def check(path: str) -> bool:
    try:
        with open(path, encoding='utf-8') as f:
            text = f.read()
    except (OSError, UnicodeDecodeError):
        return True
    issues = []
    for i, line in enumerate(text.splitlines(), 1):
        # Skip comments
        stripped = line.lstrip()
        if stripped.startswith('#'):
            continue
        if PATTERN.search(line):
            issues.append((i, line.strip()))
    if issues:
        sys.stderr.write(f"{path}: relative .venv/bin found:\n")
        for ln, content in issues:
            sys.stderr.write(f"  L{ln}: {content}\n")
        sys.stderr.write(
            "Use $(VENV)/bin/... in Makefile (variable defined at top) or an "
            "absolute path elsewhere. Relative paths break in worktrees.\n"
        )
        return False
    return True


if __name__ == '__main__':
    ok = all(check(p) for p in sys.argv[1:])
    sys.exit(0 if ok else 1)
