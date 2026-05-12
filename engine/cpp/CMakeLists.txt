add_subdirectory(src)

if(STS2_BUILD_TESTS)
    add_subdirectory(tests)
    add_subdirectory(tools/seed-pinner)
endif()
