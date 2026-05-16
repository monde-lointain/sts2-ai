// Seed pinner: regenerates tests/seeds/expected_values.h.
//
// Build only when STS2_BUILD_TESTS is ON, then run:
//   build\ninja-debug\Debug\sts2_seed_pinner.exe >
//   tests\seeds\expected_values.h
//
// Optional flag:
//   --manifest   Emit AlgorithmManifest JSON + registry-SHA + per-encounter
//                seed list to stderr (Q2-ADR-005 stamping discipline). The
//                generated header on stdout is unchanged regardless of flag.
//
// Pinned values are STL-implementation-specific (std::uniform_int_distribution
// differs across libstdc++ / libc++ / MSVC STL). Re-pin on toolchain change.

#include <array>
#include <climits>
#include <cstdint>
#include <iostream>
#include <optional>
#include <sstream>
#include <string>
#include <string_view>
#include <vector>

#include "sts2/game/card.h"
#include "sts2/game/card_effects.h"
#include "sts2/game/cards.h"
#include "sts2/game/enemies.h"
#include "sts2/game/enemy.h"
#include "sts2/game/rng.h"
#include "sts2/game/types.h"
#include "sts2/oracle/adapter/manifest.h"
#include "sts2/oracle/registry/sha.h"

namespace {

// Hard cap on brute-force seed search. 2^20 ~= 1M; failure is loud (we emit
// #error directives at the top of the header so a regen failure breaks the
// build instead of producing silently-wrong data).
constexpr std::uint64_t kSeedSearchCap = 1ULL << 20;

// Q2 per-encounter pin registry (S2-T5). Forward-laid: today's entries cover
// only CULTISTS_NORMAL; per-encounter coverage grows in Phase-1.5+. Per-entry
// HP / shuffle / deck constants stay where they are below; this table is the
// shape consumed by the `--manifest` sidecar emission per Q2-ADR-005.
struct EncounterSeedEntry {
  std::string_view encounter_id;
  std::uint64_t seed;
};

constexpr std::array<EncounterSeedEntry, 2> kEncounterSeeds = {{
    {"CULTISTS_NORMAL", 0x42ULL},
    {"CULTISTS_NORMAL", 0xC0FFEEULL},
}};

std::string card_id_name(sts2::game::CardId id) {
  return "sts2::game::CardId::" +
         std::string(sts2::game::card_effects::card_id_cpp_name(id));
}

// Returns the first seed in [0, kSeedSearchCap) for which factory(Rng{seed})
// produces an Enemy with vitals.hp == target_hp; std::nullopt if not found.
std::optional<std::uint64_t> find_seed_for_hp(
    sts2::game::Enemy (*factory)(sts2::game::Rng&), int target_hp) {
  for (std::uint64_t seed = 0; seed < kSeedSearchCap; ++seed) {
    sts2::game::Rng rng(seed);
    sts2::game::Enemy e = factory(rng);
    if (e.vitals.hp == sts2::game::Stat{target_hp}) {
      return seed;
    }
  }
  return std::nullopt;
}

// Emits a constant if the seed was found. Otherwise emits a comment in-place
// and appends a failure message to `failures` so the caller can write a
// matching #error directive at the top of the file (outside any namespace).
void emit_seed_or_record_failure(std::ostringstream& out,
                                 std::vector<std::string>& failures,
                                 const char* name,
                                 std::optional<std::uint64_t> seed,
                                 int target_hp, const char* factory_label) {
  if (seed.has_value()) {
    out << "inline constexpr std::uint64_t " << name << " = 0x" << std::hex
        << *seed << std::dec << "ULL;\n";
  } else {
    out << "// FAILED: no seed in [0, 2^20) produced " << factory_label
        << " hp=" << target_hp << "\n";
    std::ostringstream msg;
    msg << "seed-pinner: no seed for " << name
        << " in [0, 2^20). Widen kSeedSearchCap or investigate.";
    failures.push_back(msg.str());
  }
}

// Emit the Q2-ADR-005 AlgorithmManifest JSON sidecar to stderr. Shape:
//   {
//     "algorithm_sha": "...",
//     "build_sha": "...",
//     "version_tag": "...",
//     "registry_sha": "<64-hex>",
//     "encounters": [{"encounter_id": "...", "seeds": [..]}]
//   }
// Hand-rolled formatter — no external JSON dep. Encounter IDs in the table
// are simple ASCII identifiers (CULTISTS_NORMAL etc.), so no escaping logic
// is needed; if that ever changes the formatter must learn to escape.
void emit_manifest_json(std::ostream& os) {
  const auto manifest = sts2::oracle::adapter::current_manifest();
  const std::string registry_sha =
      sts2::oracle::registry::current_phase1_registry_sha256();

  os << "{\n";
  os << "  \"algorithm_sha\": \"" << manifest.algorithm_sha << "\",\n";
  os << "  \"build_sha\": \"" << manifest.build_sha << "\",\n";
  os << "  \"version_tag\": \"" << manifest.version_tag << "\",\n";
  os << "  \"registry_sha\": \"" << registry_sha << "\",\n";
  os << "  \"encounters\": [\n";

  // Group consecutive same-encounter entries into a single "seeds" array.
  // The constexpr table is already laid out per-encounter so a simple
  // single-pass group works without sorting.
  for (std::size_t i = 0; i < kEncounterSeeds.size();) {
    const std::string_view enc = kEncounterSeeds[i].encounter_id;
    os << "    {\"encounter_id\": \"" << enc << "\", \"seeds\": [";
    std::size_t j = i;
    bool first = true;
    while (j < kEncounterSeeds.size() &&
           kEncounterSeeds[j].encounter_id == enc) {
      if (!first) {
        os << ", ";
      }
      os << kEncounterSeeds[j].seed;
      first = false;
      ++j;
    }
    os << "]}";
    if (j < kEncounterSeeds.size()) {
      os << ",";
    }
    os << "\n";
    i = j;
  }

  os << "  ]\n";
  os << "}\n";
}

}  // namespace

int main(int argc, char** argv) {
  bool emit_manifest = false;
  for (int i = 1; i < argc; ++i) {
    const std::string_view arg{argv[i]};
    if (arg == "--manifest") {
      emit_manifest = true;
    } else {
      std::cerr << "seed-pinner: unknown argument: " << arg << "\n";
      return 2;
    }
  }

  constexpr std::uint64_t kRngTestSeed = 0xDEADBEEFCAFEULL;
  constexpr std::uint64_t kCombatTestSeed = 0xC0FFEEULL;
  constexpr std::uint64_t kCultistTestSeed = 0x42ULL;

  // 1. shuffle({1, 2})
  std::array<int, 2> shuffle_2{};
  {
    sts2::game::Rng rng(kRngTestSeed);
    std::vector<int> v = {1, 2};
    rng.shuffle(v);
    shuffle_2 = {v[0], v[1]};
  }

  // 2. shuffle({0..9})
  std::array<int, 10> shuffle_10{};
  {
    sts2::game::Rng rng(kRngTestSeed);
    std::vector<int> v = {0, 1, 2, 3, 4, 5, 6, 7, 8, 9};
    rng.shuffle(v);
    for (size_t i = 0; i < 10; ++i) {
      shuffle_10[i] = v[i];
    }
  }

  // 3. uniform_int(0, 9) x10
  std::array<int, 10> seq_0_9{};
  {
    sts2::game::Rng rng(kRngTestSeed);
    for (int i = 0; i < 10; ++i) {
      seq_0_9[i] = rng.uniform_int(0, 9);
    }
  }

  // 4. make_calcified_cultist HP at seed 0x42
  int calcified_hp_seed42 = 0;
  {
    sts2::game::Rng rng(kCultistTestSeed);
    calcified_hp_seed42 =
        sts2::enemies::make_calcified_cultist(rng).vitals.hp.value();
  }

  // 5. make_damp_cultist HP at seed 0x42
  int damp_hp_seed42 = 0;
  {
    sts2::game::Rng rng(kCultistTestSeed);
    damp_hp_seed42 = sts2::enemies::make_damp_cultist(rng).vitals.hp.value();
  }

  // 6. brute-force seeds for calcified HPs 38..41
  auto s_cal_38 = find_seed_for_hp(&sts2::enemies::make_calcified_cultist, 38);
  auto s_cal_39 = find_seed_for_hp(&sts2::enemies::make_calcified_cultist, 39);
  auto s_cal_40 = find_seed_for_hp(&sts2::enemies::make_calcified_cultist, 40);
  auto s_cal_41 = find_seed_for_hp(&sts2::enemies::make_calcified_cultist, 41);

  // 7. brute-force seeds for damp HPs 51..53
  auto s_damp_51 = find_seed_for_hp(&sts2::enemies::make_damp_cultist, 51);
  auto s_damp_52 = find_seed_for_hp(&sts2::enemies::make_damp_cultist, 52);
  auto s_damp_53 = find_seed_for_hp(&sts2::enemies::make_damp_cultist, 53);

  // 8. shuffled silent starter deck (12 cards)
  std::array<sts2::game::CardId, 12> deck_shuffled{};
  {
    sts2::game::Rng rng(kCombatTestSeed);
    std::vector<sts2::game::Card> deck =
        sts2::cards::make_silent_starter_deck();
    rng.shuffle(deck);
    for (size_t i = 0; i < 12; ++i) {
      deck_shuffled[i] = deck[i].id;
    }
  }

  // 9. two consecutive uniform_int(0, INT_MAX)
  int first_intmax = 0;
  int second_intmax = 0;
  {
    sts2::game::Rng rng(kRngTestSeed);
    first_intmax = rng.uniform_int(0, INT_MAX);
    second_intmax = rng.uniform_int(0, INT_MAX);
  }

  // Body of the header (the namespace block). Failures are recorded here and
  // surfaced as #error directives written before the namespace opens, so they
  // sit at file scope and unambiguously fire at preprocessing time of any TU.
  std::vector<std::string> failures;
  std::ostringstream body;

  body << "namespace sts2::tests::seeds {\n";
  body << "\n";
  body << "inline constexpr std::uint64_t kRngTestSeed     = 0x" << std::hex
       << kRngTestSeed << std::dec << "ULL;\n";
  body << "inline constexpr std::uint64_t kCombatTestSeed  = 0x" << std::hex
       << kCombatTestSeed << std::dec << "ULL;\n";
  body << "inline constexpr std::uint64_t kCultistTestSeed = 0x" << std::hex
       << kCultistTestSeed << std::dec << "ULL;\n";
  body << "\n";
  body << "// T-RNG-005: 10 successive uniform_int(0, 9) calls on "
          "Rng{kRngTestSeed}.\n";
  body << "inline constexpr std::array<int, 10> kRngSeq09 = {";
  for (size_t i = 0; i < seq_0_9.size(); ++i) {
    body << seq_0_9[i];
    if (i + 1 < seq_0_9.size()) {
      body << ", ";
    }
  }
  body << "};\n";
  body << "\n";
  body << "// T-RNG-055: shuffle({1, 2}) on Rng{kRngTestSeed}.\n";
  body << "inline constexpr std::array<int, 2>  kShuffle2  = {" << shuffle_2[0]
       << ", " << shuffle_2[1] << "};\n";
  body << "\n";
  body << "// T-RNG-060: shuffle({0..9}) on Rng{kRngTestSeed}.\n";
  body << "inline constexpr std::array<int, 10> kShuffle10 = {";
  for (size_t i = 0; i < shuffle_10.size(); ++i) {
    body << shuffle_10[i];
    if (i + 1 < shuffle_10.size()) {
      body << ", ";
    }
  }
  body << "};\n";
  body << "\n";
  body << "// T-ENM-005 / T-ENM-015: cultist HPs from "
          "make_*_cultist(Rng{kCultistTestSeed}).\n";
  body << "inline constexpr int kCalcifiedHpSeed42 = " << calcified_hp_seed42
       << ";\n";
  body << "inline constexpr int kDampHpSeed42      = " << damp_hp_seed42
       << ";\n";
  body << "\n";
  body << "// T-ENM-010 / T-ENM-020: first seed in [0, 2^20) producing a given "
          "HP.\n";
  emit_seed_or_record_failure(body, failures, "kCalcifiedSeedHp38", s_cal_38,
                              38, "make_calcified_cultist");
  emit_seed_or_record_failure(body, failures, "kCalcifiedSeedHp39", s_cal_39,
                              39, "make_calcified_cultist");
  emit_seed_or_record_failure(body, failures, "kCalcifiedSeedHp40", s_cal_40,
                              40, "make_calcified_cultist");
  emit_seed_or_record_failure(body, failures, "kCalcifiedSeedHp41", s_cal_41,
                              41, "make_calcified_cultist");
  emit_seed_or_record_failure(body, failures, "kDampSeedHp51", s_damp_51, 51,
                              "make_damp_cultist");
  emit_seed_or_record_failure(body, failures, "kDampSeedHp52", s_damp_52, 52,
                              "make_damp_cultist");
  emit_seed_or_record_failure(body, failures, "kDampSeedHp53", s_damp_53, 53,
                              "make_damp_cultist");
  body << "\n";
  body << "// Combat tests: order of make_silent_starter_deck() after "
          "Rng{kCombatTestSeed}.shuffle(deck).\n";
  body << "inline constexpr std::array<sts2::game::CardId, 12> "
          "kSilentDeckShuffledC0Ffee = {\n";
  for (size_t i = 0; i < deck_shuffled.size(); ++i) {
    body << "    " << card_id_name(deck_shuffled[i]);
    if (i + 1 < deck_shuffled.size()) {
      body << ",";
    }
    body << "\n";
  }
  body << "};\n";
  body << "\n";
  body << "// Determinism reference: two consecutive uniform_int(0, INT_MAX) "
          "on Rng{kRngTestSeed}.\n";
  body << "inline constexpr int kRngFirstUniform0Intmax  = " << first_intmax
       << ";\n";
  body << "inline constexpr int kRngSecondUniform0Intmax = " << second_intmax
       << ";\n";
  body << "\n";
  body << "}  // namespace sts2::tests::seeds\n";

  std::ostringstream out;
  out << "#pragma once\n";
  out << "\n";
  out << "// AUTO-GENERATED by tools/seed-pinner/pin_seeds.cc. DO NOT EDIT BY "
         "HAND.\n";
  out << "//\n";
  out << "// Toolchain: Linux x86_64, GCC + libstdc++ (current pins captured\n";
  out << "// 2026-05-12 per Q2 S2-T3). Values are NOT portable across STL\n";
  out << "// implementations. libstdc++, libc++, and MSVC STL each implement\n";
  out << "// std::uniform_int_distribution differently, so the same Rng seed "
         "will\n";
  out << "// produce different sequences. R12 (pin determinism across STL) "
         "carries\n";
  out << "// the cross-platform divergence risk; STL-divergent action flip is "
         "a\n";
  out << "// project-lead re-surface trigger per S2 wave-gate guidance.\n";
  out << "//\n";
  out << "// To regenerate after a toolchain change:\n";
  out << "//   cmake -B build -S . -DCMAKE_BUILD_TYPE=Debug\n";
  out << "//   cmake --build build -j$(nproc)\n";
  out << "//   build/Debug/sts2_seed_pinner > "
         "engine/cpp/tests/seeds/expected_values.h\n";
  out << "\n";
  out << "#include <array>\n";
  out << "#include <cstdint>\n";
  out << "\n";
  out << "#include \"sts2/game/types.h\"\n";
  out << "\n";
  // #error directives must sit at column 0 outside any namespace. Emit them
  // here, before the namespace opens, so a regen failure unambiguously breaks
  // any translation unit that includes this header.
  for (const auto& msg : failures) {
    out << "#error \"" << msg << "\"\n";
  }
  if (!failures.empty()) {
    out << "\n";
  }
  out << body.str();

  std::cout << out.str();

  // Print search-range diagnostic to stderr so it doesn't pollute the header
  // but is still visible during regeneration.
  auto report = [](const char* label, std::optional<std::uint64_t> s) {
    if (!s.has_value()) {
      std::cerr << "WARN: " << label << " not found within 2^20\n";
    } else if (*s >= (1ULL << 16)) {
      std::cerr << "INFO: " << label << " = 0x" << std::hex << *s << std::dec
                << " (above 2^16)\n";
    }
  };
  report("kCalcifiedSeedHp38", s_cal_38);
  report("kCalcifiedSeedHp39", s_cal_39);
  report("kCalcifiedSeedHp40", s_cal_40);
  report("kCalcifiedSeedHp41", s_cal_41);
  report("kDampSeedHp51", s_damp_51);
  report("kDampSeedHp52", s_damp_52);
  report("kDampSeedHp53", s_damp_53);

  if (emit_manifest) {
    emit_manifest_json(std::cerr);
  }

  return 0;
}
