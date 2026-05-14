"""Module-local tests for Accept/Reject dataclasses (S0.B.beta)."""

from __future__ import annotations

import pytest

from schema_registry.decision import Accept, Reject


def test_accept_is_frozen_dataclass():
    a = Accept()
    # frozen=True forbids attribute assignment.
    with pytest.raises(Exception):
        a.some_field = 1  # type: ignore[attr-defined]


def test_reject_minimal_construction():
    r = Reject(reason="schema_unknown", http_status=400)
    assert r.reason == "schema_unknown"
    assert r.http_status == 400
    assert r.retry_after_sec is None
    assert r.accepted is None


def test_reject_with_accepted_list():
    r = Reject(
        reason="schema_unknown",
        http_status=400,
        accepted=[(1, 0)],
    )
    assert r.accepted == [(1, 0)]


def test_reject_with_retry_after_sec():
    r = Reject(reason="schema_flip", http_status=503, retry_after_sec=5)
    assert r.retry_after_sec == 5


def test_reject_is_frozen():
    r = Reject(reason="schema_unknown", http_status=400)
    with pytest.raises(Exception):
        r.reason = "other"  # type: ignore[misc]


def test_accept_equality():
    assert Accept() == Accept()


def test_reject_equality():
    r1 = Reject(reason="schema_flip", http_status=503, retry_after_sec=5)
    r2 = Reject(reason="schema_flip", http_status=503, retry_after_sec=5)
    assert r1 == r2
