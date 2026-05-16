// sts2_oracle_verify_server — Q2 verify-server executable (Q2-ADR-003).
//
// AF_UNIX SOCK_STREAM cold-path RPC daemon. Forward-laid for Q12 (not yet
// booted). The handler library (sts2::oracle_verify_server) is socket-
// unaware; this wrapper supplies:
//   1. Socket bind/listen at the path in $STS2_Q2_VERIFY_SOCKET (default
//      /tmp/sts2-q2-verify.sock).
//   2. Single-threaded accept loop — for each connection: read request
//      until client closes the write side (EOF on SHUT_WR), invoke
//      handle_request, write response, close.
//   3. SIGTERM/SIGINT clean shutdown that unlinks the socket file.
//   4. Re-bind safety: if the socket file exists at start, unlink it
//      before bind (single-instance Phase-1A semantics; coexistence with
//      another running server is a Q12 concern).
//
// Framing: client closes its write side after sending the request (one
// request per connection). This mirrors the simplest cold-path protocol
// the gtests don't need to exercise — they call handle_request directly.

#include <sys/socket.h>
#include <sys/types.h>
#include <sys/un.h>
#include <unistd.h>

#include <cerrno>
#include <csignal>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <stdexcept>
#include <string>
#include <string_view>

#include "sts2/oracle/verify_server/server.h"

namespace {

constexpr const char* kDefaultSocketPath = "/tmp/sts2-q2-verify.sock";
constexpr std::size_t kReadChunkSize = 4096;

// Global socket-path holder so the signal handler can unlink at shutdown.
// volatile sig_atomic_t-safe pointer: we copy the path into a fixed buffer
// at startup; the handler only reads the buffer (never the std::string).
// NOLINTNEXTLINE(cppcoreguidelines-avoid-non-const-global-variables) --
// signal-handler shared state, must be global
char g_socket_path_buf[sizeof(::sockaddr_un{}.sun_path)] = {};
// NOLINTNEXTLINE(cppcoreguidelines-avoid-non-const-global-variables) --
// volatile sig_atomic_t, must be global
volatile std::sig_atomic_t g_shutdown_requested = 0;

extern "C" void on_signal(int /*sig*/) {
  g_shutdown_requested = 1;
  // Best-effort unlink. unlink() is async-signal-safe per POSIX.
  if (g_socket_path_buf[0] != '\0') {
    (void)::unlink(g_socket_path_buf);
  }
}

void install_signal_handlers() {
  struct sigaction sa {};
  sa.sa_handler = on_signal;
  ::sigemptyset(&sa.sa_mask);
  sa.sa_flags = 0;  // No SA_RESTART — accept() should fail with EINTR.
  if (::sigaction(SIGTERM, &sa, nullptr) != 0) {
    throw std::runtime_error("verify-server: sigaction(SIGTERM) failed");
  }
  if (::sigaction(SIGINT, &sa, nullptr) != 0) {
    throw std::runtime_error("verify-server: sigaction(SIGINT) failed");
  }
  // Ignore SIGPIPE — a client that hangs up before we finish writing would
  // otherwise tear down the entire daemon.
  struct sigaction sp {};
  sp.sa_handler = SIG_IGN;
  ::sigemptyset(&sp.sa_mask);
  if (::sigaction(SIGPIPE, &sp, nullptr) != 0) {
    throw std::runtime_error("verify-server: sigaction(SIGPIPE) failed");
  }
}

[[nodiscard]] std::string env_or(const char* key, const char* fallback) {
  const char* v = std::getenv(key);
  return {v != nullptr ? v : fallback};
}

[[nodiscard]] int bind_listen_socket(const std::string& path) {
  if (path.size() + 1U > sizeof(::sockaddr_un{}.sun_path)) {
    throw std::runtime_error(
        "verify-server: socket path exceeds sun_path size");
  }
  // Re-bind safety: unlink stale socket file if present. Phase-1A is
  // single-instance; a coexisting peer must use a different path via the
  // env var.
  if (::unlink(path.c_str()) != 0 && errno != ENOENT) {
    throw std::runtime_error(
        std::string("verify-server: unlink stale socket failed: ") +
        std::strerror(errno));
  }
  const int fd = ::socket(AF_UNIX, SOCK_STREAM, 0);
  if (fd < 0) {
    throw std::runtime_error(std::string("verify-server: socket() failed: ") +
                             std::strerror(errno));
  }
  ::sockaddr_un addr{};
  addr.sun_family = AF_UNIX;
  std::memcpy(addr.sun_path, path.data(), path.size());
  if (::bind(fd, reinterpret_cast<::sockaddr*>(&addr), sizeof(addr)) != 0) {
    const int e = errno;
    ::close(fd);
    throw std::runtime_error(std::string("verify-server: bind() failed: ") +
                             std::strerror(e));
  }
  // Listen backlog: small — cold-path traffic is sequential. Phase-1A.
  if (::listen(fd, 8) != 0) {
    const int e = errno;
    ::close(fd);
    throw std::runtime_error(std::string("verify-server: listen() failed: ") +
                             std::strerror(e));
  }
  return fd;
}

[[nodiscard]] std::string read_request(int conn_fd) {
  // Read until EOF (client closes write side). One request per connection;
  // see file-header comment for framing rationale.
  std::string buf;
  char chunk[kReadChunkSize];
  while (true) {
    const ::ssize_t n = ::read(conn_fd, chunk, sizeof(chunk));
    if (n > 0) {
      buf.append(chunk, static_cast<std::size_t>(n));
    } else if (n == 0) {
      return buf;
    } else {
      if (errno == EINTR) {
        if (g_shutdown_requested != 0) {
          return buf;
        }
        continue;
      }
      throw std::runtime_error(std::string("verify-server: read() failed: ") +
                               std::strerror(errno));
    }
  }
}

void write_all(int conn_fd, std::string_view bytes) {
  std::size_t sent = 0;
  while (sent < bytes.size()) {
    const ::ssize_t n =
        ::write(conn_fd, bytes.data() + sent, bytes.size() - sent);
    if (n > 0) {
      sent += static_cast<std::size_t>(n);
    } else if (n < 0 && errno == EINTR) {
      continue;
    } else {
      throw std::runtime_error(std::string("verify-server: write() failed: ") +
                               std::strerror(errno));
    }
  }
}

int run_server(const std::string& path) {
  const int listen_fd = bind_listen_socket(path);
  std::cerr << "verify-server: listening on " << path << '\n';
  while (g_shutdown_requested == 0) {
    const int conn_fd = ::accept(listen_fd, nullptr, nullptr);
    if (conn_fd < 0) {
      if (errno == EINTR) {
        continue;
      }
      ::close(listen_fd);
      std::cerr << "verify-server: accept() failed: " << std::strerror(errno)
                << '\n';
      return 1;
    }
    try {
      const std::string request = read_request(conn_fd);
      const std::string response =
          sts2::oracle::verify_server::handle_request(request);
      write_all(conn_fd, response);
    } catch (const std::exception& e) {
      // Connection-level errors are logged but don't crash the daemon.
      std::cerr << "verify-server: connection error: " << e.what() << '\n';
    }
    ::close(conn_fd);
  }
  ::close(listen_fd);
  // Signal handler already unlinked, but call again in case the loop exited
  // via a fall-through path (defensive; unlink of a missing path is ENOENT).
  (void)::unlink(path.c_str());
  std::cerr << "verify-server: shutdown clean\n";
  return 0;
}

}  // namespace

int main() {
  try {
    const std::string socket_path =
        env_or("STS2_Q2_VERIFY_SOCKET", kDefaultSocketPath);
    if (socket_path.size() + 1U > sizeof(g_socket_path_buf)) {
      std::cerr << "verify-server: socket path exceeds buffer size\n";
      return 1;
    }
    std::memcpy(g_socket_path_buf, socket_path.data(), socket_path.size());
    g_socket_path_buf[socket_path.size()] = '\0';

    install_signal_handlers();
    return run_server(socket_path);
  } catch (const std::exception& e) {
    std::cerr << "verify-server: fatal: " << e.what() << '\n';
    return 1;
  }
}
