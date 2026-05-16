---
allowed-tools: Bash(grep:*), Bash(awk:*), Read, Edit
description: Append the next-numbered ADR stub to docs/specs/01-decisions-log.md. ADR number auto-derived from current max.
disable-model-invocation: false
---

Append a new ADR stub for `$1` (the title) to `docs/specs/01-decisions-log.md`.

Steps:

1. **Read** `docs/specs/01-decisions-log.md` to understand current structure and find the highest existing ADR number:
   ```
   grep -oP 'ADR-\K[0-9]+' docs/specs/01-decisions-log.md | sort -n | tail -1
   ```
   Next ADR number = max + 1, zero-padded to 3 digits (e.g., 023).

2. **Compose the ADR stub** using the title `$1`:

   ```markdown
   ## ADR-<NNN>: $1

   - **Status:** PROPOSED
   - **Date:** <today YYYY-MM-DD>
   - **Context:** <describe the situation or problem driving this decision>
   - **Decision:** <state the decision made>
   - **Consequences:**
     - Negative: <list downsides first — this is the convention>
     - Positive: <list upsides>
   ```

   Convention: **Consequences leads with negatives** — always list drawbacks before benefits to force honest trade-off analysis.

3. **Update the table of contents** if one exists (look for a ToC section at the top listing ADR-NNN entries). Append the new entry in the same format as existing ones.

4. **Append the stub** to `docs/specs/01-decisions-log.md` using Edit (append to end of file, after the last ADR entry). Preserve existing whitespace conventions (blank lines between ADR blocks).

5. Report: ADR number assigned, file path, title.

Reference skill: `[[creating-an-adr]]`
