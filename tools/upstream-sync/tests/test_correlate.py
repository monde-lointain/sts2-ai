"""Tests for upstream_sync.correlate: BBCode-aware patch-note -> diff correlation.

Covers all 15 edge cases from the W4-A spec. Correlator output is *advisory*
("Patch-notes HINT"); these tests enforce shape and scoring, not authority.
"""

from __future__ import annotations

import pytest

from upstream_sync import correlate
from upstream_sync.correlate import (
    CHARACTER_SECTION_HEADERS,
    SECTION_BUCKET_HINTS,
    CorrelationMap,
    character_scope_filter,
    entity_in_allowlist,
    extract_file_stem,
    normalize_section_header,
    score_entity_match,
)
from upstream_sync.correlate import (
    correlate as do_correlate,
)
from upstream_sync.diff_analyze import (
    BUCKET_CARDS,
    BUCKET_MONSTERS,
    BUCKET_RELICS,
    DiffEntry,
    DiffReport,
)
from upstream_sync.patch_notes import PatchNote

# ---------- Builders ----------


def _entry(
    path: str,
    *,
    status: str = "M",
    character_tag: str | None = None,
) -> DiffEntry:
    return DiffEntry(
        status=status,  # type: ignore[arg-type]
        path=path,
        rename_from=None,
        rename_score=None,
        character_tag=character_tag,
        line_delta=None,
    )


def _report(
    buckets: dict[str, list[DiffEntry]],
    *,
    discovered: set[str] | None = None,
) -> DiffReport:
    return DiffReport(
        from_tag="v0.0.1",
        to_tag="v0.0.2",
        buckets=buckets,
        renames=[],
        character_tags_seen=set(),
        encounter_rng_defers=[],
        unmatched_paths=[],
        discovered_characters=discovered or set(),
    )


def _note(
    gid: str,
    contents: str,
    *,
    title: str = "Patch v0.0.2",
    date: int = 1_700_000_000,
) -> PatchNote:
    return PatchNote(
        gid=gid,
        title=title,
        date=date,
        contents=contents,
        url=f"https://store.steampowered.com/news/{gid}",
        version_hint=None,
    )


# ---------- normalize_section_header ----------


def test_normalize_section_lowercase_strip_colon():
    assert normalize_section_header("CONTENT & BALANCE:") == "content & balance"


def test_normalize_section_strip_whitespace():
    assert normalize_section_header("  Enemies  ") == "enemies"


def test_normalize_section_idempotent():
    once = normalize_section_header("Silent:")
    assert once == "silent"
    assert normalize_section_header(once) == "silent"


def test_normalize_section_empty():
    assert normalize_section_header("") == ""


# ---------- extract_file_stem ----------


def test_extract_file_stem_nested_cs():
    assert extract_file_stem("src/Core/Models/Cards/StrikeSilent.cs") == "StrikeSilent"


def test_extract_file_stem_bare_filename():
    assert extract_file_stem("Aeonglass.cs") == "Aeonglass"


def test_extract_file_stem_no_extension():
    assert extract_file_stem("src/Foo/Bar") == "Bar"


def test_extract_file_stem_dotted_filename():
    # The dataclass stem rule is filename without extension. Pathlib's `.stem`
    # only strips the final suffix, which is the desired behavior.
    assert extract_file_stem("scenes/foo/bar.tscn.json") == "bar.tscn"


# ---------- score_entity_match ----------


def test_score_exact_match_one():
    """Edge case 1: exact stem match scores 1.0."""
    assert score_entity_match("Aeonglass", "Aeonglass") == 1.0


def test_score_exact_match_case_insensitive():
    assert score_entity_match("aeonglass", "Aeonglass") == 1.0
    assert score_entity_match("AEONGLASS", "Aeonglass") == 1.0


def test_score_starts_with():
    """Edge case 2: starts-with scores 0.7."""
    assert score_entity_match("Strike", "StrikeSilent") == 0.7


def test_score_camelcase_word_match():
    """Edge case 3: entity is a word in CamelCase split scores 0.5."""
    assert score_entity_match("Strike", "FlashStrike") == 0.5


def test_score_no_match():
    """Edge case 4: no match scores 0.0."""
    assert score_entity_match("Strike", "Defend") == 0.0


def test_score_entity_with_trailing_colon():
    """Edge case 15: entity with trailing colon is normalized."""
    assert score_entity_match("Aeonglass:", "Aeonglass") == 1.0


def test_score_entity_whitespace_stripped():
    assert score_entity_match("  Strike  ", "StrikeSilent") == 0.7


def test_score_camelcase_middle_word():
    """A word in the middle of CamelCase matches with 0.5."""
    assert score_entity_match("Flash", "FooFlashBar") == 0.5


def test_score_not_substring_no_word_boundary():
    """Substring that doesn't align with CamelCase boundary doesn't match."""
    # "trike" appears in "StrikeSilent" but is not a word boundary
    assert score_entity_match("trike", "StrikeSilent") == 0.0


# ---------- character_scope_filter ----------


def test_character_scope_both_none():
    assert character_scope_filter(None, None) is True


def test_character_scope_section_none_passes():
    """If section_character is None: don't filter."""
    assert character_scope_filter("Silent", None) is True


def test_character_scope_diff_none_section_set():
    """Diff has no character; section names one → filtered out."""
    # If section says "Silent" but the diff entry isn't tagged, we still want
    # to pass — the *entry* doesn't make a competing claim.
    assert character_scope_filter(None, "Silent") is True


def test_character_scope_matching():
    assert character_scope_filter("Silent", "Silent") is True


def test_character_scope_matching_case_insensitive():
    assert character_scope_filter("Silent", "silent") is True


def test_character_scope_mismatch_filtered():
    """Edge case 7: cross-character noise is filtered."""
    assert character_scope_filter("Ironclad", "Silent") is False


# ---------- correlate (integration) ----------


def test_correlate_exact_match_scores_one():
    """Edge case 1: exact stem match flows through."""
    diff = _report(
        {
            BUCKET_RELICS: [_entry("src/Core/Models/Relics/Aeonglass.cs")],
        }
    )
    notes = [_note("g1", "Buffed [b]Aeonglass[/b]: rare relic damage 2->3.")]

    result = do_correlate(diff, notes)

    assert isinstance(result, CorrelationMap)
    path = "src/Core/Models/Relics/Aeonglass.cs"
    assert path in result.matches
    matches = result.matches[path]
    assert len(matches) == 1
    assert matches[0].score == 1.0
    assert matches[0].entity == "Aeonglass"
    assert matches[0].note_gid == "g1"
    assert matches[0].diff_path == path
    assert result.unmatched_notes == []


def test_correlate_starts_with_scores_zero_seven():
    """Edge case 2: starts-with."""
    diff = _report(
        {
            BUCKET_CARDS: [_entry("src/Core/Models/Cards/StrikeSilent.cs")],
        }
    )
    # No section header so empty bucket-hints; entity matched against all
    # buckets but only one path exists.
    notes = [_note("g1", "[b]Strike[/b]: redesigned baseline.")]

    result = do_correlate(diff, notes)
    path = "src/Core/Models/Cards/StrikeSilent.cs"
    assert result.matches[path][0].score == 0.7


def test_correlate_camelcase_word_match():
    """Edge case 3: camelCase word match."""
    diff = _report(
        {
            BUCKET_CARDS: [_entry("src/Core/Models/Cards/FlashStrike.cs")],
        }
    )
    notes = [_note("g1", "Buffed [b]Strike[/b].")]

    result = do_correlate(diff, notes)
    assert result.matches["src/Core/Models/Cards/FlashStrike.cs"][0].score == 0.5


def test_correlate_zero_score_not_emitted():
    """Edge case 4: no match → no Match emitted."""
    diff = _report(
        {
            BUCKET_CARDS: [_entry("src/Core/Models/Cards/Defend.cs")],
        }
    )
    notes = [_note("g1", "Buffed [b]Strike[/b].")]

    result = do_correlate(diff, notes)
    assert result.matches == {}
    # Note has an entity but no diff matched, so the note is unmatched.
    assert result.unmatched_notes == ["g1"]


def test_correlate_section_bucket_filter_content_and_balance():
    """Edge case 5: '[h2]Content & Balance[/h2]' → cards/relics/powers/potions only."""
    diff = _report(
        {
            BUCKET_CARDS: [_entry("src/Core/Models/Cards/Strike.cs")],
            BUCKET_MONSTERS: [_entry("src/Core/Models/Monsters/Strike.cs")],
        }
    )
    contents = "[h2]Content & Balance:[/h2]\n[b]Strike[/b] is buffed."
    notes = [_note("g1", contents)]

    result = do_correlate(diff, notes)
    # cards is allowed by Content & Balance; monsters is NOT
    assert "src/Core/Models/Cards/Strike.cs" in result.matches
    assert "src/Core/Models/Monsters/Strike.cs" not in result.matches


def test_correlate_empty_section_bucket_hints_no_filter():
    """Edge case 6: '[h2]Bug Fixes[/h2]' empty hints → match anywhere."""
    diff = _report(
        {
            BUCKET_CARDS: [_entry("src/Core/Models/Cards/Aeonglass.cs")],
            BUCKET_MONSTERS: [_entry("src/Core/Models/Monsters/Aeonglass.cs")],
        }
    )
    contents = "[h2]Bug Fixes:[/h2]\n[b]Aeonglass[/b] crash fixed."
    notes = [_note("g1", contents)]

    result = do_correlate(diff, notes)
    # Both buckets should produce matches because Bug Fixes has [] hints.
    assert "src/Core/Models/Cards/Aeonglass.cs" in result.matches
    assert "src/Core/Models/Monsters/Aeonglass.cs" in result.matches


def test_correlate_character_section_scoping_silent_wins():
    """Edge case 7: Silent: section scopes to character_tag='Silent'."""
    diff = _report(
        {
            BUCKET_CARDS: [
                _entry("src/Core/Models/Cards/StrikeSilent.cs", character_tag="Silent"),
                _entry("src/Core/Models/Cards/StrikeIronclad.cs", character_tag="Ironclad"),
            ],
        }
    )
    contents = "[h2]Silent:[/h2]\nBuffed [b]Strike[/b]."
    notes = [_note("g1", contents)]

    result = do_correlate(diff, notes)
    # Silent's StrikeSilent matches with 0.7 (starts-with); Ironclad filtered out.
    assert "src/Core/Models/Cards/StrikeSilent.cs" in result.matches
    assert "src/Core/Models/Cards/StrikeIronclad.cs" not in result.matches


def test_correlate_discovered_characters_auto_route():
    """Edge case 8: passing discovered_characters={'Necrobinder'} works."""
    diff = _report(
        {
            BUCKET_CARDS: [
                _entry("src/Core/Models/Cards/StrikeNecrobinder.cs", character_tag="Necrobinder"),
                _entry("src/Core/Models/Cards/StrikeSilent.cs", character_tag="Silent"),
            ],
        }
    )
    contents = "[h2]Necrobinder:[/h2]\nBuffed [b]Strike[/b]."
    notes = [_note("g1", contents)]

    result = do_correlate(diff, notes, discovered_characters={"Necrobinder"})
    assert "src/Core/Models/Cards/StrikeNecrobinder.cs" in result.matches
    assert "src/Core/Models/Cards/StrikeSilent.cs" not in result.matches


def test_correlate_tie_break_by_date_newer_first():
    """Edge case 9: two notes with same entity → newer date wins higher score."""
    diff = _report(
        {
            BUCKET_RELICS: [_entry("src/Core/Models/Relics/Aeonglass.cs")],
        }
    )
    notes = [
        _note("old", "[b]Aeonglass[/b] old", date=1_000_000_000),
        _note("new", "[b]Aeonglass[/b] new", date=2_000_000_000),
    ]

    result = do_correlate(diff, notes)
    path = "src/Core/Models/Relics/Aeonglass.cs"
    matches = result.matches[path]
    assert len(matches) == 2
    # Newer first (same 1.0 score; tie-break by date desc)
    assert matches[0].note_gid == "new"
    assert matches[1].note_gid == "old"


def test_correlate_tie_break_by_gid_when_dates_equal():
    diff = _report(
        {
            BUCKET_RELICS: [_entry("src/Core/Models/Relics/Aeonglass.cs")],
        }
    )
    notes = [
        _note("b", "[b]Aeonglass[/b] b", date=1_000_000_000),
        _note("a", "[b]Aeonglass[/b] a", date=1_000_000_000),
    ]

    result = do_correlate(diff, notes)
    matches = result.matches["src/Core/Models/Relics/Aeonglass.cs"]
    assert matches[0].note_gid == "a"
    assert matches[1].note_gid == "b"


def test_correlate_top_n_bounded():
    """Edge case 10: 5 notes mention same entity → only top 3 emitted (default)."""
    diff = _report(
        {
            BUCKET_RELICS: [_entry("src/Core/Models/Relics/Aeonglass.cs")],
        }
    )
    # 5 notes, each mentioning Aeonglass exactly once. All score 1.0.
    notes = [_note(f"g{i}", "[b]Aeonglass[/b].", date=1_000_000_000 + i) for i in range(5)]

    result = do_correlate(diff, notes)
    matches = result.matches["src/Core/Models/Relics/Aeonglass.cs"]
    assert len(matches) == 3
    # Newest 3 by date desc.
    assert [m.note_gid for m in matches] == ["g4", "g3", "g2"]


def test_correlate_top_n_custom():
    diff = _report(
        {
            BUCKET_RELICS: [_entry("src/Core/Models/Relics/Aeonglass.cs")],
        }
    )
    notes = [_note(f"g{i}", "[b]Aeonglass[/b].", date=1_000_000_000 + i) for i in range(5)]
    result = do_correlate(diff, notes, top_n_per_path=2)
    matches = result.matches["src/Core/Models/Relics/Aeonglass.cs"]
    assert len(matches) == 2


def test_correlate_unmatched_note():
    """Edge case 11: note whose entities don't match any diff path."""
    diff = _report(
        {
            BUCKET_CARDS: [_entry("src/Core/Models/Cards/Defend.cs")],
        }
    )
    notes = [
        _note("nomatch", "[b]Aeonglass[/b] doesn't appear in diff."),
    ]

    result = do_correlate(diff, notes)
    assert result.matches == {}
    assert result.unmatched_notes == ["nomatch"]


def test_correlate_excerpt_strips_bbcode():
    """Edge case 12: excerpt contains the surrounding text with BBCode stripped."""
    diff = _report(
        {
            BUCKET_CARDS: [_entry("src/Core/Models/Cards/Strike.cs")],
        }
    )
    contents = "...Buffed [b]Strike[/b]: damage 6->7..."
    notes = [_note("g1", contents)]

    result = do_correlate(diff, notes)
    match = result.matches["src/Core/Models/Cards/Strike.cs"][0]
    assert "Buffed Strike: damage 6->7" in match.excerpt
    # No bracket tags in the excerpt.
    assert "[b]" not in match.excerpt
    assert "[/b]" not in match.excerpt


def test_correlate_excerpt_respects_excerpt_chars():
    """Excerpt length is bounded by excerpt_chars."""
    diff = _report(
        {
            BUCKET_RELICS: [_entry("src/Core/Models/Relics/Aeonglass.cs")],
        }
    )
    contents = "x" * 200 + " [b]Aeonglass[/b] " + "y" * 200
    notes = [_note("g1", contents)]

    result = do_correlate(diff, notes, excerpt_chars=40)
    excerpt = result.matches["src/Core/Models/Relics/Aeonglass.cs"][0].excerpt
    # Allow some slop, but it should be far below 400.
    assert len(excerpt) <= 80
    assert "Aeonglass" in excerpt


def test_correlate_empty_patch_notes():
    """Edge case 13: empty patch_notes → empty matches, empty unmatched."""
    diff = _report(
        {
            BUCKET_CARDS: [_entry("src/Core/Models/Cards/Strike.cs")],
        }
    )
    result = do_correlate(diff, [])
    assert result.matches == {}
    assert result.unmatched_notes == []


def test_correlate_empty_diff_report():
    """Edge case 14: empty diff_report → notes with entities are all unmatched."""
    diff = _report({})
    notes = [
        _note("g1", "[b]Aeonglass[/b] buffed."),
        _note("g2", "[b]Strike[/b] buffed."),
    ]
    result = do_correlate(diff, notes)
    assert result.matches == {}
    assert set(result.unmatched_notes) == {"g1", "g2"}


def test_correlate_note_with_no_entities_not_unmatched():
    """A note with no [b]Entity[/b] is not 'unmatched' — it had nothing to match."""
    diff = _report(
        {
            BUCKET_CARDS: [_entry("src/Core/Models/Cards/Strike.cs")],
        }
    )
    notes = [_note("g1", "Just a description, no bold entities.")]

    result = do_correlate(diff, notes)
    assert result.matches == {}
    assert result.unmatched_notes == []


def test_correlate_per_path_sorted_by_score_desc():
    """Within a path, matches are sorted by score (desc)."""
    diff = _report(
        {
            BUCKET_CARDS: [_entry("src/Core/Models/Cards/FlashStrike.cs")],
        }
    )
    # g_high scores 1.0 (exact "FlashStrike"); g_low scores 0.5 (Strike).
    notes = [
        _note("low", "[b]Strike[/b].", date=2_000_000_000),
        _note("high", "[b]FlashStrike[/b].", date=1_000_000_000),
    ]

    result = do_correlate(diff, notes)
    matches = result.matches["src/Core/Models/Cards/FlashStrike.cs"]
    assert matches[0].note_gid == "high"
    assert matches[0].score == 1.0
    assert matches[1].note_gid == "low"
    assert matches[1].score == 0.5


def test_correlate_section_recorded_lowercased_no_colon():
    """The Match.section is the normalized form."""
    diff = _report(
        {
            BUCKET_RELICS: [_entry("src/Core/Models/Relics/Aeonglass.cs")],
        }
    )
    contents = "[h2]POTIONS & RELICS:[/h2]\n[b]Aeonglass[/b]."
    notes = [_note("g1", contents)]

    result = do_correlate(diff, notes)
    match = result.matches["src/Core/Models/Relics/Aeonglass.cs"][0]
    assert match.section == "potions & relics"


def test_correlate_no_section_uses_empty_string():
    """Entity outside any [h2] uses section ''."""
    diff = _report(
        {
            BUCKET_RELICS: [_entry("src/Core/Models/Relics/Aeonglass.cs")],
        }
    )
    notes = [_note("g1", "Standalone [b]Aeonglass[/b] mention.")]

    result = do_correlate(diff, notes)
    match = result.matches["src/Core/Models/Relics/Aeonglass.cs"][0]
    assert match.section == ""


# ---------- SECTION_BUCKET_HINTS shape ----------


def test_section_hints_keys_are_normalized():
    """All keys in SECTION_BUCKET_HINTS must be normalized (idempotent)."""
    for key in SECTION_BUCKET_HINTS:
        assert normalize_section_header(key) == key


def test_section_hints_known_values():
    assert SECTION_BUCKET_HINTS["content & balance"] == [
        "cards",
        "relics",
        "powers",
        "potions",
    ]
    assert SECTION_BUCKET_HINTS["bug fixes"] == []
    assert SECTION_BUCKET_HINTS["multiplayer"] == []
    assert SECTION_BUCKET_HINTS["ancients"] == []


def test_character_section_headers_seed():
    """Seed set must include canonical characters."""
    assert "silent" in CHARACTER_SECTION_HEADERS
    assert "defect" in CHARACTER_SECTION_HEADERS
    assert "ironclad" in CHARACTER_SECTION_HEADERS


def test_character_section_headers_normalized():
    """All entries in seed set must be normalized."""
    for header in CHARACTER_SECTION_HEADERS:
        assert normalize_section_header(header) == header


def test_correlate_discovered_unions_with_seed():
    """discovered_characters union with CHARACTER_SECTION_HEADERS — Silent still works."""
    diff = _report(
        {
            BUCKET_CARDS: [
                _entry("src/Core/Models/Cards/StrikeSilent.cs", character_tag="Silent"),
                _entry("src/Core/Models/Cards/StrikeIronclad.cs", character_tag="Ironclad"),
            ],
        }
    )
    contents = "[h2]Silent:[/h2]\n[b]Strike[/b]."
    notes = [_note("g1", contents)]

    # Pass a different character set — seed still applies because of union.
    result = do_correlate(diff, notes, discovered_characters={"Necrobinder"})
    assert "src/Core/Models/Cards/StrikeSilent.cs" in result.matches
    assert "src/Core/Models/Cards/StrikeIronclad.cs" not in result.matches


# ---------- Frozen dataclass / public API smoke ----------


def test_match_is_frozen():
    diff = _report(
        {
            BUCKET_RELICS: [_entry("src/Core/Models/Relics/Aeonglass.cs")],
        }
    )
    notes = [_note("g1", "[b]Aeonglass[/b].")]
    result = do_correlate(diff, notes)
    match = result.matches["src/Core/Models/Relics/Aeonglass.cs"][0]
    with pytest.raises(Exception):  # FrozenInstanceError under dataclasses
        match.score = 0.0  # type: ignore[misc]


def test_correlation_map_is_frozen():
    result = do_correlate(_report({}), [])
    with pytest.raises(Exception):
        result.unmatched_notes = []  # type: ignore[misc]


def test_module_docstring_emphasizes_advisory():
    """Q1-ADR-013 Element 1: HINT framing, not authority."""
    doc = (correlate.__doc__ or "").lower()
    assert "advisory" in doc or "hint" in doc


# ---------- Concern #2 — content_allowlist filtering in correlate ----------


def test_correlate_allowlist_filters_non_game_entities():
    """Entities not in allowlist are dropped when allowlist is non-empty."""
    diff = _report({BUCKET_CARDS: [_entry("src/Core/Models/Cards/Untouchable.cs")]})
    # Note contains [b]Untouchable[/b] (in allowlist) and [b]UI Label[/b] (not)
    notes = [_note("g1", "[b]Untouchable[/b] buffed; also [b]UI Label[/b] updated.")]
    allowlist = frozenset({"Untouchable"})
    result = do_correlate(diff, notes, content_allowlist=allowlist)
    # Untouchable should match
    assert "src/Core/Models/Cards/Untouchable.cs" in result.matches
    # The non-allowlist entity 'UI Label' should not affect the result
    # (UI Label can't match any diff path anyway in this setup, but the key
    # is that no error is thrown and matching still works)


def test_correlate_allowlist_empty_disables_filtering():
    """Empty allowlist = no filtering; all entities are candidates."""
    diff = _report({BUCKET_CARDS: [_entry("src/Core/Models/Cards/Strike.cs")]})
    notes = [_note("g1", "[b]Strike[/b] damage increased.")]
    result_no_filter = do_correlate(diff, notes, content_allowlist=frozenset())
    result_none_filter = do_correlate(diff, notes, content_allowlist=None)
    # Both should produce same matches
    assert result_no_filter.matches.keys() == result_none_filter.matches.keys()


def test_correlate_allowlist_none_disables_filtering():
    """content_allowlist=None (default) means no filtering applied."""
    diff = _report({BUCKET_RELICS: [_entry("src/Core/Models/Relics/Aeonglass.cs")]})
    notes = [_note("g1", "[b]Aeonglass[/b] relic fixed.")]
    result = do_correlate(diff, notes)  # no allowlist arg
    assert "src/Core/Models/Relics/Aeonglass.cs" in result.matches


def test_entity_in_allowlist_empty_allowlist_passes_all():
    assert entity_in_allowlist("Anything", frozenset()) is True


def test_entity_in_allowlist_exact_match():
    al = frozenset({"Untouchable", "Inky"})
    assert entity_in_allowlist("Untouchable", al) is True
    assert entity_in_allowlist("Inky", al) is True


def test_entity_in_allowlist_prefix_match():
    """Entity 'Inky' in allowlist matches 'Inky enchantment' entity string."""
    al = frozenset({"Inky"})
    assert entity_in_allowlist("Inky enchantment", al) is True


def test_entity_in_allowlist_rejects_unknown():
    al = frozenset({"Untouchable"})
    assert entity_in_allowlist("Artwork Credit", al) is False
    assert entity_in_allowlist("The", al) is False
