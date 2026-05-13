# sts2_oracle_golden_emit — one-shot schema-freeze golden parquet emitter.
#
# Gated on STS2_BUILD_ORACLE_SINK because the tool depends on sts2::oracle_agreement
# (which only exists under that gate). Run manually when intentionally bumping
# Q2-ADR-004's AgreementRow schema; output committed at
# engine/cpp/tests/oracle/golden/year=2025/month=05/day=12/model=golden-v0-model-sha.parquet
# is read by tests/oracle/test_agreement_schema_freeze.cc.

add_executable(sts2_oracle_golden_emit main.cc)

target_compile_features(sts2_oracle_golden_emit PRIVATE cxx_std_20)

target_link_libraries(sts2_oracle_golden_emit PRIVATE
    sts2::oracle_agreement
    sts2::simulator
)

target_include_directories(sts2_oracle_golden_emit PRIVATE
    ${PROJECT_SOURCE_DIR}/engine/cpp/src
)

sts2_apply_warnings(sts2_oracle_golden_emit)
