---
name: creating-an-adr
description: Use when creating a new Architecture Decision Record for the sts2-ai project. Codifies number sequencing from decisions-log, template structure, and the Consequences-leads-with-negatives convention.
---

# Creating an ADR

## Step 1 — Get next number

Read `docs/specs/01-decisions-log.md`. Find the highest existing ADR number in the table. Next = highest + 1. As of 2026-05-16, ADR-022 is the highest.

Do not guess — read the file. The table header is:
```
| # | Title | Status |
```

## Step 2 — Append to the table

Add a new row at the bottom of the table (before the `---` separator):

```markdown
| ADR-NNN | <Title> | PROPOSED |
```

Do not reorder existing rows.

## Step 3 — Write the ADR body

Append after the last existing ADR body. Follow this template exactly:

```markdown
## ADR-NNN — <Title>

**Status:** PROPOSED.

**Context.** <Why this decision is needed. What problem it solves. What forces are in tension. Reference relevant scaling-strategy sections, quanta, or existing ADRs by number.>

**Decision.** <What we decided. Specific, concrete. If phased, name the phases.>

**Consequences.**

- *Negative:* <downside or trade-off>
- *Negative:* <downside or trade-off>
- *Positive:* <benefit>
- *Positive:* <benefit>

**Origin.** <Source: dialogue date, session, or "N/A".>
```

## Consequences convention

**Negatives must come first.** Every ADR in this project lists negatives before positives (see ADR-001 through ADR-022). Reversing this order is a consistency violation. If you find no negatives, reconsider whether the decision is real or a non-decision.

## Status enum

| Status | Meaning |
|--------|---------|
| `PROPOSED` | Under review; not yet binding |
| `ACCEPTED` | Ratified; code/docs must comply |
| `DEFERRED` | Not decided; include target decision date or trigger |
| `SUPERSEDED-BY-ADR-NNN` | Replaced; body references successor |

Include date on Accepted: `**Status:** Accepted (YYYY-MM-DD).`

## Badge convention for module specs (per ADR-023)

When an ADR introduces, defers, or marks responsibilities aspirational in a
module spec (`docs/specs/modules/<quantum-slug>.md`), the affected sections in
that spec MUST be re-badged per ADR-023:

- `[SHIPPED]` — code exists + passes a gate
- `[PHASE-N]` — deferred to a named phase (`[PHASE-1.5]`, `[PHASE-2]`, …)
- `[ASPIRATION]` — design intent only; no roadmap committed

Rule: every Responsibilities / Interfaces / Coupling section carries ≥1 badge
per claim. Mixed badges within a section are allowed; per-bullet badges are
encouraged when a section straddles shipped + aspirational.

**No fourth `[CONTRADICTS-CODE]` badge.** If your ADR exposes a spec-vs-code
contradiction, resolve it in the same PR: update spec to match code (default,
spec is usually stale) OR add inline `[NOTE: contradicts code at <path>;
tracking <issue/ADR>]` and surface to project-lead. Persistent
`[CONTRADICTS-CODE]` is not a state — contradictions resolve at merge boundary.

**Frontmatter requirement.** Every `docs/specs/modules/*.md` has YAML
frontmatter declaring `quantum` and `substrate`. ADRs editing a spec must
preserve frontmatter intact (it is the machine-readable input to the
`spec-edit-tracker` gate per ADR-024).

## Step 4 — Cross-link affected code/docs

Any file that implements or references the decision should include a comment citing the ADR:

```python
# ADR-NNN: <one-line rationale>
```

Do this in the **same PR** as the ADR itself. ADRs without implementation cross-links are harder to audit.

If the ADR involves a proto schema change, also invoke [[bumping-a-schema-version]].

## Step 5 — Slash command shortcut

`/adr-new <title>` appends the table row + skeleton body stub. Still requires human (or agent) completion of Context, Decision, and Consequences sections — the stub only scaffolds structure.

## Common mistakes

- **Skipping the table row** — decisions-log.md has both a table and full-body sections; both must be updated.
- **Positives before negatives** — violates project convention.
- **Vague Decision** — "we will consider X" is not a decision. State what was chosen.
- **Missing Origin** — "Origin." is required even if it's just a date.
- **Creating a new file** — ADRs live in `docs/specs/01-decisions-log.md`, not separate files.

## Cross-references

- [[bumping-a-schema-version]] — schema changes require an ADR
- `docs/specs/01-decisions-log.md` — canonical source
