#!/usr/bin/env python3
"""Derives the set of characters that can reach a player's screen, and pins it.

WHY THIS EXISTS
---------------
`LiberationSans SDF.asset` shipped as TextMesh Pro's stock static atlas: 250
glyphs over `32 - 126, 160 - 255, 8192 - 8303, 8364, 8482, 9633`. That range is
ASCII plus Latin-1, and the six Turkish letters this interface is written in --
Ğ ğ İ ı Ş ş -- live in Latin Extended-A, outside it. So every Turkish label in
the game missed the primary atlas and resolved through
`LiberationSans SDF - Fallback.asset`, which is a DYNAMIC atlas. Two failures
came out of that one gap:

  1. TMP_SubMeshUI.cs:216 spawns a `HideFlags.DontSave` sub-object the first
     time a label falls through to a fallback font. A 1000-cycle session soak
     watched `sessionui` object counts climb because of it.
  2. A dynamic atlas bakes each newly-rendered glyph back into the .asset file
     on disk, so `VisualAssetIntegrityGateTests` found the fallback dirty after
     any editor session that rendered the game.

Both stop if the primary static atlas simply contains the characters. This tool
decides what "the characters" means, deterministically and from the source,
rather than from anyone's memory of which symbols the UI uses.

WHAT IT DOES
------------
Scans string and char literals out of the shipping runtime assembly, applies the
same Turkish case mapping the UI applies at runtime, unions in a baseline that
covers runtime-composed text no literal can show, checks every result against
the source face's own cmap, and writes a sorted inventory file.

The output is an input to `Nebula/Release/Rebuild player font atlas`
(Assets/Editor/Nebula/NebulaFontAtlasHygiene.cs), which bakes it through TMP's
real font pipeline. Same font file plus same inventory yields the same atlas, so
a clean checkout reproduces the committed asset.

Run:  python3 Tools/Fonts/nebula-font-inventory.py
Out:  Tools/Fonts/player-character-inventory.txt
      exit 0 when the committed inventory already matches, 1 when it changed
      (so a release script can tell that someone added a character and did not
      rebuild the atlas)
"""

import os
import struct
import sys
import unicodedata

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# The shipping runtime assembly. Assets/Editor and Assets/Tests are deliberately
# absent: Unity strips both from every player build, so a character that only
# appears in an editor tool's log line can never reach a player and has no claim
# on atlas space.
SCAN_ROOTS = ["Assets/Scripts"]

# Player-facing data that is not code. universe-snapshot.json is loaded from
# Resources at runtime and its system/planet names are drawn on the strategic map.
SCAN_DATA_FILES = ["Assets/Resources/StrategicMap/universe-snapshot.json"]

SOURCE_FACE = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf"
OUTPUT = "Tools/Fonts/player-character-inventory.txt"


# ---------------------------------------------------------------------------
# C# literal extraction
# ---------------------------------------------------------------------------

def extract_literals(source):
    """Yields the text of every string/char literal in a C# file.

    Hand-written rather than regex-based because comments are the whole problem:
    this codebase's comments explain their reasoning in Turkish-adjacent prose
    and use em-dashes, section marks and arrows heavily. A regex sweep over raw
    file bytes pulled in U+00A7 (809 occurrences), U+2014 (2276) and a dozen box
    and geometric-shape characters that exist only in `<summary>` blocks. None of
    those are drawn on screen, and baking them would grow the atlas for nothing.
    Tracking string state properly is the only way to tell the two apart.
    """
    out = []
    i, n = 0, len(source)
    while i < n:
        c = source[i]

        # comments
        if c == '/' and i + 1 < n:
            if source[i + 1] == '/':
                i = source.find('\n', i)
                if i < 0:
                    break
                continue
            if source[i + 1] == '*':
                end = source.find('*/', i + 2)
                i = n if end < 0 else end + 2
                continue

        # char literal
        if c == "'":
            j, buf = i + 1, []
            while j < n and source[j] != "'":
                if source[j] == '\\' and j + 1 < n:
                    buf.append(_unescape(source, j))
                    j += 2
                    continue
                buf.append(source[j])
                j += 1
            out.append(''.join(x for x in buf if x))
            i = j + 1
            continue

        # verbatim string: @"..." or $@"..." or @$"..."
        if c in '@$':
            k = i
            prefixes = set()
            while k < n and source[k] in '@$':
                prefixes.add(source[k])
                k += 1
            if k < n and source[k] == '"' and '@' in prefixes:
                j, buf = k + 1, []
                while j < n:
                    if source[j] == '"':
                        if j + 1 < n and source[j + 1] == '"':   # "" is an escaped quote
                            buf.append('"')
                            j += 2
                            continue
                        break
                    buf.append(source[j])
                    j += 1
                out.append(''.join(buf))
                i = j + 1
                continue
            if k < n and source[k] == '"':
                i = k
                c = '"'
            # else: fall through, a bare @ or $ is not our business

        # regular (possibly interpolated) string
        if c == '"':
            j, buf = i + 1, []
            while j < n and source[j] != '"':
                if source[j] == '\\' and j + 1 < n:
                    buf.append(_unescape(source, j))
                    j += 2
                    continue
                if source[j] == '\n':          # unterminated; bail rather than run away
                    break
                buf.append(source[j])
                j += 1
            out.append(''.join(x for x in buf if x))
            i = j + 1
            continue

        i += 1
    return out


_SIMPLE_ESCAPES = {'n': '\n', 'r': '\r', 't': '\t', '0': '\0', 'a': '\a',
                   'b': '\b', 'f': '\f', 'v': '\v', '\\': '\\', "'": "'",
                   '"': '"'}


def _unescape(source, at):
    """Resolves the escape starting at source[at] == '\\'. Returns '' if opaque.

    \\u escapes matter: a source file may spell a character either literally or
    as \\u0130, and both reach the same glyph on screen.
    """
    kind = source[at + 1]
    if kind == 'u' and at + 6 <= len(source):
        try:
            return chr(int(source[at + 2:at + 6], 16))
        except ValueError:
            return ''
    if kind == 'U' and at + 10 <= len(source):
        try:
            return chr(int(source[at + 2:at + 10], 16))
        except ValueError:
            return ''
    return _SIMPLE_ESCAPES.get(kind, '')


# ---------------------------------------------------------------------------
# Turkish case closure
# ---------------------------------------------------------------------------

# NebulaText.Upper()/Lower() run every heading, label, badge and cost string
# through the tr-TR culture, so a literal understates what is drawn. "Metal
# Alaşım" is authored in mixed case and rendered as "METAL ALAŞIM" -- the Ş is
# on screen without appearing anywhere in the source. Closing over both cases is
# not defensive padding; it is the difference between a covered atlas and a
# missing-glyph box on a heading.
#
# Spelled out rather than delegated to str.upper()/str.lower(), because Python's
# mapping is the invariant one and gets exactly the two letters wrong that
# NebulaText exists to fix: 'i'.upper() is 'I' invariantly and 'İ' in Turkish.
TR_UPPER = {'i': 'İ', 'ı': 'I'}
TR_LOWER = {'I': 'ı', 'İ': 'i'}


def case_closure(chars):
    closed = set(chars)
    for ch in chars:
        closed.add(TR_UPPER.get(ch, ch.upper()) if len(ch.upper()) == 1 else ch)
        closed.add(TR_LOWER.get(ch, ch.lower()) if len(ch.lower()) == 1 else ch)
    return closed


# ---------------------------------------------------------------------------
# Baseline
# ---------------------------------------------------------------------------

def baseline():
    """Characters that reach the screen without ever appearing as a literal.

    Interpolation holes are the reason. `$"{count} adet"` contributes digits the
    scan cannot see; `value.ToString("N0", tr-TR)` contributes the Turkish group
    and decimal separators; DateTime formatting against tr-TR contributes month
    and day names, whose letters are all in the Turkish alphabet. Rather than
    guess which of those a given build hits, the whole printable ASCII range and
    the whole Turkish alphabet are pinned unconditionally. It is ~90 glyphs in a
    1024x1024 atlas, and it removes an entire category of "only fails on the one
    screen nobody screenshotted".
    """
    chars = {chr(c) for c in range(0x20, 0x7F)}
    chars |= set("ÂâÇçĞğİıÖöŞşÜü")          # Turkish alphabet + the â/Â of "kâr", "hâlâ"
    chars |= set(".,")                        # tr-TR group and decimal separators
    return chars


def retained_legacy_range():
    """The stock TMP range the shipped atlas already carried.

    `LiberationSans SDF.asset`'s own m_CreationSettings records
    `32 - 126, 160 - 255, 8192 - 8303, 8364, 8482, 9633`, and 250 glyphs came out
    of it. Rebuilding strictly from the derived set would be a NARROWING: Latin-1
    letters, the degree sign, the superscripts and the general-punctuation block
    all vanish from the atlas, and any string that reaches one of them regresses
    to the fallback -- reintroducing exactly the sub-mesh growth and dirty-atlas
    drift this whole change exists to stop, on some screen nobody looked at.

    A rebuild may add coverage. It may not remove it. The class of text the scan
    structurally cannot see is real -- DateTime and TimeSpan formatted against
    tr-TR, Unity's own strings, anything composed from an interpolation hole --
    and ~130 extra glyphs in an atlas that has room is a cheap way to not care.
    """
    codes = set(range(32, 127)) | set(range(160, 256)) | set(range(8192, 8304))
    codes |= {8364, 8482, 9633}     # EURO SIGN, TRADE MARK SIGN, WHITE SQUARE
    return {chr(c) for c in codes}


# ---------------------------------------------------------------------------
# Source face coverage (TrueType cmap)
# ---------------------------------------------------------------------------

def face_coverage(path):
    """Codepoints LiberationSans.ttf can actually render.

    Parsed here rather than discovered inside Unity so that a character the face
    lacks is a checked-in fact with a name next to it, instead of a warning in
    somebody's console during a rebuild. The atlas request must never contain a
    codepoint the face cannot supply: TMP would report it missing and the
    rebuild's "no missing glyphs" claim would be untrue by construction.
    """
    with open(path, 'rb') as handle:
        data = handle.read()

    tag, num_tables = struct.unpack('>IH', data[0:6])
    if tag == 0x74746366:                                  # 'ttcf'
        offset = struct.unpack('>I', data[12:16])[0]
        num_tables = struct.unpack('>H', data[offset + 4:offset + 6])[0]
        base = offset + 12
    else:
        base = 12

    cmap_off = None
    for index in range(num_tables):
        entry = base + index * 16
        if data[entry:entry + 4] == b'cmap':
            cmap_off = struct.unpack('>I', data[entry + 8:entry + 12])[0]
            break
    if cmap_off is None:
        raise SystemExit(f"{path}: no cmap table")

    subtable_count = struct.unpack('>H', data[cmap_off + 2:cmap_off + 4])[0]
    best = None
    for index in range(subtable_count):
        rec = cmap_off + 4 + index * 8
        platform, encoding, sub = struct.unpack('>HHI', data[rec:rec + 8])
        rank = {(3, 10): 4, (0, 4): 4, (0, 6): 4,
                (3, 1): 3, (0, 3): 3, (0, 2): 2, (0, 1): 2, (0, 0): 2}.get((platform, encoding), 1)
        if best is None or rank > best[0]:
            best = (rank, cmap_off + sub)

    return _read_cmap_subtable(data, best[1])


def _read_cmap_subtable(data, off):
    fmt = struct.unpack('>H', data[off:off + 2])[0]
    covered = set()

    if fmt == 0:
        for code in range(256):
            if data[off + 6 + code]:
                covered.add(code)

    elif fmt == 4:
        seg_x2 = struct.unpack('>H', data[off + 6:off + 8])[0]
        segs = seg_x2 // 2
        ends = struct.unpack(f'>{segs}H', data[off + 14:off + 14 + seg_x2])
        starts_at = off + 14 + seg_x2 + 2
        starts = struct.unpack(f'>{segs}H', data[starts_at:starts_at + seg_x2])
        deltas_at = starts_at + seg_x2
        deltas = struct.unpack(f'>{segs}h', data[deltas_at:deltas_at + seg_x2])
        ranges_at = deltas_at + seg_x2
        ranges = struct.unpack(f'>{segs}H', data[ranges_at:ranges_at + seg_x2])
        for seg in range(segs):
            if starts[seg] > ends[seg]:
                continue
            for code in range(starts[seg], min(ends[seg], 0xFFFE) + 1):
                if ranges[seg] == 0:
                    glyph = (code + deltas[seg]) & 0xFFFF
                else:
                    at = ranges_at + seg * 2 + ranges[seg] + (code - starts[seg]) * 2
                    if at + 2 > len(data):
                        continue
                    glyph = struct.unpack('>H', data[at:at + 2])[0]
                    if glyph:
                        glyph = (glyph + deltas[seg]) & 0xFFFF
                if glyph:
                    covered.add(code)

    elif fmt == 6:
        first, count = struct.unpack('>HH', data[off + 6:off + 10])
        glyphs = struct.unpack(f'>{count}H', data[off + 10:off + 10 + count * 2])
        for index, glyph in enumerate(glyphs):
            if glyph:
                covered.add(first + index)

    elif fmt == 12:
        groups = struct.unpack('>I', data[off + 12:off + 16])[0]
        for index in range(groups):
            at = off + 16 + index * 12
            start, end, _ = struct.unpack('>III', data[at:at + 12])
            covered.update(range(start, min(end, start + 0x10000) + 1))

    else:
        raise SystemExit(f"cmap subtable format {fmt} not handled")

    return covered


# ---------------------------------------------------------------------------

def main():
    seen = {}                                   # char -> first source path

    def note(ch, origin):
        if ch and ch not in seen:
            seen[ch] = origin

    for root in SCAN_ROOTS:
        absolute = os.path.join(ROOT, root)
        for base, dirs, files in os.walk(absolute):
            dirs.sort()
            for name in sorted(files):
                if not name.endswith('.cs'):
                    continue
                path = os.path.join(base, name)
                rel = os.path.relpath(path, ROOT).replace(os.sep, '/')
                with open(path, encoding='utf-8') as handle:
                    text = handle.read()
                for literal in extract_literals(text):
                    for ch in literal:
                        note(ch, rel)

    for rel in SCAN_DATA_FILES:
        path = os.path.join(ROOT, rel)
        if not os.path.exists(path):
            continue
        with open(path, encoding='utf-8') as handle:
            for ch in handle.read():
                note(ch, rel)

    for ch in baseline():
        note(ch, '(baseline)')

    # Closure before the legacy range, so the closure describes what the game
    # actually renders rather than the case-partners of 130 characters nobody uses.
    for ch in case_closure(list(seen)):
        note(ch, '(tr-TR case closure)')

    for ch in retained_legacy_range():
        note(ch, '(retained legacy range)')

    # Control characters and the separators TMP handles structurally rather than
    # as glyphs. U+FFFF appears in source as a sentinel, never as text.
    drop = {c for c in seen if unicodedata.category(c) in ('Cc', 'Cf', 'Cs', 'Cn', 'Zl', 'Zp')}
    drop |= {' '}          # NBSP: TMP substitutes the space glyph's advance
    for ch in drop:
        seen.pop(ch, None)

    covered = face_coverage(os.path.join(ROOT, SOURCE_FACE))
    wanted = sorted(seen, key=ord)
    bakeable = [c for c in wanted if ord(c) in covered]

    # Split the absences by who asked for them, because they mean opposite things.
    # A retained-range codepoint the face lacks is unremarkable -- the original
    # bake requested 306 and produced 250 for exactly this reason -- and listing
    # 44 of them buries the ones that matter. A PLAYER-origin absence is a
    # character somebody typed into a label that no font in this repo can draw.
    unavailable = [c for c in wanted
                   if ord(c) not in covered and seen[c] != '(retained legacy range)']
    unavailable_retained = sum(1 for c in wanted
                               if ord(c) not in covered and seen[c] == '(retained legacy range)')

    player_count = sum(1 for c in bakeable if seen[c] != '(retained legacy range)')

    lines = [
        "# Nebula player character inventory -- GENERATED, do not hand-edit.",
        "# python3 Tools/Fonts/nebula-font-inventory.py",
        "#",
        "# Every character that can be drawn on a player's screen, derived from the",
        "# string literals of Assets/Scripts, closed over the tr-TR case mapping that",
        "# NebulaText applies at runtime, unioned with a printable-ASCII plus",
        "# Turkish-alphabet baseline for text composed from interpolation holes, and",
        "# unioned again with the stock range the shipped atlas already carried so a",
        "# rebuild can only widen coverage, never narrow it.",
        "#",
        "# Consumed by Nebula/Release/Rebuild player font atlas, which bakes exactly",
        "# these codepoints into LiberationSans SDF.asset as a STATIC atlas. The",
        "# shipping font architecture depends on this file being complete: anything",
        "# missing here is missing from the static atlas, falls through to the dynamic",
        "# fallback, and brings back both the TMP_SubMeshUI object growth and the",
        "# dirty-atlas gate failure that this file exists to close.",
        "#",
        "# Column 4 is provenance. PLAYER means the character was observed in player",
        "# -facing text; RETAINED means it is carried only because the pre-existing",
        "# atlas had it. Never a reason to drop a RETAINED row -- see",
        "# retained_legacy_range() for why narrowing is the dangerous direction.",
        "#",
        f"# source face : {SOURCE_FACE}",
        f"# bakeable    : {len(bakeable)}  ({player_count} player, "
        f"{len(bakeable) - player_count} retained)",
        f"# unavailable : {len(unavailable)} player-facing, "
        f"{unavailable_retained} retained-range (expected, not a defect)",
        "#",
        "# COUNT " + str(len(bakeable)),
    ]

    if unavailable:
        lines.append("#")
        lines.append("# PLAYER-FACING CHARACTERS THE SOURCE FACE CANNOT DRAW.")
        lines.append("# Not requested, not baked, and not fixable here: LiberationSans.ttf is the")
        lines.append("# only face in this repository, and it is what the fallback loads too, so")
        lines.append("# there is nowhere for these to come from. Each one renders as a box today.")
        lines.append("# The fix is in the source string, and the project has already made it once")
        lines.append("# -- NebulaHudPlanetContext dropped its own \"checkmark or cross\" pair for")
        lines.append("# words on the grounds that they are \"font-dependent characters rather than")
        lines.append("# the project's own iconography\".")
        for ch in unavailable:
            lines.append(f"# UNAVAILABLE U+{ord(ch):04X}  {unicodedata.name(ch, '?')}  ({seen[ch]})")

    lines.append("")
    for ch in bakeable:
        name = unicodedata.name(ch, 'UNNAMED')
        origin = 'RETAINED' if seen[ch] == '(retained legacy range)' else 'PLAYER'
        lines.append(f"U+{ord(ch):04X}\t{ch}\t{name}\t{origin}")

    payload = "\n".join(lines) + "\n"
    out_path = os.path.join(ROOT, OUTPUT)
    previous = None
    if os.path.exists(out_path):
        with open(out_path, encoding='utf-8') as handle:
            previous = handle.read()

    with open(out_path, 'w', encoding='utf-8', newline='\n') as handle:
        handle.write(payload)

    print(f"{OUTPUT}: {len(bakeable)} bakeable "
          f"({player_count} player, {len(bakeable) - player_count} retained), "
          f"{len(unavailable)} player-facing character(s) the face cannot draw")
    for ch in unavailable:
        print(f"  UNAVAILABLE U+{ord(ch):04X} {unicodedata.name(ch, '?')} first seen in {seen[ch]}")

    if previous is None:
        print("inventory created")
        return 1
    if previous != payload:
        print("inventory CHANGED -- rerun Nebula/Release/Rebuild player font atlas")
        return 1
    print("inventory unchanged")
    return 0


if __name__ == '__main__':
    sys.exit(main())
