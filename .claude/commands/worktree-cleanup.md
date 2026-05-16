---
allowed-tools: Bash(git worktree:*), Bash(git merge-base:*), Bash(git branch:*)
description: List orphan worktrees (branches merged into main but worktrees still present), optionally prune after confirmation.
disable-model-invocation: false
---

Audit worktrees for stale/orphaned entries and optionally prune them.

Steps:

1. **List all worktrees**:
   ```
   git worktree list --porcelain
   ```

2. **For each non-main worktree**, check whether its branch has been merged into main:
   ```
   git merge-base --is-ancestor <branch> main
   ```
   Exit 0 = ancestor (merged), exit 1 = not merged.

3. **Categorize** each worktree as:
   - **Merged/orphan**: branch is ancestor of main — safe to remove
   - **Active**: branch not yet merged — do NOT touch
   - **Detached/unknown**: no branch name — flag for manual review

4. **Present findings** in a clear table:
   ```
   PATH                                  BRANCH             STATUS
   /path/to/worktree-agent-abc123        worktree-agent-abc  merged (orphan)
   /path/to/worktree-agent-def456        worktree-agent-def  active
   ```

5. **Ask for confirmation** before pruning. Do NOT prune automatically. The user must explicitly say "yes, prune" or similar.

6. **If confirmed**, for each merged/orphan worktree:
   ```
   git worktree unlock <path>  # if locked
   git worktree remove <path>
   git branch -D <branch>
   ```
   Report each removed worktree.

7. Report: total worktrees, orphan count, removed count (if any).
