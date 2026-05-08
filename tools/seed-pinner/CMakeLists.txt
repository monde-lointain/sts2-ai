# sts2_seed_pinner — regenerator for tests/seeds/expected_values.h.
#
# Gated on STS2_BUILD_TESTS so production builds aren't impacted. This
# executable is NOT run automatically: it must be invoked by hand to
# rebuild the pinned-values header (see tools/seed-pinner/README.md).

add_executable(sts2_seed_pinner pin_seeds.cpp)

target_compile_features(sts2_seed_pinner PRIVATE cxx_std_20)

target_link_libraries(sts2_seed_pinner PRIVATE sts2::simulator)

sts2_apply_warnings(sts2_seed_pinner)
