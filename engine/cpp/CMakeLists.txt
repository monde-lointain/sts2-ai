add_subdirectory(src)

# S4-T5: sts2_oracle_verify_server (Q2-ADR-003) — verify-server executable.
# Unconditional: deployable tool, not a test artifact. Links the same
# adapter + AI stack as the existing tests but does NOT pull Arrow/Parquet,
# so it lives outside both STS2_BUILD_TESTS and STS2_BUILD_ORACLE_SINK
# gates. Forward-laid for Q12 boot.
add_subdirectory(tools/oracle-verify-server)

if(STS2_BUILD_TESTS)
    add_subdirectory(tests)
    add_subdirectory(tools/seed-pinner)
endif()

# S3-T6 (Stream C): emit-sample-rows driver. Independent of STS2_BUILD_TESTS
# (the tool produces real Parquet under data/oracle/agreement/ regardless of
# whether tests are built). Gated on STS2_BUILD_ORACLE_SINK because it links
# sts2::oracle_agreement → Arrow + Parquet.
if(STS2_BUILD_ORACLE_SINK)
    add_subdirectory(tools/emit-sample-rows)
endif()
