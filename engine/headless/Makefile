SHELL := /bin/bash
.SHELLFLAGS := -o pipefail -c
export PATH := /usr/bin:$(PATH)

.PHONY: build test tripwire ci clean probe probe-quick probe-full probe-build probe-capture probe-generate-corpus probe-upstream-capture probe-upstream-initial-state gc-measurement

build:
	@echo "==> dotnet build"
	dotnet build sts2-headless.sln -c Debug

test: build
	@echo "==> dotnet test"
	dotnet test sts2-headless.sln -c Debug --no-build

tripwire:
	@echo "==> Verifying analyzer tripwire fires"
	@if dotnet build test/Sts2Headless.AnalyzerTripwire/Sts2Headless.AnalyzerTripwire.csproj -c Debug 2>&1 | tee /tmp/sts2-tripwire.log; then \
	  echo "ERROR: tripwire built successfully; analyzer is NOT firing"; \
	  exit 1; \
	else \
	  if grep -q "RS0030" /tmp/sts2-tripwire.log; then \
	    echo "OK: tripwire correctly failed with RS0030"; \
	  else \
	    echo "ERROR: tripwire failed but not with RS0030 — unexpected failure mode"; \
	    cat /tmp/sts2-tripwire.log; \
	    exit 1; \
	  fi; \
	fi

# R7 GC measurement harness. Re-runs the 10K-RT hook-protocol workload with
# the GcMetricsSampler wired in and emits a JSON line to stdout containing
# cumulative gc_time_seconds, per-gen collection counts, and the
# gc/wall ratio. CI captures stdout so the number is preserved in the
# orchestrator's status round-up.
gc-measurement: build
	@echo "==> R7 GC measurement (10K-RT hook-protocol workload)"
	dotnet test test/Sts2Headless.Tests.Host/Sts2Headless.Tests.Host.csproj \
	  -c Debug --no-build --filter "FullyQualifiedName~Gc.GcMeasurementHarness" \
	  --logger "console;verbosity=detailed" | grep -E "r7_gc_(measurement|variance)|R7-REOPEN" || true

ci: build test tripwire gc-measurement
	@echo "==> CI green"

# === S13 determinism probe ============================================
#
# The probe is an OUT-OF-SOLUTION tool at test/determinism-probe/. It links
# the Host project but lives outside sts2-headless.sln to keep Domain builds
# decoupled from probe runs.

probe-build:
	@echo "==> dotnet build (probe)"
	dotnet build test/determinism-probe/Sts2Headless.DeterminismProbe.csproj -c Debug

# `make probe` is an alias for the quick path (pre-commit / dev-loop).
probe: probe-quick

# Quick: 22 structural + 5 smoke-perstep. Budget < 60s; suitable for
# pre-commit / CI fast-lane.
probe-quick: probe-build
	@echo "==> probe-quick (structural-all-22 + smoke-perstep-5)"
	dotnet run --project test/determinism-probe -c Debug --no-build -- --mode quick

# Full: 292 entries (22 structural + 220 initial-state + 50 perstep).
# Use as scheduled task / pre-merge gate.
probe-full: probe-build
	@echo "==> probe-full (full Phase-1 corpus)"
	dotnet run --project test/determinism-probe -c Debug --no-build -- --mode full

# Regenerate goldens. Run only when an intentional Q1 change shifts the
# state hashes (e.g., S6/S7 schema bump, behavior fill-in for a previously
# stubbed model). Reviewer must inspect the diff before committing.
probe-capture: probe-build
	@echo "==> probe-capture (overwrites goldens with current Q1 trace)"
	dotnet run --project test/determinism-probe -c Debug --no-build -- --mode capture

# Regenerate the corpus JSON from CorpusGenerator. Use after adding
# encounters / seeds to the generator.
probe-generate-corpus: probe-build
	@echo "==> probe-generate-corpus"
	dotnet run --project test/determinism-probe -c Debug --no-build -- --mode generate-corpus

# === Stream C upstream comparison =====================================
#
# Stream-C-T2/T3 plumbing: capture 220 upstream-derived initial-state goldens
# by linking the Steam-install sts2.dll directly. The upstream-capture project
# is INTENTIONALLY OUT of sts2-headless.sln (mock-worker precedent); these
# targets exist so the capture is reproducible. CI should NOT depend on the
# Steam install being present — these targets are dev-machine only.

probe-upstream-capture:
	@echo "==> probe-upstream-capture (link sts2.dll, batch-capture 220 goldens)"
	dotnet build test/determinism-probe-upstream-capture/Sts2Headless.UpstreamCapture.csproj -c Debug
	rm -rf test/determinism-probe/goldens-upstream/initial-state
	dotnet run --project test/determinism-probe-upstream-capture --no-build -c Debug -- \
	  --batch-out test/determinism-probe/goldens-upstream/initial-state

# Stream-C-T4 gate target: byte-compare Q1's post-SetUpCombat snapshot against
# the upstream-derived goldens for all 22 encounters x 10 seeds.
#
#   PASS  -> M-Headless probe gate flips PARTIAL -> PASS for initial-state
#   DIVR  -> Stream A dispatch should pause; root-cause as P0
#   SKIP  -> Documented Q1-vs-upstream content gap (Q1 invented encounters)
#   ERR   -> I/O or construction error (build / golden missing)
probe-upstream-initial-state: probe-build
	@echo "==> probe-upstream-initial-state (22 encounters x 10 seeds; byte-compare vs upstream)"
	dotnet run --project test/determinism-probe -c Debug --no-build -- --mode initial-state-upstream

clean:
	dotnet clean sts2-headless.sln
	find . -type d \( -name bin -o -name obj \) -exec rm -rf {} +
