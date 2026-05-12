# ------------------------------------------------------------------
# sts2::oracle_adapter — Q1 -> Q2 engine->CompactState adapter
# ------------------------------------------------------------------
# Per Q2-ADR-001: sibling to sts2::ai, depends on sts2::simulator
# (consumes CompactState / EnemyState / CardCounts). Self-contained;
# the existing 424-test battery is untouched.
add_library(sts2_oracle_adapter STATIC)
add_library(sts2::oracle_adapter ALIAS sts2_oracle_adapter)

target_sources(sts2_oracle_adapter PRIVATE
    # S1-T0 manifest is header-only; placeholder source so the static-lib
    # target has at least one TU. Real sources land in S1-T1+ (state_blob.cc,
    # envelope.cc, cultists_projection.cc, adapter.cc).
    manifest.cc
)

target_compile_features(sts2_oracle_adapter PUBLIC cxx_std_20)

target_include_directories(sts2_oracle_adapter
    PUBLIC  $<BUILD_INTERFACE:${PROJECT_SOURCE_DIR}/engine/cpp/include>
    PRIVATE ${PROJECT_SOURCE_DIR}/engine/cpp/src
)

target_link_libraries(sts2_oracle_adapter PUBLIC sts2::simulator)

sts2_apply_warnings(sts2_oracle_adapter)
