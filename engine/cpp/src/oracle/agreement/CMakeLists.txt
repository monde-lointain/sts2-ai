# ------------------------------------------------------------------
# sts2::oracle_agreement — Q2 oracle-agreement Parquet sink (Q2-ADR-004)
# ------------------------------------------------------------------
# Wraps Apache Arrow + Parquet writer/reader behind a 15-column AgreementRow
# POD. Gated under root-level option STS2_BUILD_ORACLE_SINK so the existing
# 474-test battery (adapter / registry / search) builds without Arrow.
add_library(sts2_oracle_agreement STATIC)
add_library(sts2::oracle_agreement ALIAS sts2_oracle_agreement)

target_sources(sts2_oracle_agreement PRIVATE
    sink.cc
)

target_compile_features(sts2_oracle_agreement PUBLIC cxx_std_20)

target_include_directories(sts2_oracle_agreement
    PUBLIC  $<BUILD_INTERFACE:${PROJECT_SOURCE_DIR}/engine/cpp/include>
    PRIVATE ${PROJECT_SOURCE_DIR}/engine/cpp/src
)

# sts2::simulator is the project-wide PUBLIC base — keeps consumers' include
# graph consistent with the sibling oracle libraries (adapter, registry).
# Arrow + Parquet shared imported targets per the root find_package() gated
# under STS2_BUILD_ORACLE_SINK.
target_link_libraries(sts2_oracle_agreement
    PUBLIC
        sts2::simulator
        Arrow::arrow_shared
        Parquet::parquet_shared
)

sts2_apply_warnings(sts2_oracle_agreement)
