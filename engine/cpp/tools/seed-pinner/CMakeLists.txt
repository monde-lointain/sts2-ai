# sts2_seed_pinner — regenerator for tests/seeds/expected_values.h.
#
# Gated on STS2_BUILD_TESTS so production builds aren't impacted. This
# executable is NOT run automatically: it must be invoked by hand to
# rebuild the pinned-values header (see tools/seed-pinner/README.md).
#
# Links sts2::oracle_adapter + sts2::oracle_registry for the optional
# `--manifest` sidecar (Q2-ADR-005). STS2_CONTRACTS_ROOT mirrors the
# registry lib's setup so current_phase1_registry_sha256() resolves
# phase1-silent.json at runtime.

add_executable(sts2_seed_pinner pin_seeds.cc)

target_compile_features(sts2_seed_pinner PRIVATE cxx_std_20)

target_link_libraries(sts2_seed_pinner PRIVATE
    sts2::simulator
    sts2::oracle_adapter
    sts2::oracle_registry
)

target_compile_definitions(sts2_seed_pinner PRIVATE
    STS2_CONTRACTS_ROOT="${PROJECT_SOURCE_DIR}/contracts"
)

sts2_apply_warnings(sts2_seed_pinner)
