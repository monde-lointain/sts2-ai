# vulture whitelist — symbols vulture flags as unused but ARE referenced
# (via reflection, decorators, framework callbacks, etc).
# Populate after first `make py-dead-code` run. Entries here suppress
# false positives; genuine dead code should be deleted, not whitelisted.
#
# Format: any reference (attribute access, name binding) tells vulture
# the symbol is "used". Group entries by package for maintainability.
