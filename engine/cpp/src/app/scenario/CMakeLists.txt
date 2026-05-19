# ------------------------------------------------------------------
# sts2::app_scenario — JSON scenario loader for sts2_fight
# ------------------------------------------------------------------
# Owns the nlohmann/json v3.12.0 dependency PRIVATELY. Linked only by the
# sts2_fight executable and sts2_simulator_tests; sts2_simulator core does
# NOT see this dep. Q2-ADR-001 keeps the verifier path on hand-rolled JSON.

include(FetchContent)

FetchContent_Declare(
    nlohmann_json
    GIT_REPOSITORY https://github.com/nlohmann/json.git
    GIT_TAG        v3.12.0
)
set(JSON_BuildTests OFF CACHE BOOL "" FORCE)
set(JSON_Install    OFF CACHE BOOL "" FORCE)
FetchContent_MakeAvailable(nlohmann_json)

add_library(sts2_app_scenario STATIC scenario.cc)
add_library(sts2::app_scenario ALIAS sts2_app_scenario)

target_compile_features(sts2_app_scenario PUBLIC cxx_std_20)

target_include_directories(sts2_app_scenario
    PUBLIC  $<BUILD_INTERFACE:${PROJECT_SOURCE_DIR}/engine/cpp/include>
)

target_link_libraries(sts2_app_scenario
    PUBLIC  sts2::simulator
    PRIVATE nlohmann_json::nlohmann_json)

sts2_apply_warnings(sts2_app_scenario)
