find_program(CLOC_EXECUTABLE NAMES cloc)

if(CLOC_EXECUTABLE)
  add_custom_target(cloc
    COMMAND ${CLOC_EXECUTABLE}
      ${CMAKE_SOURCE_DIR}/src
      ${CMAKE_SOURCE_DIR}/include
    WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
    COMMENT "Running cloc"
  )

  add_custom_target(cloc-full
    COMMAND ${CLOC_EXECUTABLE}
      --by-file
      ${CMAKE_SOURCE_DIR}/src
      ${CMAKE_SOURCE_DIR}/include
      ${CMAKE_SOURCE_DIR}/tests
    WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
    COMMENT "Running cloc (full)"
  )

  add_custom_target(cloc-report
    COMMAND ${CLOC_EXECUTABLE}
      --json
      --out=${CMAKE_BINARY_DIR}/cloc-report.json
      ${CMAKE_SOURCE_DIR}/src
      ${CMAKE_SOURCE_DIR}/include
    WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
    COMMENT "Running cloc (JSON report)"
  )

  add_custom_target(cloc-report-full
    COMMAND ${CLOC_EXECUTABLE}
      --by-file
      --json
      --out=${CMAKE_BINARY_DIR}/cloc-report-full.json
      ${CMAKE_SOURCE_DIR}/src
      ${CMAKE_SOURCE_DIR}/include
      ${CMAKE_SOURCE_DIR}/tests
    WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
    COMMENT "Running cloc (full JSON report)"
  )
endif()
