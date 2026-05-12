#pragma once

#include <cstdint>
#include <cstring>
#include <span>
#include <stdexcept>
#include <string>
#include <utility>

// Little-endian byte cursor used by both the M1 binary state-blob reader and
// the proto3 envelope hand-parser. Adapter-internal — not in any public
// header. Out-of-range reads throw the caller-provided exception type so the
// reader/parser can surface its own error type without leaking this helper.

namespace sts2::oracle::adapter::detail {

class BinaryCursor {
 public:
  explicit BinaryCursor(std::span<const std::uint8_t> bytes) noexcept
      : bytes_(bytes), pos_(0) {}

  [[nodiscard]] std::size_t pos() const noexcept { return pos_; }
  [[nodiscard]] std::size_t remaining() const noexcept {
    return bytes_.size() - pos_;
  }
  [[nodiscard]] bool exhausted() const noexcept { return pos_ >= bytes_.size(); }
  [[nodiscard]] std::span<const std::uint8_t> all() const noexcept {
    return bytes_;
  }

  template <typename Ex>
  void require(std::size_t n, const char* what) {
    if (remaining() < n) {
      throw Ex(std::string("truncated: ") + what);
    }
  }

  template <typename Ex>
  std::uint8_t read_u8(const char* what) {
    require<Ex>(1, what);
    return bytes_[pos_++];
  }

  template <typename Ex>
  std::uint16_t read_u16(const char* what) {
    require<Ex>(2, what);
    const std::uint16_t v =
        static_cast<std::uint16_t>(bytes_[pos_]) |
        (static_cast<std::uint16_t>(bytes_[pos_ + 1]) << 8);
    pos_ += 2;
    return v;
  }

  template <typename Ex>
  std::uint32_t read_u32(const char* what) {
    require<Ex>(4, what);
    const std::uint32_t v =
        static_cast<std::uint32_t>(bytes_[pos_]) |
        (static_cast<std::uint32_t>(bytes_[pos_ + 1]) << 8) |
        (static_cast<std::uint32_t>(bytes_[pos_ + 2]) << 16) |
        (static_cast<std::uint32_t>(bytes_[pos_ + 3]) << 24);
    pos_ += 4;
    return v;
  }

  template <typename Ex>
  std::int32_t read_i32(const char* what) {
    return static_cast<std::int32_t>(read_u32<Ex>(what));
  }

  template <typename Ex>
  void read_bytes_into(std::uint8_t* dst, std::size_t n, const char* what) {
    require<Ex>(n, what);
    std::memcpy(dst, bytes_.data() + pos_, n);
    pos_ += n;
  }

  template <typename Ex>
  std::span<const std::uint8_t> read_span(std::size_t n, const char* what) {
    require<Ex>(n, what);
    auto s = bytes_.subspan(pos_, n);
    pos_ += n;
    return s;
  }

  // Length-prefixed UTF-8 string with the prefix width specified by the
  // caller (u8 / u16 / u32 — width is part of the wire schema).
  template <typename Ex, typename LenT>
  std::string read_lp_string(const char* what) {
    static_assert(std::is_same_v<LenT, std::uint8_t> ||
                      std::is_same_v<LenT, std::uint16_t> ||
                      std::is_same_v<LenT, std::uint32_t>,
                  "LenT must be u8/u16/u32");
    std::uint32_t len = 0;
    if constexpr (std::is_same_v<LenT, std::uint8_t>) {
      len = read_u8<Ex>(what);
    } else if constexpr (std::is_same_v<LenT, std::uint16_t>) {
      len = read_u16<Ex>(what);
    } else {
      len = read_u32<Ex>(what);
    }
    require<Ex>(len, what);
    std::string s(reinterpret_cast<const char*>(bytes_.data() + pos_), len);
    pos_ += len;
    return s;
  }

 private:
  std::span<const std::uint8_t> bytes_;
  std::size_t pos_;
};

}  // namespace sts2::oracle::adapter::detail
