#include "sts2/input/input.h"

#include <cctype>
#include <istream>
#include <string>

#include "input/input_internal.h"

namespace sts2::input::detail {

std::string trim(std::string s) {
  size_t b = 0;
  while (b < s.size() && std::isspace(static_cast<unsigned char>(s[b]))) ++b;
  size_t e = s.size();
  while (e > b && std::isspace(static_cast<unsigned char>(s[e - 1]))) --e;
  return s.substr(b, e - b);
}

bool parse_nonneg_int(const std::string& s, int& out) {
  if (s.empty()) return false;
  int v = 0;
  for (char ch : s) {
    if (!std::isdigit(static_cast<unsigned char>(ch))) return false;
    v = v * 10 + (ch - '0');
    if (v > 1000000) return false;
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
    a.kind = Action::Quit;
    return a;
  }
  line = detail::trim(std::move(line));
  if (line.empty()) {
    a.kind = Action::Invalid;
    return a;
  }
  if (line == "e" || line == "E") {
    a.kind = Action::EndTurn;
    return a;
  }
  if (line == "q" || line == "Q") {
    a.kind = Action::Quit;
    return a;
  }
  int idx = 0;
  if (detail::parse_nonneg_int(line, idx)) {
    a.kind = Action::PlayCard;
    a.card_idx = idx;
    return a;
  }
  a.kind = Action::Invalid;
  return a;
}

int read_index(std::istream& in, int max_inclusive) {
  std::string line;
  if (!std::getline(in, line)) return -1;
  line = detail::trim(std::move(line));
  int v = 0;
  if (!detail::parse_nonneg_int(line, v)) return -1;
  if (v > max_inclusive) return -1;
  return v;
}

}  // namespace sts2::input
