#include <gtest/gtest.h>

#include "sts2/oracle/adapter/manifest.h"

namespace {

using sts2::oracle::adapter::AlgorithmManifest;
using sts2::oracle::adapter::current_manifest;

TEST(AlgorithmManifest, CurrentManifest_HasNonEmptyFields) {
  // S1-T0 stub: per Q2-ADR-005 the shape (not the SHA content) is the
  // contract for Phase-1A. Real SHA-256 computation lands in S3+.
  const AlgorithmManifest m = current_manifest();
  EXPECT_FALSE(m.algorithm_sha.empty());
  EXPECT_FALSE(m.build_sha.empty());
  EXPECT_FALSE(m.version_tag.empty());
}

TEST(AlgorithmManifest, CurrentManifest_IsDeterministic) {
  // Two successive calls return equal manifests; downstream diagnostic
  // stamping must be deterministic per Q2-ADR-005.
  EXPECT_EQ(current_manifest(), current_manifest());
}

}  // namespace
