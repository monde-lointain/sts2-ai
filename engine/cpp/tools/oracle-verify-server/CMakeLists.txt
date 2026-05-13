# sts2_oracle_verify_server — Q2 verify-server executable (Q2-ADR-003).
#
# AF_UNIX SOCK_STREAM cold-path RPC daemon. Forward-laid for Q12. Builds
# unconditionally (the executable is a deployable tool, not a test artifact);
# the library it wraps (sts2::oracle_verify_server) only depends on the
# adapter + AI search stack (no Arrow/Parquet), so the executable is light
# enough to be wired outside STS2_BUILD_TESTS and STS2_BUILD_ORACLE_SINK.

add_executable(sts2_oracle_verify_server main.cc)

target_compile_features(sts2_oracle_verify_server PRIVATE cxx_std_20)

target_link_libraries(sts2_oracle_verify_server PRIVATE
    sts2::oracle_verify_server
    sts2::oracle_adapter
    sts2::ai
    sts2::simulator
)

sts2_apply_warnings(sts2_oracle_verify_server)
