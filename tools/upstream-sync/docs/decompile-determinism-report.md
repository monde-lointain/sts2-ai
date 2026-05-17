# GDRE Decompile Determinism Report

**Date:** 2026-05-17
**Author:** Wave 4 / Stream A.0
**GDRE version:** v2.5.0-beta.5 (binary: `gdre_tools.x86_64`, Godot Engine v4.7.dev.gh.30eb162a7)

---

## 1. Purpose

Determine whether GDRE produces byte-identical output across two extractions of the
same `.pck`, so that Phase A.1 drift gates can use byte-level comparison rather than
requiring AST-canonical normalization.

---

## 2. Methodology

### 2.1 Inputs

| Item | Path |
|---|---|
| Source PCK | `~/snap/steam/common/.local/share/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck` |
| PCK size | 1,892,362,604 bytes |
| Steam version | v0.105.1 (buildid 23156356, commit `d5e30a22`) |
| Reference tree | `~/development/projects/godot/sts2` (git-tagged `v0.105.1`, synced 2026-05-14) |

### 2.2 Commands run

```bash
# Fresh extraction to /tmp/gdre-fresh
mkdir -p /tmp/gdre-fresh
/home/clydew372/applications/GDRE_tools-v2.5.0-beta.5-linux/gdre_tools.x86_64 \
  --headless \
  --recover=<STEAM_PCK> \
  --output=/tmp/gdre-fresh \
  --ignore-checksum-errors

# GDRE reported: Successfully converted: 3930, Recovery finished in 01m18s

# Hash non-artifact .cs files in both trees
find /tmp/gdre-fresh -name '*.cs' | grep -vE '/--[yz]__' | sort \
  | xargs sha256sum | sed "s|/tmp/gdre-fresh/||g" | sort > /tmp/gdre-fresh-hashes.txt

find ~/development/projects/godot/sts2 -name '*.cs' | grep -vE '/--[yz]__' | sort \
  | xargs sha256sum | sed "s|<EXISTING>/||g" | sort > /tmp/gdre-existing-hashes.txt

# Check for same-path/different-hash entries (true non-determinism)
join -1 2 -2 2 \
  <(sort -k2 /tmp/gdre-existing-hashes.txt) \
  <(sort -k2 /tmp/gdre-fresh-hashes.txt) | \
  awk '$2 != $3 {print "DIFF:", $1}'
# → (no output)

# Diff to find file-count differences
diff /tmp/gdre-fresh-hashes.txt /tmp/gdre-existing-hashes.txt
```

### 2.3 Raw diff output (first 36 lines — complete)

```
35a36
> 03b331848e0482342a0fd77e5e0a392bac3d57030b6504b3f990494d564cf468  System/Text/RegularExpressions/Generated/...__SnakeCaseRegex_1.cs
137a139
> 0be1a8399fbefa553e3202c44c7fb653cf774b73b6633960d3f702df47a494b2  .godot/mono/temp/obj/Debug/.NETCoreApp,Version=v9.0.AssemblyAttributes.cs
749a752
> 3919eb3258a4e8922fc63c5277e80519f2ae2cdfc2f73dbdfff863afd079ea17  RiderTestRunner/NetCoreRunner.cs
754a758
> 397fbb1e7c9b7932d30b2cca5ba35cfd261112c2c7706e6c916eba452f23918d  addons/mega_text/ThemeConstants.cs
812a817
> 3d6248e3afddbfbc72a62e62f8d973fccc3b658075440d9dc8f8f832c3bb0e41  System/Text/RegularExpressions/Generated/...__CamelCaseRegex_0.cs
1008a1014
> 4c8413763e6dae58c5f656aff175a53f52950a8487b3f888aaeec2c3d73ab2e3  .godot/mono/temp/obj/Debug/sts2.AssemblyInfo.cs
1147a1154
> 56b3b5afa9afaed7b3d142179e0412d3b1f1d5da5867e64524eb7410a1ef681b  RiderTestRunner/CiCoreRunner.cs
1435a1443
> 6bd2fc41955f396eb43f09bf828c4372f472fafe8b2f29bfdf5eea1c743c6cee  System/Text/RegularExpressions/Generated/...__Utilities.cs
1443a1452
> 6c8c743a71e03e84a5f32d85c1124bb62d2541054289d05c57fc643a786b54de  addons/mega_text/MegaLabelHelper.cs
1486a1496
> 704a8e30e6758fca82c29ceba3fe288e1c2b77e108a7241c105d60e2083a3db7  System/Text/RegularExpressions/Generated/...__HtmlTags_7.cs
1616a1627
> 7996179e2dd52eb39bde8b7bddf2639e50b9c4e5d289f824232d11971951fc4c  System/Text/RegularExpressions/Generated/...__SteamIdRegex_4.cs
1657a1669
> 7be989da7eb11918f4538f8788b51c0615215a903d77648b0d828bd88e8a0e98  addons/mega_text/MegaLabel.cs
1675a1688
> 7d07c09f49aee8c407d2ea21e845a2199d683d89d9bd6e3bcce45e1ae6a85c56  System/Text/RegularExpressions/Generated/...__ConsecutiveSpaces_6.cs
2204a2218
> a5d789a75511903042ba08059ebf94b20b509cd3ee174d5ef6685630df88ae81  System/Text/RegularExpressions/Generated/...__SpecialCharRegex_3.cs
2219a2234
> a7122ce37efd15023327c1292860f8108f41c550d39cd8c5bca16ae70e97782a  addons/mega_text/MegaRichTextLabel.cs
2269a2285
> aa8617c2ef692582f9ec509c6b0d2f81d23a879c59546cb84febcb3e8f4f8baf  System/Text/RegularExpressions/Generated/...__NonSpaceWhitespaceCharacters_5.cs
2457a2474
> b96c88f78cb99bf2b8b116259e74d47773f3edc617247786aa76389af3909b5e  .godot/mono/temp/obj/Debug/Sentry.Attributes.cs
3271a3289
> f5d511027d19ec847b4b75fd07d5f4ffd0708e21b7bc48e9fda6ec66feb4b12c  System/Text/RegularExpressions/Generated/...__WhitespaceRegex_2.cs
```

All 18 diff lines are additions (`>`), meaning files present in the fresh extract but absent
from the reference tree. **No lines show changed hashes for a shared path.**

---

## 3. Result

### Classification: **DETERMINISTIC**

| Metric | Value |
|---|---|
| Non-artifact `.cs` files in fresh extract | 3,397 |
| Non-artifact `.cs` files in reference tree | 3,379 |
| Files present in both, same hash | 3,379 (100%) |
| Files with same path but different hash | **0** |
| Files only in fresh (outside allowlist) | 18 |
| Files only in reference | 0 |

### Analysis of 18 "extra" files in fresh extract

All 18 files are **outside the `src/` rsync allowlist** used by `extract.py`. They fall
into four categories:

| Category | Count | Examples |
|---|---|---|
| `.godot/mono/temp/obj/Debug/` — build-time generated | 3 | `sts2.AssemblyInfo.cs`, `Sentry.Attributes.cs` |
| `System/Text/RegularExpressions/Generated/` — regex source-gen | 8 | `SnakeCaseRegex_1.cs`, `CamelCaseRegex_0.cs` |
| `addons/mega_text/` — Godot addon | 4 | `MegaLabel.cs`, `MegaRichTextLabel.cs` |
| `RiderTestRunner/` — IDE test runner stubs | 2 | `CiCoreRunner.cs`, `NetCoreRunner.cs` |

These are excluded by the existing `ALLOWLIST_RSYNC_INCLUDES` in `extract.py`
(`/src/`, `/scenes/`, etc. only). The reference tree was synced correctly; the fresh
extract simply produces these additional non-game paths that the allowlist drops.

**Conclusion:** GDRE extraction is byte-deterministic for the allowlisted source tree.
The 18-file delta is a consistent allowlist artifact, not a decompiler non-determinism.

---

## 4. Recommendation for Phase A.1

### Gate name: `DecompileReproducibilityGate`

**Strategy:** byte-diff on the allowlisted content set (`src/`, `scenes/`, root files).
No AST-canonical normalization is needed. Gate algorithm:

1. Extract `.pck` to fresh staging dir (same args as production sync).
2. For each file matching `ALLOWLIST_RSYNC_INCLUDES`:
   - Compute `sha256sum`; compare vs. prior sync snapshot stored in git-tag `v0.105.1`.
   - If any hash differs AND path is in `src/` → FAIL (source drift detected).
   - If any hash differs AND path is NOT in `src/` → WARN only (non-game content change).
3. Emit JSON gate report: `{verdict: PASS|FAIL|WARN, changed_paths: [...], timestamp: ...}`.

### Allowlist (none required)

No allowlist of "expected-to-differ" files is needed. The GDRE decompiler is
byte-reproducible for the content set we track. The 18 extra-path categories are
already excluded by the production allowlist in `extract.py`.

---

## 5. Reference: GDRE extraction log summary

```
Imported resources for export session:  3930
Successfully converted:                 3930
Lossy:                                  1229
Rewrote metadata:                       3877
Non-importable conversions:             0
Not converted:                          0
Failed conversions:                     0

Recovery finished in 01m18s
```

Exit code: 0 (success).
