"""Tests for upstream_sync.change_type: heuristic change-type + magnitude classifier."""

from __future__ import annotations

import pytest

from upstream_sync.change_type import classify_change_type

# ---------------------------------------------------------------------------
# change_type classification
# ---------------------------------------------------------------------------


def test_buffed_keyword():
    ct, _mag = classify_change_type(
        "Buffed Untouchable card: upgraded Block gain increased From +2 -> +3"
    )
    assert ct == "buffed"


def test_nerfed_keyword():
    ct, _mag = classify_change_type(
        "Nerfed Blade of Ink card: Inky enchantment damage decreased from +2 -> +1"
    )
    assert ct == "nerfed"


def test_added_keyword():
    ct, _mag = classify_change_type("Added a new card to Silent's starting deck")
    assert ct == "added"


def test_removed_keyword():
    ct, _mag = classify_change_type("Removed Aeonglass from the relic pool")
    assert ct == "removed"


def test_deleted_keyword_maps_to_removed():
    ct, _mag = classify_change_type("Deleted obsolete card variant")
    assert ct == "removed"


def test_fixed_keyword():
    ct, _mag = classify_change_type("Fixed Aeonglass icon/intent display")
    assert ct == "fixed"


def test_reworked_keyword():
    ct, _mag = classify_change_type("Reworked the Nightmare card completely")
    assert ct == "reworked"


def test_redesign_maps_to_reworked():
    ct, _mag = classify_change_type("Redesigned the orb socket UI")
    assert ct == "reworked"


def test_changed_keyword():
    ct, _mag = classify_change_type("Changed Nightmare card: if you play it during combat...")
    assert ct == "changed"


def test_updated_maps_to_changed():
    ct, _mag = classify_change_type("Updated cost of Rampage from 2 to 1")
    assert ct == "changed"


def test_increased_maps_to_buffed():
    ct, _mag = classify_change_type("Increased SilentStrike damage from 6 to 7")
    assert ct == "buffed"


def test_decreased_maps_to_nerfed():
    ct, _mag = classify_change_type("Decreased Ironclad block from 5 to 4")
    assert ct == "nerfed"


def test_improved_maps_to_buffed():
    ct, _mag = classify_change_type("Improved the Headbutt relic effect")
    assert ct == "buffed"


def test_unclassified_fallback():
    ct, _mag = classify_change_type("Untouchable card now works differently in combat")
    assert ct == "unclassified"


def test_empty_string_unclassified():
    ct, mag = classify_change_type("")
    assert ct == "unclassified"
    assert mag is None


def test_whitespace_only_unclassified():
    ct, mag = classify_change_type("   ")
    assert ct == "unclassified"
    assert mag is None


# ---------------------------------------------------------------------------
# magnitude extraction
# ---------------------------------------------------------------------------


def test_magnitude_arrow_notation():
    _ct, mag = classify_change_type(
        "Buffed Untouchable card: upgraded Block gain increased From +2 -> +3"
    )
    assert mag == "+2→+3"


def test_magnitude_nerfed_arrow():
    _ct, mag = classify_change_type(
        "Nerfed Blade of Ink card: Inky enchantment damage decreased from +2 -> +1"
    )
    assert mag == "+2→+1"


def test_magnitude_from_to_notation():
    _ct, mag = classify_change_type("Increased SilentStrike damage from 6 to 7")
    assert mag == "6→7"


def test_magnitude_unicode_arrow():
    _ct, mag = classify_change_type("Buffed Hyperbeam: damage increased 26 → 28")
    assert mag is not None
    assert "26" in mag and "28" in mag


def test_magnitude_none_for_fixed():
    ct, mag = classify_change_type("Fixed Aeonglass icon/intent display")
    assert ct == "fixed"
    assert mag is None


def test_magnitude_none_for_added():
    ct, mag = classify_change_type("Added new card to the pool")
    assert ct == "added"
    assert mag is None


def test_magnitude_none_for_narrative_change():
    ct, mag = classify_change_type(
        "Changed Nightmare card: if you play it during combat, the effect is different now"
    )
    assert ct == "changed"
    assert mag is None


def test_magnitude_bracketed_form():
    """Mega Crit uses '26(34) -> 28(36)' form for upgraded cards."""
    _ct, mag = classify_change_type("Buffed Hyperbeam: damage increased from 26(34) -> 28(36)")
    assert mag is not None
    # Should capture both from and to groups
    assert "26" in mag or "28" in mag


# ---------------------------------------------------------------------------
# Known corpus examples (spot accuracy check)
# ---------------------------------------------------------------------------


@pytest.mark.parametrize(
    "excerpt,expected_ct",
    [
        ("Buffed Untouchable card: upgraded Block gain increased From +2 -> +3", "buffed"),
        ("Nerfed Blade of Ink card: Inky enchantment damage decreased from +2 -> +1", "nerfed"),
        ("Changed Nightmare card: if you play it...", "changed"),
        ("Added Aeonglass relic", "added"),
        ("Fixed Kaiser Crab appearing if you load into an already completed boss room", "fixed"),
        ("Removed the intent flicker on Slime Boss", "removed"),
    ],
)
def test_corpus_change_type(excerpt: str, expected_ct: str):
    ct, _ = classify_change_type(excerpt)
    assert ct == expected_ct


@pytest.mark.parametrize(
    "excerpt,expected_mag_fragment",
    [
        ("Buffed Untouchable card: upgraded Block gain increased From +2 -> +3", "+2"),
        ("Nerfed Blade of Ink card: Inky enchantment damage decreased from +2 -> +1", "+2"),
        ("Increased SilentStrike damage from 6 to 7", "6"),
    ],
)
def test_corpus_magnitude_present(excerpt: str, expected_mag_fragment: str):
    _, mag = classify_change_type(excerpt)
    assert mag is not None
    assert expected_mag_fragment in mag
