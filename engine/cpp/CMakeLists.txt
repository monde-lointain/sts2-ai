add_subdirectory(src)

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
