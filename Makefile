.PHONY: help build test run clean distclean reconfig q1-ci schema-codegen schema-test services-smoke content-registry content-test phase0-gate
.PHONY: format format-patch
.PHONY: cppcheck cppcheck-xml scan-build tidy
.PHONY: complexity complexity-full complexity-xml
.PHONY: coverage
.PHONY: cloc cloc-full cloc-report cloc-report-full
.PHONY: sanitize sanitize-run sanitize-test sanitize-clean

BUILD_DIR := build
SANITIZE_BUILD_DIR := build-sanitize

JOBS ?= $(nproc)

BUILD_TYPE ?= Release
CC ?=
CXX ?=

CMAKE_OPTS :=
ifneq ($(BUILD_TYPE),)
	CMAKE_OPTS += -DCMAKE_BUILD_TYPE=$(BUILD_TYPE)
endif
ifneq ($(CC),)
	CMAKE_OPTS += -DCMAKE_C_COMPILER=$(CC)
endif
ifneq ($(CXX),)
	CMAKE_OPTS += -DCMAKE_CXX_COMPILER=$(CXX)
endif

.DEFAULT_GOAL := help

# Help target
help:
	@echo "sts2-ai - Build Targets"
	@echo ""
	@echo "Variables:"
	@echo "  BUILD_TYPE=<type>   Set build type (default: Release)"
	@echo "  CC=<path>           Set C compiler path"
	@echo "  CXX=<path>          Set C++ compiler path"
	@echo "  JOBS=<n>            Parallel jobs (default: nproc)"
	@echo ""
	@echo "Core:"
	@echo "  help              Show this help message (default)"
	@echo "  build             Build the project"
	@echo "  test              Build and run unit tests"
	@echo "  run               Run executable (use FILE=path to specify input)"
	@echo "  clean             Clean build artifacts"
	@echo "  distclean         Remove build directory"
	@echo "  reconfig          Reconfigure CMake build"
	@echo "  q1-ci             Run sim-headless CI"
	@echo "  schema-codegen    Generate schema bindings"
	@echo "  schema-test       Run schema compatibility tests"
	@echo "  services-smoke    Run service skeleton smoke tests"
	@echo "  content-test      Run content registry tests"
	@echo "  phase0-gate       Run Phase 0 gate checks"
	@echo ""
	@echo "Formatting:"
	@echo "  format            Run clang-format on sources"
	@echo "  format-patch      Generate patch with formatting changes"
	@echo ""
	@echo "Static Analysis:"
	@echo "  cppcheck          Run cppcheck"
	@echo "  cppcheck-xml      Run cppcheck with XML report"
	@echo "  scan-build        Run clang static analyzer"
	@echo "  tidy              Run clang-tidy linter"
	@echo ""
	@echo "Complexity:"
	@echo "  complexity        Run lizard (violations only)"
	@echo "  complexity-full   Run lizard (full report)"
	@echo "  complexity-xml    Run lizard with XML report"
	@echo ""
	@echo "Coverage:"
	@echo "  coverage          Generate code coverage report (Linux/macOS only)"
	@echo ""
	@echo "SLOC:"
	@echo "  cloc              Count lines of code (src only)"
	@echo "  cloc-full         Count lines of code (all files)"
	@echo "  cloc-report       Generate SLOC CSV report (src only)"
	@echo "  cloc-report-full  Generate SLOC CSV report (all files)"
	@echo ""
	@echo "Sanitizers (ASan + UBSan):"
	@echo "  sanitize          Build with sanitizers enabled"
	@echo "  sanitize-run      Build and run with sanitizers (use FILE=path)"
	@echo "  sanitize-test     Build and run tests with sanitizers"
	@echo "  sanitize-clean    Remove sanitizer build directory"

build: $(BUILD_DIR)/Makefile
	@cmake --build $(BUILD_DIR) -j$(JOBS)

test: $(BUILD_DIR)/Makefile
	@cmake --build $(BUILD_DIR) -j$(JOBS)
	@cd $(BUILD_DIR) && ctest --output-on-failure

run: $(BUILD_DIR)/Makefile
	@cmake --build $(BUILD_DIR) -j$(JOBS)
	@$(BUILD_DIR)/$(BUILD_TYPE)/sts2_fight $(FILE)

clean:
	@if [ -d $(BUILD_DIR) ]; then cmake --build $(BUILD_DIR) --target clean; fi

distclean:
	@rm -rf $(BUILD_DIR)

reconfig:
	@rm -rf $(BUILD_DIR)
	@cmake -B $(BUILD_DIR) -S . $(CMAKE_OPTS)

q1-ci:
	@$(MAKE) -C services/sim-headless ci

schema-codegen:
	@.venv/bin/python tools/schema/generate_bindings.py

schema-test: schema-codegen
	@.venv/bin/python -m unittest tests.schema.test_compatibility

services-smoke:
	@.venv/bin/python tests/services/smoke_services.py

content-registry:
	@.venv/bin/python tools/content/seed_phase1_registry.py

content-test: content-registry
	@.venv/bin/python -m unittest tests.content.test_registry

phase0-gate: test q1-ci schema-test services-smoke content-test

# Formatting targets
format: $(BUILD_DIR)/Makefile
	@cmake --build $(BUILD_DIR) --target format

format-patch: $(BUILD_DIR)/Makefile
	@cmake --build $(BUILD_DIR) --target format-patch

# Static analysis targets
cppcheck: $(BUILD_DIR)/Makefile
	@cmake --build $(BUILD_DIR) --target cppcheck

cppcheck-xml: $(BUILD_DIR)/Makefile
	@cmake --build $(BUILD_DIR) --target cppcheck-xml

scan-build: $(BUILD_DIR)/Makefile
	@cmake --build $(BUILD_DIR) --target scan-build

tidy: $(BUILD_DIR)/Makefile
	@cmake --build $(BUILD_DIR) --target tidy

# Complexity targets
complexity: $(BUILD_DIR)/Makefile
	@cmake --build $(BUILD_DIR) --target complexity

complexity-full: $(BUILD_DIR)/Makefile
	@cmake --build $(BUILD_DIR) --target complexity-full

complexity-xml: $(BUILD_DIR)/Makefile
	@cmake --build $(BUILD_DIR) --target complexity-xml

# Coverage target (requires reconfiguration with ENABLE_COVERAGE=ON)
coverage:
	@cmake -B $(BUILD_DIR) -DCMAKE_BUILD_TYPE=Debug -DENABLE_COVERAGE=ON
	@cmake --build $(BUILD_DIR) -j$(JOBS)
	@cmake --build $(BUILD_DIR) --target coverage

# SLOC targets
cloc: $(BUILD_DIR)/Makefile
	@cmake --build $(BUILD_DIR) --target cloc

cloc-full: $(BUILD_DIR)/Makefile
	@cmake --build $(BUILD_DIR) --target cloc-full

cloc-report: $(BUILD_DIR)/Makefile
	@cmake --build $(BUILD_DIR) --target cloc-report

cloc-report-full: $(BUILD_DIR)/Makefile
	@cmake --build $(BUILD_DIR) --target cloc-report-full

# Build directory setup
$(BUILD_DIR)/Makefile:
	@cmake -B $(BUILD_DIR) -S . $(CMAKE_OPTS)

# Sanitizer targets (ASan + UBSan)
sanitize:
	@echo "Configuring sanitizer build (Debug + ASan + UBSan)..."
	@cmake -B $(SANITIZE_BUILD_DIR) -S . \
		-DCMAKE_BUILD_TYPE=Debug \
		-DENABLE_SANITIZERS=ON \
		$(if $(CC),-DCMAKE_C_COMPILER=$(CC),) \
		$(if $(CXX),-DCMAKE_CXX_COMPILER=$(CXX),)
	@cmake --build $(SANITIZE_BUILD_DIR) -j$(JOBS)
	@echo "Sanitizer build complete: $(SANITIZE_BUILD_DIR)/Debug/sts2_fight"

sanitize-run: sanitize
	@echo "Running with sanitizers..."
	@$(SANITIZE_BUILD_DIR)/Debug/sts2_fight $(FILE)

sanitize-test: sanitize
	@echo "Running tests with sanitizers..."
	@cd $(SANITIZE_BUILD_DIR) && ctest --output-on-failure

sanitize-clean:
	@rm -rf $(SANITIZE_BUILD_DIR)
	@echo "Sanitizer build directory removed"
