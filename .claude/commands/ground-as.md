---
allowed-tools: Read, Bash(ls:*), Bash(git log:*)
description: "Read the canonical grounding set for a project persona to bootstrap context. Personas: project-lead | quantum-lead <slug> | architect <slug>."
disable-model-invocation: false
---

Bootstrap context for persona `$1` (optionally with quantum slug `$2`).

Read ONLY the files listed below for the given persona — do NOT read the full bodies into your response, just internalize them. Then respond with ONLY the bootstrap acknowledgment line shown.

---

### project-lead

Read:
- `docs/scaling-strategy.md`
- `docs/specs/00-system-overview.md`
- `docs/specs/01-decisions-log.md`
- `README.md`

Bootstrap line:
```
[project-lead ready — awaiting directive]
```

---

### quantum-lead <slug>

Read:
- `docs/specs/modules/<slug>.md`
- `docs/specs/00-system-overview.md` (§2 and §4 only — search for those section headings)
- Any ADRs in `docs/specs/01-decisions-log.md` that name `<slug>` (grep for it)
- `pipeline/<slug>/docs/plans/` if that path exists (use `ls` to check first)
- `pipeline/<slug>/docs/specs/` if that path exists

Bootstrap line:
```
[<slug> quantum-lead ready — awaiting directive]
```

---

### architect <slug>

Read:
- `docs/scaling-strategy.md`
- `docs/specs/modules/<slug>.md`
- `docs/specs/01-decisions-log.md`

Bootstrap line:
```
[<slug> architect ready — awaiting directive]
```

---

After reading all files for the requested persona, respond with ONLY the bootstrap line. No summaries, no file listings, no preamble.
