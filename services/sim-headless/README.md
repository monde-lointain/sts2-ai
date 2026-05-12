# sts2-headless

Headless C# port of Slay the Spire 2 for the Q1 Game Simulator (RL training pipeline). See `docs/specs/` for architecture, `docs/plans/` for implementation plan.

## Building

```
make build   # dotnet build
make test    # dotnet test (depends on build)
make ci      # build + test + tripwire; the canonical health check
```

## Analyzer tripwire

`make tripwire` builds a small standalone project that intentionally references a banned API (`DateTime.Now`) and asserts the build fails with RS0030 — i.e. proves the determinism analyzer is actually firing, not silently disabled.
