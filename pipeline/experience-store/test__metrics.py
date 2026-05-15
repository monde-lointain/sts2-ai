"""Tests for the shared PrometheusLineBuilder.

Covers:
1. Empty builder -> [].
2. Counter, no labels.
3. Counter with labels (insertion order; service last).
4. Gauge with float_format=".6f".
5. Gauge with no float_format renders as integer.
6. Multiple calls accumulate in emission order.
7. Service name with `"` passes through literally (no escaping; documented).
"""
from __future__ import annotations

from _metrics import PrometheusLineBuilder


def test_empty_builder_returns_empty_list() -> None:
    b = PrometheusLineBuilder("svc")
    assert b.lines() == []


def test_counter_no_labels_emits_service_only() -> None:
    b = PrometheusLineBuilder("svc")
    b.counter("foo_total", value=5)
    assert b.lines() == [b'foo_total{service="svc"} 5']


def test_counter_labels_insertion_order_service_last() -> None:
    b = PrometheusLineBuilder("svc")
    b.counter("foo", labels={"a": "1", "b": "2"}, value=3)
    assert b.lines() == [b'foo{a="1",b="2",service="svc"} 3']


def test_counter_labels_preserve_non_alphabetical_insertion_order() -> None:
    # b before a in insertion order — must not be sorted.
    b = PrometheusLineBuilder("svc")
    b.counter("foo", labels={"b": "2", "a": "1"}, value=7)
    assert b.lines() == [b'foo{b="2",a="1",service="svc"} 7']


def test_gauge_float_format_six_places() -> None:
    b = PrometheusLineBuilder("svc")
    b.gauge("foo", value=1.23456, float_format=".6f")
    assert b.lines() == [b'foo{service="svc"} 1.234560']


def test_gauge_no_float_format_renders_int_from_int() -> None:
    b = PrometheusLineBuilder("svc")
    b.gauge("foo", value=5)
    assert b.lines() == [b'foo{service="svc"} 5']


def test_gauge_no_float_format_renders_int_from_float() -> None:
    b = PrometheusLineBuilder("svc")
    b.gauge("foo", value=5.0)
    assert b.lines() == [b'foo{service="svc"} 5']


def test_multiple_calls_accumulate_in_emission_order() -> None:
    b = PrometheusLineBuilder("svc")
    b.counter("a_total", value=1)
    b.gauge("b", value=2)
    b.counter("c_total", labels={"x": "y"}, value=3)
    b.gauge("d", value=0.5, float_format=".6f")
    assert b.lines() == [
        b'a_total{service="svc"} 1',
        b'b{service="svc"} 2',
        b'c_total{x="y",service="svc"} 3',
        b'd{service="svc"} 0.500000',
    ]


def test_service_name_with_quote_passes_through_literally() -> None:
    # No escaping is performed; the literal `"` ends up inside the label
    # value. This is documented behavior — callers are responsible for
    # passing well-formed service names.
    b = PrometheusLineBuilder('sv"c')
    b.counter("foo_total", value=1)
    assert b.lines() == [b'foo_total{service="sv"c"} 1']


def test_counter_default_value_is_zero() -> None:
    b = PrometheusLineBuilder("svc")
    b.counter("foo_total")
    assert b.lines() == [b'foo_total{service="svc"} 0']


def test_gauge_default_value_is_zero() -> None:
    b = PrometheusLineBuilder("svc")
    b.gauge("foo")
    assert b.lines() == [b'foo{service="svc"} 0']


def test_empty_labels_dict_treated_as_no_labels() -> None:
    b = PrometheusLineBuilder("svc")
    b.counter("foo_total", labels={}, value=1)
    assert b.lines() == [b'foo_total{service="svc"} 1']
