find_program(CLANG_FORMAT_EXECUTABLE NAMES clang-format)

if(CLANG_FORMAT_EXECUTABLE)
  file(GLOB_RECURSE ALL_SOURCE_FILES
    ${CMAKE_SOURCE_DIR}/engine/cpp/include/*.h
    ${CMAKE_SOURCE_DIR}/engine/cpp/src/*.cc
    ${CMAKE_SOURCE_DIR}/engine/cpp/src/*.h
    ${CMAKE_SOURCE_DIR}/engine/cpp/tests/*.cc
    ${CMAKE_SOURCE_DIR}/engine/cpp/tests/*.h
  )

  if(ALL_SOURCE_FILES)
    add_custom_target(format
      COMMAND ${CLANG_FORMAT_EXECUTABLE} -i ${ALL_SOURCE_FILES}
      WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
      COMMENT "Running clang-format"
      VERBATIM
    )

    add_custom_target(format-patch
      COMMAND ${CLANG_FORMAT_EXECUTABLE} --dry-run --Werror ${ALL_SOURCE_FILES}
      WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
      COMMENT "Checking format (dry run)"
      VERBATIM
    )
  endif()
endif()
