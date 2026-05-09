# cppcheck target
find_program(CPPCHECK_EXECUTABLE NAMES cppcheck)
if(CPPCHECK_EXECUTABLE)
  # Collect custom rule files from submodule
  file(GLOB CPPCHECK_RULE_FILES
    ${CMAKE_SOURCE_DIR}/external/cppcheck-rules/*/rule.xml
  )

  # Build --rule-file arguments
  set(CPPCHECK_RULE_ARGS "")
  foreach(RULE_FILE ${CPPCHECK_RULE_FILES})
    list(APPEND CPPCHECK_RULE_ARGS --rule-file=${RULE_FILE})
  endforeach()

  add_custom_target(cppcheck
    COMMAND ${CPPCHECK_EXECUTABLE}
      --enable=all
      --suppress=missingIncludeSystem
      --suppress=unusedFunction
      --suppress=unmatchedSuppression
      --inline-suppr
      --std=c++20
      --error-exitcode=1
      --check-level=exhaustive
      -I${CMAKE_SOURCE_DIR}
      -I${CMAKE_SOURCE_DIR}/include
      -I${CMAKE_SOURCE_DIR}/src
      ${CPPCHECK_RULE_ARGS}
      ${CMAKE_SOURCE_DIR}/src
      ${CMAKE_SOURCE_DIR}/include
    WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
    COMMENT "Running cppcheck with custom rules"
  )

  add_custom_target(cppcheck-xml
    COMMAND ${CPPCHECK_EXECUTABLE}
      --enable=all
      --suppress=missingIncludeSystem
      --suppress=unusedFunction
      --suppress=unmatchedSuppression
      --inline-suppr
      --std=c++20
      --check-level=exhaustive
      --xml
      --xml-version=2
      -I${CMAKE_SOURCE_DIR}
      -I${CMAKE_SOURCE_DIR}/include
      -I${CMAKE_SOURCE_DIR}/src
      ${CPPCHECK_RULE_ARGS}
      ${CMAKE_SOURCE_DIR}/src
      ${CMAKE_SOURCE_DIR}/include
      2> cppcheck-report.xml
    WORKING_DIRECTORY ${CMAKE_BINARY_DIR}
    COMMENT "Running cppcheck with custom rules (XML output)"
  )
endif()

# clang-tidy target
find_program(CLANG_TIDY_EXECUTABLE NAMES clang-tidy)
if(CLANG_TIDY_EXECUTABLE)
  # Collect all source and header files (project + tests).
  # gtest/gmock sources live under ${CMAKE_BINARY_DIR}/_deps and are not
  # picked up here.
  file(GLOB_RECURSE ALL_CXX_SOURCE_FILES
    ${CMAKE_SOURCE_DIR}/src/*.cc
    ${CMAKE_SOURCE_DIR}/include/*.h
    ${CMAKE_SOURCE_DIR}/tests/*.cc
  )

  add_custom_target(tidy
    COMMAND ${CLANG_TIDY_EXECUTABLE}
      -p ${CMAKE_BINARY_DIR}
      ${ALL_CXX_SOURCE_FILES}
    WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
    COMMENT "Running clang-tidy"
  )
endif()

# scan-build target
find_program(SCAN_BUILD_EXECUTABLE NAMES scan-build)
if(SCAN_BUILD_EXECUTABLE)
  add_custom_target(scan-build
    COMMAND ${SCAN_BUILD_EXECUTABLE}
      -o ${CMAKE_BINARY_DIR}/scan-build-reports
      --exclude ${CMAKE_BINARY_DIR}/_deps
      cmake --build ${CMAKE_BINARY_DIR} --clean-first
    WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
    COMMENT "Running scan-build"
  )
endif()
