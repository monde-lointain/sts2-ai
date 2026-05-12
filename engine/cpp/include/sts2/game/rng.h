#pragma once

#include <cstdint>
#include <random>
#include <vector>

namespace sts2::game {

class Rng {
 public:
  explicit Rng(uint64_t seed);
  int uniform_int(int lo_inclusive, int hi_inclusive);

  template <typename T>
  void shuffle(std::vector<T>& v) {
    if (v.size() < 2) {
      return;
    }
    for (size_t i = v.size() - 1; i > 0; --i) {
      std::uniform_int_distribution<size_t> dist(0, i);
      size_t j = dist(engine_);
      using std::swap;
      swap(v[i], v[j]);
    }
  }

 private:
  std::mt19937_64 engine_;
};

}  // namespace sts2::game
