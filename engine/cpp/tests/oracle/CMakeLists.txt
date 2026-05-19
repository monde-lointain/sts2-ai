# ------------------------------------------------------------------
# sts2_oracle_tests — Q2 oracle adapter gtest executable
# ------------------------------------------------------------------
# Per Q2-ADR-001: a separate test target keeps the existing
# sts2_simulator_tests battery (424 tests) untouched. Adapter tests
# link sts2::oracle_adapter + sts2::simulator (the latter for
# sts2::ai::Search in the round-trip test).
add_executable(sts2_oracle_tests)

target_sources(sts2_oracle_tests PRIVATE
    test_manifest.cc
    test_state_blob.cc
    test_envelope.cc
    test_cultists_projection.cc
    test_louse_progenitor_projection.cc
    test_louse_progenitor_search_pins.cc
    test_nibbits_weak_search_pins.cc
    test_nibbits_weak_projection.cc
    test_nibbits_normal_projection.cc
    ../seeds/dump_cultist_zobrist_key.cc
    test_adapter_facade.cc
    test_adapter_roundtrip.cc
    test_adapter_reject.cc
    test_registry_sha.cc
    test_pin_row.cc
    test_cultists_search_pins.cc
    test_small_slimes_search_pins.cc
    test_agreement_row.cc
    test_verify_server.cc
    adapter_fixtures.h
    envelope_test_helpers.h
)

target_compile_features(sts2_oracle_tests PRIVATE cxx_std_20)

target_link_libraries(sts2_oracle_tests PRIVATE
    sts2::oracle_adapter
    sts2::oracle_registry
    sts2::oracle_verify_server
    sts2::simulator
    sts2::ai
    GTest::gtest_main
    GTest::gmock
)

# S2-T1: STS2_CONTRACTS_ROOT lets test cases call current_phase1_registry_sha256()
# and cross-check via direct compute_registry_sha256(path).
target_compile_definitions(sts2_oracle_tests PRIVATE
    STS2_CONTRACTS_ROOT="${PROJECT_SOURCE_DIR}/contracts"
)

# S3-T5 (Stream C): Parquet sink round-trip tests. Gated under
# STS2_BUILD_ORACLE_SINK because sts2::oracle_agreement pulls Arrow + Parquet.
# The compile_definition lets test_sink.cc #if-guard its TEST(...) blocks
# defensively (belt + suspenders).
if(STS2_BUILD_ORACLE_SINK)
    target_sources(sts2_oracle_tests PRIVATE
        test_sink.cc
        test_agreement_schema_freeze.cc
    )
    target_link_libraries(sts2_oracle_tests PRIVATE
        sts2::oracle_agreement
        Arrow::arrow_shared
        Parquet::parquet_shared
    )
    target_compile_definitions(sts2_oracle_tests PRIVATE STS2_BUILD_ORACLE_SINK)
endif()

# Mirror the simulator_tests include shape so headers under
# engine/cpp/include/ resolve normally.
target_include_directories(sts2_oracle_tests PRIVATE
    ${CMAKE_SOURCE_DIR}/engine/cpp
    ${PROJECT_SOURCE_DIR}/engine/cpp/src
)

# Match the existing test target's warnings posture (relax -Werror under
# tests; GoogleTest headers can trip /W4 + /WX on MSVC).
sts2_apply_warnings(sts2_oracle_tests)
if(MSVC)
    target_compile_options(sts2_oracle_tests PRIVATE /WX-)
else()
    target_compile_options(sts2_oracle_tests PRIVATE -Wno-error)
endif()

gtest_discover_tests(sts2_oracle_tests)
