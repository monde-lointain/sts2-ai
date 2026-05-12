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
    adapter_fixtures.h
)

target_compile_features(sts2_oracle_tests PRIVATE cxx_std_20)

target_link_libraries(sts2_oracle_tests PRIVATE
    sts2::oracle_adapter
    sts2::simulator
    sts2::ai
    GTest::gtest_main
    GTest::gmock
)

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
