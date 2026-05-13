# sts2_emit_sample_rows — S3-T6 driver, emits a 2-row Parquet sample
# (adapter-diagnostic + S2 pin verified row) to data/oracle/agreement/.
#
# Gated on STS2_BUILD_ORACLE_SINK (added at the engine/cpp/CMakeLists.txt
# level so the tool is independent of STS2_BUILD_TESTS). Links the full
# Q2 lib stack: oracle_agreement (sink) + oracle_adapter (D3 reject path)
# + oracle_registry (registry SHA) + simulator (transitive base).
#
# STS2_CONTRACTS_ROOT mirrors the registry lib's setup so
# current_phase1_registry_sha256() resolves phase1-silent.json at runtime.

add_executable(sts2_emit_sample_rows main.cc)

target_compile_features(sts2_emit_sample_rows PRIVATE cxx_std_20)

target_link_libraries(sts2_emit_sample_rows PRIVATE
    sts2::oracle_agreement
    sts2::oracle_adapter
    sts2::oracle_registry
    sts2::simulator
)

target_compile_definitions(sts2_emit_sample_rows PRIVATE
    STS2_CONTRACTS_ROOT="${PROJECT_SOURCE_DIR}/contracts"
)

target_include_directories(sts2_emit_sample_rows PRIVATE
    ${CMAKE_SOURCE_DIR}/engine/cpp
    ${PROJECT_SOURCE_DIR}/engine/cpp/src
)

sts2_apply_warnings(sts2_emit_sample_rows)

# Smoke test: invoke the driver against a per-build temp dir so the tool
# self-verifies on every ctest run (fixture parse → AdapterReject branch →
# write → read-back round-trip). Stages a clean out-root via cmake -E rm
# before each run so re-invocation never collides with the prior file
# (single-writer-per-day-per-model rule in the sink).
if(STS2_BUILD_TESTS)
    set(_emit_sample_rows_test_outroot
        "${CMAKE_BINARY_DIR}/emit_sample_rows_smoke")
    add_test(NAME EmitSampleRows.ResetOutRoot
             COMMAND ${CMAKE_COMMAND} -E rm -rf
                     "${_emit_sample_rows_test_outroot}")
    add_test(NAME EmitSampleRows.RunsAndEmits2Rows
             COMMAND sts2_emit_sample_rows
                     --out-root "${_emit_sample_rows_test_outroot}")
    # FIXTURES_SETUP/REQUIRED ensures the reset step runs whenever the
    # RunsAndEmits2Rows test is selected, even under a -R filter that
    # picks only the second test.
    set_tests_properties(EmitSampleRows.ResetOutRoot PROPERTIES
        FIXTURES_SETUP EmitSampleRowsOutRoot)
    set_tests_properties(EmitSampleRows.RunsAndEmits2Rows PROPERTIES
        FIXTURES_REQUIRED EmitSampleRowsOutRoot
        PASS_REGULAR_EXPRESSION
            "row\\[1\\] \\(verified\\)")
endif()
