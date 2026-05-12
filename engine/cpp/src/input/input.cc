#include "sts2/input/input.h"

#include <cctype>
#include <istream>
#include <string>

#include "input/input_internal.h"
#include "sts2/game/index_types.h"

namespace sts2::input::detail {

std::string trim(const std::string& s) {
  size_t b = 0;
  while (b < s.size() && std::isspace(static_cast<unsigned char>(s[b]))) {
    ++b;
  }
  size_t e = s.size();
  while (e > b && std::isspace(static_cast<unsigned char>(s[e - 1]))) {
    --e;
  }
  return s.substr(b, e - b);
}

bool parse_nonneg_int(const std::string& s, int& out) {
  if (s.empty()) {
    return false;
  }
  int v = 0;
  for (char ch : s) {
    if (!std::isdigit(static_cast<unsigned char>(ch))) {
      return false;
    }
    v = (v * 10) + (ch - '0');
    if (v > 1000000) {
      return false;
    }
  }
  out = v;
  return true;
}

}  // namespace sts2::input::detail

namespace sts2::input {

Action read_action(std::istream& in) {
  Action a;
  std::string line;
  if (!std::getline(in, line)) {
    a.kind = Action::kQuit;
    return a;
  }
  line = detail::trim(line);
  if (line.empty()) {
    a.kind = Action::kInvalid;
    return a;
  }
  if (line == "e" || line == "E") {
    a.kind = Action::kEndTurn;
    return a;
  }
  if (line == "q" || line == "Q") {
    a.kind = Action::kQuit;
    return a;
  }
  int idx = 0;
  if (detail::parse_nonneg_int(line, idx)) {
    a.kind = Action::kPlayCard;
    a.card_idx = sts2::game::HandIndex{idx};
    return a;
  }
  a.kind = Action::kInvalid;
  return a;
}

int read_index(std::istream& in, int max_inclusive) {
  std::string line;
  if (!std::getline(in, line)) {
    return -1;
  }
  line = detail::trim(line);
  int v = 0;
  if (!detail::parse_nonneg_int(line, v)) {
    return -1;
  }
  if (v > max_inclusive) {
    return -1;
  }
  return v;
}

}  // namespace sts2::input
