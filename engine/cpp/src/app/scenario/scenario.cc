#include "sts2/app/scenario.h"

#include <stdexcept>

namespace sts2::app {

Scenario load_scenario(std::string_view /*path*/) {
  throw std::runtime_error("scenario loader: not yet implemented");
}

BuiltCombat build_combat(const Scenario& /*s*/,
                         std::optional<std::uint64_t> /*seed_override*/) {
  throw std::runtime_error("scenario loader: not yet implemented");
}

}  // namespace sts2::app
