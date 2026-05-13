# ------------------------------------------------------------------
# sts2::oracle_verify_server — Q2 verify-server library (Q2-ADR-003)
# ------------------------------------------------------------------
# JSON-over-Unix-socket cold-path verify(state_blob, budget) RPC for Q12.
# Forward-laid (Q12 not yet booted). The library is socket-unaware: the
# tools/oracle-verify-server/ executable wraps it in an AF_UNIX accept loop.
# In-process gtests exercise handle_request directly (no socket bind).
#
# Per Q2-ADR-001 hand-roll-the-wire ethos: JSON parser/serializer and base64
# decoder are implemented in this library; no nlohmann_json or other deps.
# Links sts2::oracle_adapter (envelope + adapter facade) + sts2::ai (Search
# + transition) + sts2::simulator (CardEffect table for card_id stringify).
add_library(sts2_oracle_verify_server STATIC)
add_library(sts2::oracle_verify_server ALIAS sts2_oracle_verify_server)

target_sources(sts2_oracle_verify_server PRIVATE
    protocol.cc
    server.cc
    base64_internal.h
    json_internal.h
)

target_compile_features(sts2_oracle_verify_server PUBLIC cxx_std_20)

target_include_directories(sts2_oracle_verify_server
    PUBLIC  $<BUILD_INTERFACE:${PROJECT_SOURCE_DIR}/engine/cpp/include>
    PRIVATE ${PROJECT_SOURCE_DIR}/engine/cpp/src
)

target_link_libraries(sts2_oracle_verify_server PUBLIC
    sts2::oracle_adapter
    sts2::ai
    sts2::simulator
)

sts2_apply_warnings(sts2_oracle_verify_server)
