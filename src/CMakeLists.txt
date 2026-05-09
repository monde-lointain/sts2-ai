# ------------------------------------------------------------------
# sts2::simulator — combined game/render/input static library
# ------------------------------------------------------------------
add_library(sts2_simulator STATIC)
add_library(sts2::simulator ALIAS sts2_simulator)

target_sources(sts2_simulator PRIVATE
    # game module
    game/rng.cc
    game/powers.cc
    game/damage.cc
    game/cards.cc
    game/enemies.cc
    game/deck.cc
    game/hand.cc
    game/combat.cc
    # render module
    render/bar.cc
    render/console.cc
    render/render.cc
    render/ai_recommendation.cc
    render/render_internal.h
    # input module
    input/input.cc
    input/input_internal.h
    # app module
    app/args.cc
    app/prompts.cc
)

target_compile_features(sts2_simulator PUBLIC cxx_std_20)

target_include_directories(sts2_simulator
    PUBLIC  $<BUILD_INTERFACE:${PROJECT_SOURCE_DIR}/include>
    PRIVATE ${PROJECT_SOURCE_DIR}/src
)

sts2_apply_warnings(sts2_simulator)

# ------------------------------------------------------------------
# sts2::ai — battle recommender library
# ------------------------------------------------------------------
add_library(sts2_ai STATIC)
add_library(sts2::ai ALIAS sts2_ai)

target_sources(sts2_ai PRIVATE
    ai/state.cc
    ai/probability.cc
    ai/transition.cc
    ai/search.cc
    ai/recommend.cc
)

target_compile_features(sts2_ai PUBLIC cxx_std_20)

target_include_directories(sts2_ai
    PUBLIC  $<BUILD_INTERFACE:${PROJECT_SOURCE_DIR}/include>
    PRIVATE ${PROJECT_SOURCE_DIR}/src)

target_link_libraries(sts2_ai PUBLIC sts2::simulator)

sts2_apply_warnings(sts2_ai)

# ------------------------------------------------------------------
# sts2_fight — fight simulator executable
# ------------------------------------------------------------------
add_executable(sts2_fight main.cc)
target_link_libraries(sts2_fight PRIVATE sts2::simulator sts2::ai)
sts2_apply_warnings(sts2_fight)
