# ------------------------------------------------------------------
# sts2::simulator — combined game/render/input static library
# ------------------------------------------------------------------
add_library(sts2_simulator STATIC)
add_library(sts2::simulator ALIAS sts2_simulator)

target_sources(sts2_simulator PRIVATE
    # game module
    game/Rng.cpp
    game/Powers.cpp
    game/Damage.cpp
    game/Cards.cpp
    game/Enemies.cpp
    game/Combat.cpp
    # render module
    render/Bar.cpp
    render/Console.cpp
    render/Render.cpp
    # input module
    input/Input.cpp
    # app module
    app/Args.cpp
    app/Prompts.cpp
    # headers — listed so IDEs surface them in the project view
    game/Types.h
    game/Rng.h
    game/Power.h
    game/Powers.h
    game/Damage.h
    game/Card.h
    game/Cards.h
    game/Vitals.h
    game/Player.h
    game/Enemy.h
    game/Enemies.h
    game/Combat.h
    render/Ansi.h
    render/Bar.h
    render/Console.h
    render/Glyphs.h
    render/Render.h
    render/Render_internal.h
    input/Input.h
    input/Input_internal.h
    app/Args.h
    app/Prompts.h
)

target_compile_features(sts2_simulator PUBLIC cxx_std_20)

target_include_directories(sts2_simulator PUBLIC
    $<BUILD_INTERFACE:${PROJECT_SOURCE_DIR}/src>
)

sts2_apply_warnings(sts2_simulator)

# ------------------------------------------------------------------
# sts2_fight — fight simulator executable
# ------------------------------------------------------------------
add_executable(sts2_fight main.cpp)
target_link_libraries(sts2_fight PRIVATE sts2::simulator)
sts2_apply_warnings(sts2_fight)
