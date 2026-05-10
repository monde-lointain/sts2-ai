if(ENABLE_COVERAGE)
  if(CMAKE_CXX_COMPILER_ID MATCHES "GNU|Clang")
    add_compile_options(--coverage)
    add_link_options(--coverage)

    # lcov target
    find_program(LCOV_EXECUTABLE NAMES lcov)
    find_program(GENHTML_EXECUTABLE NAMES genhtml)

    if(LCOV_EXECUTABLE AND GENHTML_EXECUTABLE)
      add_custom_target(coverage
        COMMAND ${LCOV_EXECUTABLE} --directory . --zerocounters --rc branch_coverage=1
        COMMAND ${CMAKE_CTEST_COMMAND} --output-on-failure
        COMMAND ${LCOV_EXECUTABLE} --directory . --capture --output-file coverage.info --rc branch_coverage=1 --ignore-errors mismatch,inconsistent
        COMMAND ${LCOV_EXECUTABLE} --remove coverage.info '/usr/*' '*/tests/*' '*/build/*' '*/googletest/*' --output-file coverage.info.cleaned --rc branch_coverage=1 --ignore-errors unused
        COMMAND ${GENHTML_EXECUTABLE} coverage.info.cleaned --output-directory coverage-report --branch-coverage
        COMMAND ${LCOV_EXECUTABLE} --summary coverage.info.cleaned --rc branch_coverage=1 --ignore-errors unused || true
        COMMAND ${CMAKE_COMMAND} -E echo "Coverage report generated in ${CMAKE_BINARY_DIR}/coverage-report/index.html"
        WORKING_DIRECTORY ${CMAKE_BINARY_DIR}
        COMMENT "Generating code coverage report"
      )
    endif()
  else()
    message(WARNING "Code coverage is only supported on GCC and Clang")
  endif()
endif()
