"""Shared Prometheus text-v0.0.4 line builder.

Used by experience-store submodules (`schema_registry`, `ingest_api`,
`sampler`, `lifecycle`) that previously hand-rolled identical formatting
inside `metrics_lines(service_name)`. The builder only renders; callers
retain their counter bookkeeping and locking. NOT thread-safe — callers
hold their own locks (matches the existing pattern).

Output line shape (per `pipeline/common/service_host.py:28-34`):

    <name>{<label_k>="<label_v>",...,service="<service>"} <value>

No trailing newline per line — the aggregator joins.

Label rendering relies on `dict` preserving insertion order (CPython 3.7+,
language-guaranteed). The `service` label is auto-appended last on every
emitted line.

No escaping is performed on label values or the service name; callers are
responsible for passing strings that don't contain `"` or backslashes. A
test asserts the literal passthrough behavior for a service name containing
a quote.
"""
from __future__ import annotations


class PrometheusLineBuilder:
    def __init__(self, service_name: str) -> None:
        """Service name is auto-appended as a `service="<name>"` label on
        every emitted line.
        """
        self._service = service_name
        self._lines: list[bytes] = []

    def counter(
        self,
        name: str,
        labels: dict[str, str] | None = None,
        value: int = 0,
    ) -> None:
        """Append one counter line. Labels rendered in dict-insertion order;
        service label always last.
        """
        self._lines.append(self._format(name, labels, str(int(value))))

    def gauge(
        self,
        name: str,
        labels: dict[str, str] | None = None,
        value: int | float = 0,
        *,
        float_format: str | None = None,
    ) -> None:
        """Append one gauge line. If float_format is given (e.g. '.6f'),
        value is rendered as `format(float(value), float_format)`. Otherwise
        int(value).
        """
        if float_format is not None:
            rendered = format(float(value), float_format)
        else:
            rendered = str(int(value))
        self._lines.append(self._format(name, labels, rendered))

    def lines(self) -> list[bytes]:
        """Return accumulated UTF-8 byte lines, in emission order."""
        return self._lines

    def _format(
        self,
        name: str,
        labels: dict[str, str] | None,
        rendered_value: str,
    ) -> bytes:
        parts: list[str] = []
        if labels:
            for key, val in labels.items():
                parts.append(f'{key}="{val}"')
        parts.append(f'service="{self._service}"')
        return f'{name}{{{",".join(parts)}}} {rendered_value}'.encode("utf-8")
