#pragma once

#include <string>
#include <string_view>

// Q2 oracle-verify-server handler. Per Q2-ADR-003, the cold-path RPC is a
// stateless function that consumes a JSON request and emits a JSON response.
// The socket-bind / accept-loop concern lives in the tools/oracle-verify-
// server/ executable; this library is pure orchestration so the same code
// path is exercised by in-process gtests (no socket bind).
//
// Re-entrancy: handle_request is stateless. Each call constructs its own
// Search instance (no cross-request caching in Phase-1A; that's Q12 / batch-
// mode work). Safe to call from multiple threads simultaneously.

namespace sts2::oracle::verify_server {

// Entry point. Consumes a JSON request, runs the verify orchestration
// (parse envelope -> adapter -> search OR reject), serializes a JSON
// response. Never throws — all internal failures are translated to a
// verified=false response with a stable `reason` enum.
[[nodiscard]] std::string handle_request(std::string_view request_json);

}  // namespace sts2::oracle::verify_server
