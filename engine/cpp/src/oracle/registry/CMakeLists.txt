# ------------------------------------------------------------------
# sts2::oracle_registry — Q2 per-encounter pin registry support lib
# ------------------------------------------------------------------
# Per Q2-ADR-005: registry-SHA stamping is part of the manifest
# discipline. This target hosts the registry-SHA computation utility
# + the PinnedScenarioRow struct consumed by S2 streams B and D.
# Sibling to sts2::oracle_adapter; self-contained; adapter sources
# untouched (S2 stream-A constraint).
add_library(sts2_oracle_registry STATIC)
add_library(sts2::oracle_registry ALIAS sts2_oracle_registry)

target_sources(sts2_oracle_registry PRIVATE
    sha.cc
)

target_compile_features(sts2_oracle_registry PUBLIC cxx_std_20)

target_include_directories(sts2_oracle_registry
    PUBLIC  $<BUILD_INTERFACE:${PROJECT_SOURCE_DIR}/engine/cpp/include>
    PRIVATE ${PROJECT_SOURCE_DIR}/engine/cpp/src
)

# Locate contracts/ deterministically at runtime. Used by
# current_phase1_registry_sha256() to find phase1-silent.json.
target_compile_definitions(sts2_oracle_registry PRIVATE
    STS2_CONTRACTS_ROOT="${PROJECT_SOURCE_DIR}/contracts"
)

# sts2::simulator brings in transition::ActionKind + CardId used by
# the PinnedScenarioRow struct in pin_row.h (header-only, ships in
# S2-T2 — link dependency declared up front so consumers compile cleanly).
target_link_libraries(sts2_oracle_registry PUBLIC sts2::simulator)

sts2_apply_warnings(sts2_oracle_registry)
