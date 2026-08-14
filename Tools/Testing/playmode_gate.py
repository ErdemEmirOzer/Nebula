#!/usr/bin/env python3
"""The single authority for running Nebula's PlayMode suite and deciding what the
result means.

    python3 Tools/Testing/playmode_gate.py hash
    python3 Tools/Testing/playmode_gate.py plan      --manifest M --out D --workers 5
    python3 Tools/Testing/playmode_gate.py run       --out D --worker 3
    python3 Tools/Testing/playmode_gate.py resume    --out D --worker 3
    python3 Tools/Testing/playmode_gate.py aggregate --out D

It supersedes runscoped.sh, runbatched.sh, runfull.sh and the Part4 shard_* scripts,
which between them implemented four different and mutually inconsistent answers to
"did this run actually happen". Each rule below is here because its absence produced
a wrong green on this project, and the file names which one.

WHAT WENT WRONG BEFORE, AND WHERE IT IS FIXED HERE

  A full PlayMode run ended without writing its XML. The summary parsed the file
  still sitting at that path from five days earlier and reported total=581
  passed=564 failed=0 for a suite containing none of the fixtures being gated.
  -> Every artefact path carries the run id, so last run's file is not at this
     run's path at all. The path is deleted first anyway, the start epoch is
     recorded, and an XML older than that start is treated as absent. Three
     independent barriers, because this one has already cost a release decision.

  A worker was killed by the watchdog and every fixture result in it was lost,
  twice. Unity writes its result XML only at the END of a run.
  -> The journal. One fsynced JSON line per finished leaf, written by
     NebulaGateCallbacks the moment the leaf finishes. A killed worker still
     proves everything it had already done, and `resume` re-runs only the leaves
     with no line.

  Fixture-level sharding cannot balance: AuditPart2LifecyclePlayModeTests alone
  exceeds a 45-minute watchdog, so no assignment of whole fixtures is even.
  -> Planning is over LEAVES, from the real test list the framework itself
     produced, with longest-processing-time-first over measured durations.

  Discovery was a regex over .cs files, which cannot see [TestCase] or
  [ValueSource] expansions and cannot see an inherited category.
  -> The manifest comes from TestRunnerApi.RetrieveTestList. This script never
     parses C#; it refuses to run without a manifest.

TWO NAMES PER LEAF, AND WHY IT MATTERS

  fullName       unique. TestAdaptor.cs:25 injects " GeneratedTestCaseN" for leaves
                 that share an NUnit full name with a sibling.
  selectionName  what a filter matches. FullNameFilter compares against the RAW
                 NUnit full name, with no such suffix.

  So a set of leaves sharing a selectionName is indivisible: naming it selects all
  of them. The planner keeps such a set on one worker and refuses to split it,
  because splitting it is what turns into "duplicate" and "missing" counts that
  look like a lost worker.

TWO HASHES PER RUN, AND WHY ONE IS NOT ENOUGH

  sourceSha256        the C# the assemblies are compiled from.
  runtimeInputSha256  everything else the run reads: scenes, prefabs, TMP font
                      atlases and TMP Settings, sprites, materials, shaders,
                      ProjectSettings, the package manifest and lock.

  A font atlas is not code. Rebuild one, or nudge a UI sprite, or change a quality
  level, and every .cs file in the tree is byte-identical -- so sourceSha256 alone
  says "same program" about a build whose player-visible output has changed, and
  the gate would merge yesterday's results with today's without a murmur. The
  second hash is what makes "same program" mean the same program.

  It covers Assets, ProjectSettings and Packages WHOLESALE, and subtracts a short
  list of named exceptions (RUNTIME_INPUT_SKIP_*). Wholesale-minus-exceptions is
  the only direction that is safe: an allow-list of interesting folders silently
  stops covering the next folder somebody adds, and a gap in a hash is invisible
  by construction -- it does not fail, it agrees.

  What it must NOT cover is anything the run itself writes, or the run invalidates
  itself. Two of those are real here and both have bitten this project's tooling:
  the capture PNGs the visual tests emit (which live outside all three roots, in
  UIDesign/), and Assets/InitTestScene<ticks>.unity, which Unity's own PlayMode
  launcher creates inside Assets while the run is in flight. Hash that scene and
  every PlayMode worker declares itself invalid the moment it starts.
"""
import argparse
import hashlib
import json
import os
import re
import signal
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET

UNITY = os.environ.get(
    "NEBULA_UNITY",
    "/Applications/Unity/Hub/Editor/2022.3.22f1/Unity.app/Contents/MacOS/Unity")
ENTRYPOINT = "Nebula.EditorTools.TestGate.NebulaGateWorker.Run"
MANIFEST_ENTRYPOINT = "Nebula.EditorTools.TestGate.NebulaTestManifest.Emit"

# Exactly runscoped.sh's and shard_worker.py's set. Generated .csproj files are
# deliberately absent: Unity rewrites them as a side effect of a test run, inside
# the very window this gate measures, so hashing them declared every run invalid.
HASH_ROOTS = [
    "Assets/Scripts", "Assets/Tests/EditMode", "Assets/Tests/PlayMode",
    "Assets/Editor/Nebula", "Assets/Editor/NebulaTestManifest",
    "Assets/Editor/NebulaVisualTexture", "Assets/Editor/NebulaWindowsVersionInfo",
]
HASH_SUFFIXES = (".cs", ".asmdef", ".asmref", ".rsp")

# The three trees Unity actually reads to decide what the player sees. Everything
# under them is a runtime input unless a rule below names it as an exception --
# never the other way round. An allow-list of "the folders that matter" is what a
# hash must not be built from: the day somebody adds Assets/UiAtlases, an allow-list
# keeps returning the same digest for a changed product, and nothing anywhere fails.
RUNTIME_INPUT_ROOTS = ("Assets", "ProjectSettings", "Packages")

# Directories that never hold an input. Library/Temp/obj are Unity's own caches and
# are rewritten continuously while the Editor is up; the rest are output.
RUNTIME_INPUT_SKIP_DIRS = frozenset((
    ".git", "Library", "Temp", "obj", "Logs", "Build", "Builds", "UserSettings",
    "TestResults", "TestReports", "Artifacts",
))

# Generated during the very window this hash measures, so hashing them would make
# every run invalidate itself. .csproj/.sln are the case the source hash already
# documents: Unity rewrites them as a side effect of running tests. .partial is this
# gate's own atomic-write temporary for the result XML and the manifest.
RUNTIME_INPUT_SKIP_SUFFIXES = (
    ".csproj", ".sln", ".suo", ".user", ".userprefs", ".pidb", ".booproj",
    ".partial", ".tmp", ".swp", ".orig", ".rej", ".log", "~",
)

# Editor and OS droppings. A Finder window opened on Assets/Resources writes a
# .DS_Store, and without this rule that is indistinguishable from a changed sprite.
RUNTIME_INPUT_SKIP_NAMES = frozenset((".DS_Store", "Thumbs.db", "Desktop.ini"))

# Unity's PlaymodeLauncher creates Assets/InitTestScene<DateTime.Ticks>.unity to
# boot a PlayMode run and deletes it at the end -- inside the measured window, by
# the process being measured. Twenty-three of them are sitting in this checkout
# right now from runs that were killed before the cleanup. Hashing them means every
# PlayMode worker reports "runtime inputs changed while the worker ran" about the
# scene it was itself asked to create.
INIT_TEST_SCENE = re.compile(r"^Assets/InitTestScene\d+\.unity(\.meta)?$")

COMPILER_ERROR = ("Scripts have compiler errors", "error CS",
                  "Aborting batchmode due to failure")

KNOWN_ASSEMBLIES = ("Nebula.Tests.PlayMode",)

# /2 adds runtimeInputSha256. /1 is still parsed rather than rejected on sight, so a
# manifest emitted by an older editor gets a refusal that says what is missing and
# how to re-emit it, instead of "schema is not /2" -- but the field itself is
# mandatory either way (build_plan), because a hash that some manifests may omit is
# a hash that proves nothing about any of them.
MANIFEST_SCHEMAS = ("nebula-leaf-manifest/1", "nebula-leaf-manifest/2")

# Lane definitions. `graphics` says whether the worker gets a real Metal device;
# `release` says whether the lane's verdict gates a release; `fixtureCeiling` says
# whether FIXTURE_TOTAL_MAX_SECONDS applies.
#
# That last one is off for the two endurance lanes, and it is a scoping fix rather
# than a relaxation. Read what the ceiling is for (its comment below): a fixture
# heavier than the ceiling cannot be moved between workers as a unit, so pinning it
# makes one worker hold the tail of the run. That premise is about BALANCE, and it
# is false for any lane whose leaves are long BY DESIGN -- there no ceiling below a
# single leaf's own runtime is reachable, so the rule cannot be satisfied, only
# tripped. Those lanes' fixtures are not indivisible either: each of the six
# AuditPart2LifecycleSoakTests leaves has its own selectionName and the planner
# already spreads them across workers.
#
# Left applying there it is not a safety check but an outage, and both endurance
# lanes were already in it. Soak: TutorialLifecycleTests contributes one 193.5s
# leaf, 193.5 > 120, and NO soak plan could be built at all. Diagnostic:
# EngineDestroyProbePlayModeTests totals 220.7s -- a bisection ladder whose own
# header calls it an engine probe, not a product test -- and no diagnostic plan
# could be built either. A lane that cannot be planned is a trap set for whoever
# needs it next, and it springs at the moment somebody needs it most.
#
# It stays live for ReleaseRegression and Graphics, where the premise does hold:
# their leaves are meant to be short, so a fixture over two minutes really is a
# balance problem and really is fixable. The endurance lanes' own thresholds and
# cycle counts are untouched by any of this and must stay that way; what changed is
# only which lanes the planner's balance ceiling binds.
#
# `coLocateFixtures` makes the FIXTURE the unit a worker is given, instead of the
# leaf. On for Graphics alone, and it is a correctness rule, not a tuning knob.
#
# A capture fixture writes state that a sibling leaf then reads:
# NebulaBrandMatrixCaptureTests.TheManifestHasNoPendingCells checks the manifest the
# capture leaf produced, and split across two processes it does not fail honestly --
# it fails with "the capture case did not run in this process; the manifest cannot
# be checked", which is a leaf reporting that it was never able to measure anything.
# The project already has the sibling rule for the other direction (capture fixtures
# must not be STACKED into one process, or UnloadUnusedAssets goes 10ms -> 64s); this
# is the same rule applied to splitting, and neither is expressible as a duration.
#
# Off everywhere else, and that is equally deliberate. ReleaseRegression is 833
# leaves over five workers and its fixtures are wildly uneven; making the fixture the
# unit there collapses the balance the whole planner exists to produce. Where it IS
# on, worse balance is the correct trade and is not a regression to be tuned away: a
# lane of 60 leaves that runs in about a minute per worker has no makespan worth
# protecting, and a split fixture produces a green that measured nothing.
LANES = {
    "ReleaseRegression": {"graphics": False, "release": True,  "fixtureCeiling": True,
                          "coLocateFixtures": False},
    "Graphics":          {"graphics": True,  "release": False, "fixtureCeiling": True,
                          "coLocateFixtures": True},
    "Soak":              {"graphics": False, "release": False, "fixtureCeiling": False,
                          "coLocateFixtures": False},
    "Diagnostic":        {"graphics": False, "release": False, "fixtureCeiling": False,
                          "coLocateFixtures": False},
}
# Graphics first, and it is the only entry whose position is load-bearing.
#
# NUnit propagates a class-level category to every method in the class, so a leaf
# routinely carries two lane names: ReleaseRegression from its fixture and Graphics
# from its own [Category]. Something has to win, and the tie-break is not a matter
# of taste -- the four lanes are not symmetric. Being in the wrong GATING lane is a
# reporting error somebody can see: a soak test counted as a regression makes the
# release lane slower and louder. Being on the wrong DEVICE is invisible: a Graphics
# leaf on -nographics does not fail, it passes without rendering anything, and that
# pass is then cited as evidence the rendering works. So the label that decides
# whether a real Metal device is present outranks every label that only decides
# which report a result lands in.
#
# It must outrank Soak and Diagnostic too, not just ReleaseRegression. Those lanes
# are headless, so a Soak+Graphics leaf classified by an earlier ordering landed in
# a headless lane, and the check_plan backstop then refused the ENTIRE Soak plan
# because of one leaf -- turning a mislabelled test into an outage for the whole
# lane. Graphics-first makes the safety property hold by construction; the backstop
# below stays as a second line for a plan that was edited rather than planned.
LANE_PRECEDENCE = ("Graphics", "Soak", "Diagnostic", "ReleaseRegression")
DEFAULT_LANE = "ReleaseRegression"

# A release leaf that takes longer than this is not a regression test, it is a soak
# test that has not been labelled, and it is what makes a shard unbalanceable.
RELEASE_LEAF_MAX_SECONDS = 30.0
# A fixture whose leaves together exceed this cannot be moved between workers as a
# unit when contamination forces it to be pinned, which is how one worker ends up
# holding the whole tail of the run.
FIXTURE_TOTAL_MAX_SECONDS = 120.0
# Never measured. Deliberately generous: underestimating an unknown leaf puts it on
# an already-full worker and the makespan is set by that worker.
UNKNOWN_LEAF_BUDGET_SECONDS = 20.0

DURATIONS_DEFAULT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                 "leaf-durations.json")
EXEMPTIONS_DEFAULT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                  "release-ceiling-exemptions.json")


class Refusal(Exception):
    """Reasons the gate will not start. Always plural: one refusal per problem, so a
    single run tells the operator everything that is wrong rather than the first
    thing."""

    def __init__(self, reasons):
        self.reasons = list(reasons)
        Exception.__init__(self, "; ".join(self.reasons))


# --------------------------------------------------------------------------- hash

def source_paths(root):
    paths = []
    for relative in HASH_ROOTS:
        base = os.path.join(root, relative)
        if not os.path.isdir(base):
            continue
        for directory, names, files in os.walk(base):
            names.sort()
            for name in sorted(files):
                if name.endswith(HASH_SUFFIXES):
                    paths.append(os.path.join(directory, name))
    return sorted(paths)


def source_hash(root):
    digest = hashlib.sha256()
    for path in source_paths(root):
        relative = os.path.relpath(path, root).replace(os.sep, "/")
        digest.update(relative.encode("utf-8") + b"\0")
        with open(path, "rb") as handle:
            for chunk in iter(lambda: handle.read(1 << 20), b""):
                digest.update(chunk)
        digest.update(b"\0")
    return digest.hexdigest()


# ------------------------------------------------------------- runtime inputs

def is_runtime_input(relative, name):
    """One place, so every exception to "everything under the three roots" is a
    line somebody can read, argue with, and write a test against."""
    if name in RUNTIME_INPUT_SKIP_NAMES:
        return False
    if name.endswith(RUNTIME_INPUT_SKIP_SUFFIXES):
        return False
    if INIT_TEST_SCENE.match(relative):
        return False
    return True


def file_digest(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def runtime_input_digests(root):
    """{relative path: sha256} for every runtime input in the tree.

    Per-file rather than one rolling digest, because "the runtime inputs changed"
    is an answer nobody can act on. The 581/564/0 incident was survivable partly
    because the evidence said WHICH file was stale; a bare hash mismatch over four
    and a half thousand files sends the operator to bisect by hand. Keeping the map
    costs half a megabyte and buys run_worker the ability to name the changed paths
    in its own status file.
    """
    digests = {}
    for relative_root in RUNTIME_INPUT_ROOTS:
        base = os.path.join(root, relative_root)
        if os.path.isfile(base):
            digests[relative_root] = file_digest(base)
            continue
        if not os.path.isdir(base):
            continue
        for directory, names, files in os.walk(base):
            names[:] = sorted(name for name in names
                              if name not in RUNTIME_INPUT_SKIP_DIRS)
            for name in sorted(files):
                path = os.path.join(directory, name)
                relative = os.path.relpath(path, root).replace(os.sep, "/")
                if not is_runtime_input(relative, name):
                    continue
                # A symlink's target is not part of this tree and its content is not
                # this tree's to promise; the five mirrors would disagree about it.
                if os.path.islink(path):
                    continue
                try:
                    digests[relative] = file_digest(path)
                except (IOError, OSError):
                    # Unreadable is a fact about the tree, and a fact that has to
                    # survive into the hash: silently skipping it would let a file
                    # that cannot be opened look identical to one that is not there.
                    digests[relative] = "unreadable"
    return digests


def runtime_input_hash(root, digests=None):
    """The second immutable identity of a run, beside sourceSha256."""
    digests = runtime_input_digests(root) if digests is None else digests
    roll = hashlib.sha256()
    for relative in sorted(digests):
        roll.update(relative.encode("utf-8") + b"\0")
        roll.update(digests[relative].encode("ascii") + b"\0")
    return roll.hexdigest()


def runtime_input_changes(before, after, limit=40):
    """What moved between two digest maps, as paths rather than as a hash."""
    changed = sorted(name for name in set(before) & set(after)
                     if before[name] != after[name])
    added = sorted(set(after) - set(before))
    removed = sorted(set(before) - set(after))
    return {
        "changed": changed[:limit], "changedCount": len(changed),
        "added": added[:limit], "addedCount": len(added),
        "removed": removed[:limit], "removedCount": len(removed),
    }


def dotnet_regex_escape(value):
    """Escape for .NET's Regex, not Python's.

    Only needed on the legacy -testFilter path. Filter.cs:25 documents -testFilter's
    values as regexes; every leaf name contains dots and a [TestCase] leaf's name
    contains parentheses, so a name passed raw neither matches itself literally nor
    fails to match its siblings. Python's re.escape also escapes '-', '&' and '~',
    which .NET does not recognise as escapes, so its output is not safe to hand to
    .NET -- hence this rather than re.escape.
    """
    out = []
    for character in value:
        if character in "\\*+?|{[()^$.#" or character.isspace():
            out.append("\\")
        out.append(character)
    return "".join(out)


def anchored_filter(name):
    """The only form of -testFilter that selects exactly one leaf."""
    return "^" + dotnet_regex_escape(name) + "$"


# ----------------------------------------------------------------------- manifest

def load_manifest(path):
    if not os.path.isfile(path):
        raise Refusal(["no leaf manifest at %s; run -executeMethod %s first"
                       % (path, MANIFEST_ENTRYPOINT)])
    try:
        with open(path, encoding="utf-8") as handle:
            manifest = json.load(handle)
    except Exception as error:
        raise Refusal(["leaf manifest is not readable: %s" % error])
    if manifest.get("schema") not in MANIFEST_SCHEMAS:
        raise Refusal(["leaf manifest schema is %r, expected one of %s"
                       % (manifest.get("schema"), ", ".join(MANIFEST_SCHEMAS))])
    leaves = manifest.get("leaves")
    if not isinstance(leaves, list) or not leaves:
        raise Refusal(["leaf manifest contains no leaves"])
    if manifest.get("leafCount") != len(leaves):
        raise Refusal(["leaf manifest says leafCount=%r but carries %d leaves; it was "
                       "read while it was being written"
                       % (manifest.get("leafCount"), len(leaves))])
    return manifest


def lane_of(leaf):
    categories = leaf.get("categories") or []
    for lane in LANE_PRECEDENCE:
        if lane in categories:
            return lane
    return DEFAULT_LANE


def is_uncategorised(leaf):
    categories = leaf.get("categories") or []
    return not any(lane in categories for lane in LANE_PRECEDENCE)


def lane_conflicts(leaves, lane):
    """Multi-lane leaves that bear on THIS lane, and where precedence sent them.

    Class-level propagation makes multi-lane leaves normal rather than exceptional,
    so they are reported, never refused. Reported narrowly, though: the first
    version listed every multi-lane leaf in the manifest on every plan, so the
    Diagnostic plan printed six rows about Graphics and Soak leaves that were never
    candidates for it. A report that is mostly irrelevant to the thing in front of
    you is one people learn to scroll past, and this one has to be read -- it is the
    only place a leaf LEAVING the lane you asked for is visible.

    So: only leaves carrying this lane's own name. Either precedence kept them
    (inThisPlan) or it took them somewhere else, and the second case is the one that
    makes an otherwise complete-looking lane incomplete.
    """
    rows = []
    for leaf in leaves:
        categories = leaf.get("categories") or []
        carried = [name for name in LANE_PRECEDENCE if name in categories]
        if len(carried) > 1 and lane in carried:
            won = lane_of(leaf)
            rows.append({"fullName": leaf["fullName"], "carries": carried,
                         "lane": won, "inThisPlan": won == lane})
    return sorted(rows, key=lambda row: row["fullName"])


# --------------------------------------------------------------------------- plan

def load_durations(path):
    if not path or not os.path.isfile(path):
        return {}
    try:
        with open(path, encoding="utf-8") as handle:
            data = json.load(handle)
    except Exception:
        return {}
    return data.get("leaves", {}) if isinstance(data, dict) else {}


def load_exemptions(path):
    """Named, capped, justified exceptions to RELEASE_LEAF_MAX_SECONDS.

    Returns (by_full_name, refusals). Refusals are fatal: a malformed exemption file
    must stop the gate rather than be skipped, because every way of skipping it ends
    with a ceiling that quietly does not apply.

    Three properties make this an audit record rather than a waiver.

    A `reason` is mandatory and must be prose. The value of a ceiling is not that it
    is never crossed, it is that crossing it is a decision somebody made and signed;
    an exemption with a blank reason is the ceiling deleted for one test, with no
    trace of who deleted it or why.

    A `capSeconds` is mandatory and is itself enforced. An exemption is a commitment
    to a specific number, not permission to be slow -- a leaf that blows through its
    own cap refuses the plan exactly as if it had no exemption. Otherwise the first
    exemption becomes an unbounded hole that grows silently for years.

    And every entry is audited against the measurements each time a plan is built, so
    an entry whose leaf got faster, moved lane, or was renamed is reported as stale.
    An exemption list nobody prunes stops describing the system it exempts.
    """
    refusals, by_name = [], {}
    if not path or not os.path.isfile(path):
        return by_name, refusals
    try:
        with open(path, encoding="utf-8") as handle:
            document = json.load(handle)
    except Exception as error:
        return by_name, ["ceiling exemption file is not readable: %s" % error]

    if document.get("schema") != "nebula-ceiling-exemptions/1":
        return by_name, ["ceiling exemption schema is %r, expected "
                         "nebula-ceiling-exemptions/1" % document.get("schema")]
    entries = document.get("exemptions")
    if not isinstance(entries, list):
        return by_name, ["ceiling exemption file has no exemptions list"]

    for index, entry in enumerate(entries):
        where = "exemption %d" % index
        if not isinstance(entry, dict):
            refusals.append("%s is not an object" % where)
            continue
        name = entry.get("fullName")
        if not name or not isinstance(name, str):
            refusals.append("%s names no leaf" % where)
            continue
        where = "exemption for %s" % name
        reason = entry.get("reason")
        if not isinstance(reason, str) or not reason.strip():
            refusals.append("%s has no reason; an exemption without a stated reason "
                            "is the ceiling deleted, not an exception to it" % where)
            continue
        cap = entry.get("capSeconds")
        if not isinstance(cap, (int, float)) or isinstance(cap, bool) or cap <= 0:
            refusals.append("%s has no usable capSeconds; an exemption must commit to "
                            "a number" % where)
            continue
        if name in by_name:
            refusals.append("%s is listed twice; which cap applies is ambiguous" % where)
            continue
        by_name[name] = {"fullName": name, "capSeconds": float(cap),
                         "reason": reason.strip()}
    return by_name, refusals


def audit_exemptions(exemptions, leaves, durations):
    """Each exemption's current standing against the measurements. Never refuses.

    `applied` means the leaf really is over the base ceiling and within its cap.
    `stale` means the entry no longer does anything and should be deleted.
    `unverified` means there is no measurement yet, so nothing can be said -- which
    is honest rather than reassuring, and is the normal state on a cold cache.
    """
    by_name = {leaf["fullName"]: leaf for leaf in leaves}
    audit = []
    for name in sorted(exemptions):
        entry = dict(exemptions[name])
        leaf = by_name.get(name)
        seconds, origin = (budget_for(leaf, durations) if leaf else (None, None))
        entry["measuredSeconds"] = round(seconds, 2) if origin == "measured" else None
        if entry["capSeconds"] <= RELEASE_LEAF_MAX_SECONDS:
            entry["state"] = "stale"
            entry["note"] = ("cap %.0fs is at or below the %.0fs base ceiling, so this "
                             "entry can never apply" % (entry["capSeconds"],
                                                        RELEASE_LEAF_MAX_SECONDS))
        elif leaf is None:
            entry["state"] = "stale"
            entry["note"] = "names no leaf in the manifest; renamed or deleted"
        elif not LANES[lane_of(leaf)]["release"]:
            entry["state"] = "stale"
            entry["note"] = ("its leaf is in the %s lane, which has no release ceiling"
                             % lane_of(leaf))
        elif origin != "measured":
            entry["state"] = "unverified"
            entry["note"] = "no measured duration yet; cannot tell whether it is needed"
        elif seconds <= RELEASE_LEAF_MAX_SECONDS:
            entry["state"] = "stale"
            entry["note"] = ("now runs in %.1fs, inside the %.0fs ceiling; delete this "
                             "entry" % (seconds, RELEASE_LEAF_MAX_SECONDS))
        else:
            entry["state"] = "applied"
            entry["note"] = ("%.1fs, over the %.0fs ceiling and within its %.0fs cap"
                             % (seconds, RELEASE_LEAF_MAX_SECONDS, entry["capSeconds"]))
        audit.append(entry)
    return audit


def budget_for(leaf, durations):
    row = durations.get(leaf["fullName"])
    if isinstance(row, dict) and isinstance(row.get("seconds"), (int, float)):
        return float(row["seconds"]), "measured"
    return UNKNOWN_LEAF_BUDGET_SECONDS, "unknown"


def selection_of(leaf):
    return leaf.get("selectionName") or leaf["fullName"]


def group_selection_names(group):
    """Every name a filter must carry to select this group's leaves.

    A leaf-level group is one selection name; a fixture-level group is all of its
    fixture's. Named separately from the group KEY because a fixture name is not a
    selectable identity -- Filter.testNames is compared by ordinal equality against
    leaf full names, so handing a worker "Nebula.Tests.PlayMode.FooTests" selects
    nothing at all and the worker reports a clean zero-test run.
    """
    names = group.get("selectionNames")
    return list(names) if names else [group["selectionName"]]


def group_leaves(leaves, by_fixture=False):
    """The indivisible units a worker can be given.

    Leaf level, the default: all leaves sharing a selectionName. Anything smaller
    cannot be selected without also selecting the rest of its group, and a plan that
    pretends otherwise produces exactly the duplicate/missing counts the aggregate is
    meant to catch.

    Fixture level, for lanes with coLocateFixtures: all leaves of one fixture. This
    is strictly coarser -- leaves sharing a selectionName always share a fixture --
    so it subsumes the leaf-level rule rather than competing with it, and no group
    can ever straddle the two.
    """
    groups = {}
    for leaf in leaves:
        selection = selection_of(leaf)
        key = (leaf.get("fixture") or selection) if by_fixture else selection
        groups.setdefault(key, []).append(leaf)
    return groups


def assign(groups, workers, durations):
    """Longest-processing-time-first over leaf groups: the 4/3-competitive makespan
    split, applied to the units that are actually movable."""
    buckets = [{"worker": i + 1, "groups": [], "seconds": 0.0} for i in range(workers)]
    weighted = []
    for name, leaves in groups.items():
        seconds = sum(budget_for(leaf, durations)[0] for leaf in leaves)
        weighted.append((seconds, name, leaves))
    for seconds, name, leaves in sorted(weighted, key=lambda row: (-row[0], row[1])):
        target = min(buckets, key=lambda bucket: (bucket["seconds"], bucket["worker"]))
        selections = []
        for leaf in leaves:
            if selection_of(leaf) not in selections:
                selections.append(selection_of(leaf))
        target["groups"].append({"selectionName": name, "selectionNames": selections,
                                 "leaves": leaves, "seconds": round(seconds, 2)})
        target["seconds"] += seconds
    return buckets


def check_plan(plan, durations, exemptions=None):
    """Everything the gate refuses to start on. One entry per rule, all evaluated."""
    reasons = []
    exemptions = exemptions or {}
    leaves = [leaf for worker in plan["workers"] for group in worker["groups"]
              for leaf in group["leaves"]]

    seen_full, seen_unique = {}, {}
    for leaf in leaves:
        seen_full.setdefault(leaf["fullName"], []).append(leaf)
        seen_unique.setdefault(leaf.get("uniqueName"), []).append(leaf)
    for name, rows in sorted(seen_full.items()):
        if len(rows) > 1:
            reasons.append("duplicate FullName across the plan: %s (x%d)" % (name, len(rows)))
    for name, rows in sorted(seen_unique.items()):
        if name and len(rows) > 1:
            reasons.append("duplicate UniqueName across the plan: %s (x%d)" % (name, len(rows)))

    # A selectionName may only live on one worker. Selecting it on two runs its whole
    # group twice, and the aggregate reports that as a duplicate leaf with no way to
    # tell it from a genuine double-run.
    by_selection = {}
    for worker in plan["workers"]:
        for group in worker["groups"]:
            for name in group_selection_names(group):
                by_selection.setdefault(name, set()).add(worker["worker"])
    for name, owners in sorted(by_selection.items()):
        if len(owners) > 1:
            reasons.append("selectionName %s is split across workers %s"
                           % (name, sorted(owners)))

    # And the same rule one level up, for the lanes that need the fixture whole.
    # Checked over the PLAN rather than trusted from the grouping, for the reason
    # run_worker re-checks everything: plan.json is a file a resume can rewrite and a
    # second operator can edit, and a fixture silently split here is not a failure
    # anyone would recognise -- the sibling leaf reports that it could not measure
    # anything, which reads like a product bug.
    fixture_owners = {}
    for worker in plan["workers"]:
        if not LANES.get(worker["lane"], {}).get("coLocateFixtures", False):
            continue
        for group in worker["groups"]:
            for leaf in group["leaves"]:
                fixture = leaf.get("fixture") or "<no fixture>"
                fixture_owners.setdefault(fixture, set()).add(worker["worker"])
    for fixture, owners in sorted(fixture_owners.items()):
        if len(owners) > 1:
            reasons.append("fixture %s is split across workers %s, and lane %s requires "
                           "each fixture whole in one process: a capture leaf and the "
                           "sibling that verifies what it wrote cannot be in different "
                           "processes"
                           % (fixture, sorted(owners), plan.get("lane", "?")))

    fixture_measured, fixture_unmeasured = {}, {}
    for worker in plan["workers"]:
        lane = worker["lane"]
        headless = not worker["graphics"]
        release = LANES[lane]["release"]
        for group in worker["groups"]:
            for leaf in group["leaves"]:
                categories = leaf.get("categories") or []
                seconds, origin = budget_for(leaf, durations)
                fixture = (lane, leaf.get("fixture") or "<no fixture>")
                if origin == "measured":
                    fixture_measured[fixture] = fixture_measured.get(fixture, 0.0) + seconds
                else:
                    fixture_unmeasured[fixture] = fixture_unmeasured.get(fixture, 0) + 1

                if release and ("Soak" in categories or "Diagnostic" in categories):
                    reasons.append("%s is Soak/Diagnostic but planned into release lane %s"
                                   % (leaf["fullName"], lane))
                if headless and "Graphics" in categories:
                    reasons.append("%s is Graphics but planned onto headless worker %d"
                                   % (leaf["fullName"], worker["worker"]))
                if release and origin == "measured" and seconds > RELEASE_LEAF_MAX_SECONDS:
                    exemption = exemptions.get(leaf["fullName"])
                    if exemption is None:
                        reasons.append("release leaf %s takes %.1fs, over the %.0fs "
                                       "ceiling" % (leaf["fullName"], seconds,
                                                    RELEASE_LEAF_MAX_SECONDS))
                    elif seconds > exemption["capSeconds"]:
                        # The cap is the commitment. A leaf past its own stated number
                        # is refused exactly as if it had no exemption, because the
                        # alternative -- an exemption that only ever has to be granted
                        # once -- is a hole that widens by itself.
                        reasons.append("release leaf %s is exempted to %.0fs but takes "
                                       "%.1fs; the exemption does not cover this"
                                       % (leaf["fullName"], exemption["capSeconds"],
                                          seconds))
                if release and leaf.get("runState") != "Runnable":
                    reasons.append("release leaf %s is %s, not Runnable"
                                   % (leaf["fullName"], leaf.get("runState")))
                if leaf.get("assembly") not in KNOWN_ASSEMBLIES:
                    reasons.append("%s is in unknown assembly %r"
                                   % (leaf["fullName"], leaf.get("assembly")))
                # RuntimeTestRunnerFilter.AddFilters reads a leading '!' as an
                # exclusion, so one such name would silently subtract itself and
                # everything else from the run.
                if leaf["fullName"].startswith("!") or (leaf.get("selectionName") or "").startswith("!"):
                    reasons.append("%s begins with '!', which a filter reads as an exclusion"
                                   % leaf["fullName"])
                # CommandLineOption.SplitStringToArray splits on ';'. Nothing on the
                # -executeMethod path is affected, but a name carrying one cannot be
                # expressed on the legacy -testFilter path and the difference between
                # the two drivers must not be a silent one.
                if ";" in leaf["fullName"]:
                    reasons.append("%s contains ';', which no command-line filter can express"
                                   % leaf["fullName"])

    # Measured seconds only, exactly as the per-leaf ceiling works. The alternative
    # was tried and is worse than useless: charging every never-run leaf the
    # conservative unknown budget put any fixture with seven unmeasured leaves over
    # the ceiling, so on a cold duration cache -- which is every first run, and this
    # gate has never had a warm one -- the gate refused the entire suite and could
    # never produce the measurements that would have let it start. A ceiling nobody
    # can get under is not a safety check, it is an outage. The count of unmeasured
    # leaves is reported alongside, so the total reads as the floor it actually is.
    #
    # Keyed by (lane, fixture): the ceiling is a per-lane balance rule, and the Soak
    # lane is exempt from it by construction (see LANES). Totalling a fixture across
    # lanes would let one lane's leaves refuse another lane's plan.
    for key in sorted(set(fixture_measured) | set(fixture_unmeasured)):
        lane, fixture = key
        if not LANES.get(lane, {}).get("fixtureCeiling", True):
            continue
        seconds = fixture_measured.get(key, 0.0)
        unmeasured = fixture_unmeasured.get(key, 0)
        if seconds > FIXTURE_TOTAL_MAX_SECONDS:
            reasons.append("fixture %s totals %.1fs across its measured leaves in lane "
                           "%s, over the %.0fs ceiling (%d further leaf/leaves "
                           "unmeasured)"
                           % (fixture, seconds, lane, FIXTURE_TOTAL_MAX_SECONDS,
                              unmeasured))

    empty = [worker["worker"] for worker in plan["workers"] if not worker["groups"]]
    if empty:
        reasons.append("worker(s) %s were given nothing to run" % empty)
    return reasons


def build_plan(manifest_path, out_dir, workers, repo, lane=DEFAULT_LANE,
               durations_path=DURATIONS_DEFAULT, expect_source_hash=None,
               graphics=None, run_id=None, exemptions_path=EXEMPTIONS_DEFAULT,
               expect_runtime_hash=None):
    manifest = load_manifest(manifest_path)
    exemptions, reasons = load_exemptions(exemptions_path)

    actual_hash = source_hash(repo)
    runtime_digests = runtime_input_digests(repo)
    actual_runtime_hash = runtime_input_hash(repo, runtime_digests)
    declared = manifest.get("sourceSha256")
    if not declared:
        reasons.append("the manifest carries no sourceSha256; re-emit it with "
                       "NEBULA_TEST_MANIFEST_SOURCE_HASH set to `playmode_gate.py hash`")
    elif declared != actual_hash:
        reasons.append("manifest source hash %s does not match the tree's %s"
                       % (declared[:16], actual_hash[:16]))
    # The runtime input hash is as mandatory as the source hash and for the same
    # reason: a manifest that carries only one of them was listed from a tree whose
    # other half nobody can identify, and every later comparison against it is
    # vacuous. Optional would be worse than absent -- it would look like a check.
    declared_runtime = manifest.get("runtimeInputSha256")
    if not declared_runtime:
        reasons.append("the manifest carries no runtimeInputSha256; re-emit it with "
                       "NEBULA_TEST_MANIFEST_RUNTIME_HASH set to "
                       "`playmode_gate.py runtime-hash`")
    elif declared_runtime != actual_runtime_hash:
        reasons.append("manifest runtime input hash %s does not match the tree's %s"
                       % (declared_runtime[:16], actual_runtime_hash[:16]))
    if expect_source_hash and expect_source_hash != actual_hash:
        reasons.append("tree source hash %s does not match the expected %s"
                       % (actual_hash[:16], expect_source_hash[:16]))
    if expect_runtime_hash and expect_runtime_hash != actual_runtime_hash:
        reasons.append("tree runtime input hash %s does not match the expected %s"
                       % (actual_runtime_hash[:16], expect_runtime_hash[:16]))
    if lane not in LANES:
        reasons.append("unknown lane %r; known lanes are %s" % (lane, sorted(LANES)))
    if reasons:
        raise Refusal(reasons)

    durations = load_durations(durations_path)
    mine = [leaf for leaf in manifest["leaves"] if lane_of(leaf) == lane]
    if not mine:
        raise Refusal(["no leaves classify into lane %s" % lane])
    uncategorised = sorted(leaf["fullName"] for leaf in mine if is_uncategorised(leaf))

    co_locate = LANES[lane].get("coLocateFixtures", False)
    buckets = assign(group_leaves(mine, by_fixture=co_locate), workers, durations)
    run_id = run_id or uuid.uuid4().hex[:12]
    use_graphics = LANES[lane]["graphics"] if graphics is None else bool(graphics)

    plan = {
        "schema": "nebula-playmode-plan/1",
        "runId": run_id,
        "lane": lane,
        "manifest": os.path.abspath(manifest_path),
        "repo": os.path.abspath(repo),
        "sourceSha256": actual_hash,
        "runtimeInputSha256": actual_runtime_hash,
        "runtimeInputFileCount": len(runtime_digests),
        "plannedEpoch": time.time(),
        "workerCount": workers,
        "coLocateFixtures": co_locate,
        "fixtureCount": len({leaf.get("fixture") or "<no fixture>" for leaf in mine}),
        "leafCount": len(mine),
        "uncategorised": uncategorised,
        "uncategorisedCount": len(uncategorised),
        "laneConflicts": lane_conflicts(manifest["leaves"], lane),
        "durationsPath": durations_path,
        "exemptionsPath": exemptions_path,
        # Audited against the whole manifest, not just this lane's leaves, so an
        # entry naming a leaf that moved to another lane is still recognisable as
        # stale rather than as a leaf that no longer exists.
        "exemptionAudit": audit_exemptions(exemptions, manifest["leaves"], durations),
        "workers": [],
    }
    for bucket in buckets:
        leaves = [leaf for group in bucket["groups"] for leaf in group["leaves"]]
        plan["workers"].append({
            "worker": bucket["worker"],
            "lane": lane,
            "graphics": use_graphics,
            "graphicsRequired": any("Graphics" in (leaf.get("categories") or [])
                                    for leaf in leaves),
            "groups": bucket["groups"],
            "leafCount": len(leaves),
            "fixtures": sorted({leaf.get("fixture") or "<no fixture>" for leaf in leaves}),
            "fixtureCount": len({leaf.get("fixture") or "<no fixture>" for leaf in leaves}),
            "estimatedSeconds": round(bucket["seconds"], 1),
            "measuredLeaves": sum(1 for leaf in leaves
                                  if budget_for(leaf, durations)[1] == "measured"),
        })

    problems = check_plan(plan, durations, exemptions)
    if problems:
        raise Refusal(problems)

    write_plan(plan, out_dir)
    return plan


def worker_paths(out_dir, run_id, worker):
    """Every artefact path carries the run id.

    This is the structural half of the stale-result defence. Deleting the previous
    file and comparing mtimes are both checks that can be forgotten or reordered; a
    path that did not exist before this run cannot hold another run's numbers no
    matter what anybody forgets.
    """
    return {
        "planPath": os.path.join(out_dir, "worker-%d.%s.plan.json" % (worker, run_id)),
        "resultPath": os.path.join(out_dir, "results", "worker-%d.%s.xml" % (worker, run_id)),
        "journalPath": os.path.join(out_dir, "journals", "worker-%d.%s.jsonl" % (worker, run_id)),
        "logPath": os.path.join(out_dir, "logs", "worker-%d.%s.log" % (worker, run_id)),
        "statusPath": os.path.join(out_dir, "worker-%d.%s.status.json" % (worker, run_id)),
        "orderedPath": os.path.join(out_dir, "worker-%d.%s.orderedtests.txt" % (worker, run_id)),
    }


def write_plan(plan, out_dir):
    for sub in ("", "results", "journals", "logs"):
        os.makedirs(os.path.join(out_dir, sub), exist_ok=True)

    for worker in plan["workers"]:
        paths = worker_paths(out_dir, plan["runId"], worker["worker"])
        worker.update(paths)
        leaves = [leaf for group in worker["groups"] for leaf in group["leaves"]]
        ordered = [leaf["fullName"] for leaf in leaves]
        selection = []
        for group in worker["groups"]:
            for name in group_selection_names(group):
                if name not in selection:
                    selection.append(name)

        # The worker plan the Editor reads. Flat, with a string[] -- JsonUtility can
        # deserialise nothing richer, and using it means the C# side needs no JSON
        # parser of its own.
        with open(paths["planPath"], "w", encoding="utf-8") as handle:
            json.dump({
                "worker": worker["worker"],
                "runId": plan["runId"],
                "lane": worker["lane"],
                "assembly": KNOWN_ASSEMBLIES[0],
                "sourceSha256": plan["sourceSha256"],
                "runtimeInputSha256": plan["runtimeInputSha256"],
                "resultPath": os.path.abspath(paths["resultPath"]),
                "journalPath": os.path.abspath(paths["journalPath"]),
                "testNames": selection,
            }, handle, indent=2)

        # The ordered list is an artefact, not the selection mechanism. It is the
        # documented order for the contamination-sensitive fixtures pinned to this
        # worker, and it is the exact file -orderedTestListFile takes: its lines are
        # compared against TestExtensions.GetFullName(node.FullName, childIndex),
        # which is character-for-character ITestAdaptor.FullName. It cannot be used
        # through TestRunnerApi.Execute -- ExecutionSettings.orderedTestNames is
        # internal (ExecutionSettings.cs:55) -- so intra-worker ordering is only
        # available to a -runTests invocation. Contamination pinning here is enforced
        # by CO-LOCATION on one worker, which the planner can guarantee, and this file
        # records the order a -runTests re-run should use.
        with open(paths["orderedPath"], "w", encoding="utf-8") as handle:
            handle.write("\n".join(ordered) + "\n")

    with open(os.path.join(out_dir, "plan.json"), "w", encoding="utf-8") as handle:
        json.dump(plan, handle, indent=2, sort_keys=True)


def load_plan(out_dir):
    path = os.path.join(out_dir, "plan.json")
    if not os.path.isfile(path):
        raise Refusal(["no plan at %s" % path])
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def worker_entry(plan, worker):
    for entry in plan["workers"]:
        if entry["worker"] == worker:
            return entry
    raise Refusal(["plan has no worker %d" % worker])


# ---------------------------------------------------------------------- execution

def scan_log(path):
    found = []
    if not os.path.isfile(path):
        return found
    with open(path, encoding="utf-8", errors="replace") as handle:
        for line in handle:
            if any(token in line for token in COMPILER_ERROR):
                text = line.strip()[:300]
                if text not in found:
                    found.append(text)
    return found[:20]


def read_journal(path):
    """Every well-formed line. A truncated final line is dropped rather than fatal:
    it is what a process killed mid-write leaves behind, and the lines before it are
    still proof."""
    entries, damaged = [], 0
    if not os.path.isfile(path):
        return entries, damaged
    with open(path, encoding="utf-8", errors="replace") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            try:
                entries.append(json.loads(line))
            except Exception:
                damaged += 1
    return entries, damaged


def journal_leaves(entries, source_sha256=None, runtime_sha256=None):
    """The leaves a journal proves finished, filtered to one pair of hashes.

    Never merged across either hash. Evidence from a different tree is evidence
    about a different program, and adding it to this run's totals is how a run
    reports a number for a suite it did not execute. Both hashes have to filter,
    not just the first: a line stamped with this run's sourceSha256 and a different
    runtimeInputSha256 comes from the same C# reading a different font atlas, and
    that is exactly the case the second hash was added to catch.

    A line with no runtimeInputSha256 at all -- written by a worker from before the
    field existed -- fails the filter and is not counted. That is the right answer:
    it is not evidence about the runtime inputs of anything.
    """
    proven = {}
    for entry in entries:
        if entry.get("kind") != "leaf":
            continue
        if source_sha256 is not None and entry.get("sourceSha256") != source_sha256:
            continue
        if runtime_sha256 is not None and entry.get("runtimeInputSha256") != runtime_sha256:
            continue
        proven.setdefault(entry.get("fullName"), []).append(entry)
    return proven


def unity_command(repo, worker, paths):
    """`repo` is the tree Unity opens; every path below is in the shared --out.

    Those two are deliberately separate. Five parallel workers need five APFS
    mirrors -- they cannot share a Library lock -- but they need ONE --out, because
    results, journals and status files only mean something aggregated together.
    Reading -projectPath from the plan forced those to be the same directory, and
    the workaround (five --out copies with `repo` rewritten) put the evidence in the
    shared directory while each worker validated in its own empty one, so all five
    reported "Unity exited without writing a result XML" over 839 leaves that had
    in fact been written and proven.
    """
    command = [UNITY, "-batchmode", "-projectPath", os.path.abspath(repo)]
    if not worker["graphics"]:
        command.append("-nographics")
    command += [
        "-logFile", os.path.abspath(paths["logPath"]),
        "-executeMethod", ENTRYPOINT,
        "-nebulaWorkerPlan", os.path.abspath(paths["planPath"]),
    ]
    return command


def _launch(command, watchdog):
    """Its own process group, so the watchdog takes down Unity and everything it
    spawned. Killing the parent alone leaves the child Editor holding the Library
    lock and the worker never really stops."""
    process = subprocess.Popen(command, start_new_session=True,
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        return process.wait(timeout=watchdog), False, process.pid
    except subprocess.TimeoutExpired:
        os.killpg(os.getpgid(process.pid), signal.SIGTERM)
        try:
            return process.wait(timeout=45), True, process.pid
        except subprocess.TimeoutExpired:
            os.killpg(os.getpgid(process.pid), signal.SIGKILL)
            return process.wait(), True, process.pid


def run_worker(out_dir, worker_number, watchdog=2700.0, launch=_launch, plan=None,
               repo=None):
    """Run one worker. `repo` overrides which tree Unity opens, nothing else.

    The override is only safe because of the hash check below, and the check is only
    worth anything if it runs BEFORE Unity does -- a mirror that is not the planned
    tree must cost nothing, not twenty minutes and a result nobody can use.
    """
    plan = plan or load_plan(out_dir)
    worker = worker_entry(plan, worker_number)
    paths = worker_paths(out_dir, plan["runId"], worker_number)
    repo = os.path.abspath(repo or plan["repo"])
    for sub in ("results", "journals", "logs"):
        os.makedirs(os.path.join(out_dir, sub), exist_ok=True)

    status = {
        "worker": worker_number,
        "runId": plan["runId"],
        "lane": worker["lane"],
        "graphics": worker["graphics"],
        "expectedSourceSha256": plan["sourceSha256"],
        "expectedRuntimeInputSha256": plan.get("runtimeInputSha256"),
        "expectedLeaves": worker["leafCount"],
        "watchdogSeconds": watchdog,
        # Which tree actually produced this. The five mirrors are byte-identical by
        # construction and hash-identical by check, so nothing downstream depends on
        # this -- but when a worker is the odd one out, "which mirror was it" is the
        # first question, and the evidence should not require reconstructing the
        # command line to answer it.
        "repo": repo,
        "planRepo": os.path.abspath(plan["repo"]),
        "repoIsMirror": repo != os.path.abspath(plan["repo"]),
    }
    status.update(paths)

    def finish(verdict, note=None):
        status["result"] = verdict
        status["note"] = note
        with open(paths["statusPath"], "w", encoding="utf-8") as handle:
            json.dump(status, handle, indent=2, sort_keys=True)
        return verdict, status

    # The plan is re-checked here, not only where it was built. plan.json is a file on
    # disk that a second operator can edit, that a resume can rewrite, and that a
    # half-finished earlier gate can leave behind; the checks are worth nothing if the
    # only thing they ever saw was the plan as first constructed in memory.
    # Exemptions are re-read from the FILE, not from plan.json's audit record. The
    # file is reviewed and committed; plan.json is generated. Reading the granted
    # exemptions out of the generated artefact would mean anyone able to edit it
    # could grant themselves one, which is precisely what re-checking the plan on
    # disk exists to catch.
    exemptions, exemption_refusals = load_exemptions(plan.get("exemptionsPath"))
    if exemption_refusals:
        status["exemptionProblems"] = exemption_refusals
        return finish("RUN_INVALID", "the ceiling exemption file is not usable: %s"
                      % exemption_refusals[0])
    problems = check_plan(plan, load_durations(plan.get("durationsPath")), exemptions)
    if problems:
        status["planProblems"] = problems
        return finish("RUN_INVALID", "the plan on disk does not pass its own checks: %s"
                      % problems[0])

    # Everything that can refuse, refuses HERE -- before a single file is removed.
    #
    # The earlier order hashed the tree after deleting this worker's result, journal
    # and log. That was survivable while `run` was only ever called once per worker,
    # and stops being survivable the moment retries exist: a retry pointed at the
    # wrong mirror would delete the journal proving what the good attempt had already
    # finished, and then refuse. A refused run must leave the world exactly as it
    # found it, or `resume` is reading the wreckage of the refusal.
    where = "mirror " + repo if status["repoIsMirror"] else "the tree"
    before = source_hash(repo)
    status["sourceHashBefore"] = before
    if before != plan["sourceSha256"]:
        return finish("RUN_INVALID",
                      "%s hashes %s, but the plan is for %s -- this is not the planned "
                      "tree" % (where, before[:16], plan["sourceSha256"][:16]))

    # The same check on the other half of the run's identity, and in the same place:
    # before Unity, before anything is deleted. A mirror whose C# matches and whose
    # font atlases do not is precisely the mirror somebody re-synced with `git
    # checkout -- Assets/Scripts`, and it is worth nothing and costs twenty minutes.
    expected_runtime = plan.get("runtimeInputSha256")
    if not expected_runtime:
        return finish("RUN_INVALID",
                      "the plan carries no runtimeInputSha256; re-plan with a gate that "
                      "stamps one, or nothing ties this run to the assets it read")
    runtime_before_map = runtime_input_digests(repo)
    runtime_before = runtime_input_hash(repo, runtime_before_map)
    status["runtimeInputHashBefore"] = runtime_before
    status["runtimeInputFileCount"] = len(runtime_before_map)
    if runtime_before != expected_runtime:
        return finish("RUN_INVALID",
                      "%s has runtime inputs hashing %s, but the plan is for %s -- the "
                      "C# matches and the assets do not"
                      % (where, runtime_before[:16], expected_runtime[:16]))

    # Deleted only now, and even though the run id already makes these paths unique.
    # The redundancy is the point: the run id protects against last week's file, this
    # protects against a retry of THIS run id reading its own first attempt.
    for key in ("resultPath", "journalPath", "logPath", "statusPath"):
        if os.path.exists(paths[key]):
            os.remove(paths[key])

    started = time.time()
    status["startEpoch"] = started
    command = unity_command(repo, worker, paths)
    status["command"] = " ".join(command)

    exit_code, timed_out, pid = launch(command, watchdog)
    finished = time.time()
    status.update({"finishEpoch": finished, "wallSeconds": round(finished - started, 1),
                   "exitCode": exit_code, "timedOut": timed_out, "pid": pid})

    entries, damaged = read_journal(paths["journalPath"])
    proven = journal_leaves(entries, plan["sourceSha256"], expected_runtime)
    status["journalLines"] = len(entries)
    status["journalDamagedLines"] = damaged
    status["journalProvenLeaves"] = len(proven)

    status["compilerErrors"] = scan_log(paths["logPath"])
    after = source_hash(repo)
    status["sourceHashAfter"] = after
    runtime_after_map = runtime_input_digests(repo)
    runtime_after = runtime_input_hash(repo, runtime_after_map)
    status["runtimeInputHashAfter"] = runtime_after

    if status["compilerErrors"]:
        return finish("RUN_INVALID",
                      "the project did not compile; any results are from a stale assembly")
    if after != before:
        return finish("RUN_INVALID", "assembly sources changed while the worker ran")
    if runtime_after != runtime_before:
        # Named, not counted. "Something changed" over four and a half thousand
        # files is a dead end; "ProjectSettings/QualitySettings.asset changed" is a
        # test that failed to restore what it altered, and is fixable today.
        changes = runtime_input_changes(runtime_before_map, runtime_after_map)
        status["runtimeInputChanges"] = changes
        first = (changes["changed"] + changes["added"] + changes["removed"])[:3]
        return finish("RUN_INVALID",
                      "runtime inputs changed while the worker ran (%d changed, %d added, "
                      "%d removed): %s"
                      % (changes["changedCount"], changes["addedCount"],
                         changes["removedCount"], ", ".join(first)))
    if not os.path.isfile(paths["resultPath"]):
        return finish("RUN_INVALID",
                      "Unity exited (%s) without writing a result XML" % exit_code)

    written = os.path.getmtime(paths["resultPath"])
    status["xmlMtime"] = written
    if written < started:
        return finish("RUN_INVALID",
                      "result XML predates this run; it is not this run's result")

    try:
        root = ET.parse(paths["resultPath"]).getroot()
    except Exception as error:
        return finish("RUN_INVALID", "result XML is not parseable: %s" % error)

    for key in ("total", "passed", "failed", "skipped", "inconclusive"):
        try:
            status[key] = int(root.get(key) or 0)
        except ValueError:
            status[key] = 0
    status["xmlSourceSha256"] = root.get("nebula-source-sha256")
    if status["xmlSourceSha256"] and status["xmlSourceSha256"] != plan["sourceSha256"]:
        return finish("RUN_INVALID", "result XML was produced from a different source tree")
    status["xmlRuntimeInputSha256"] = root.get("nebula-runtime-input-sha256")
    if status["xmlRuntimeInputSha256"] and status["xmlRuntimeInputSha256"] != expected_runtime:
        return finish("RUN_INVALID",
                      "result XML was produced from different runtime inputs")
    if status["total"] == 0:
        return finish("RUN_INVALID", "the worker ran zero tests")
    if timed_out:
        # Name the leaf that was in flight, and say plainly that nothing inside the
        # run stopped it first.
        #
        # A killed worker's journal proves what FINISHED; the leaf that did not is
        # the one worth looking at, and it is recoverable -- it is the first name in
        # this worker's documented order with no journal line. Without it the
        # operator reads "watchdog fired at 5400s" and starts by looking for a
        # per-leaf timeout that would have caught it, which for the leaves that
        # carry no [Timeout] does not exist. Hunting a guard that was never there is
        # the most expensive way to start a diagnosis.
        ordered = [leaf["fullName"] for group in worker["groups"]
                   for leaf in group["leaves"]]
        in_flight = [name for name in ordered if name not in proven]
        status["leavesUnproven"] = in_flight
        return finish("RUN_INVALID",
                      "watchdog fired at %.0fs with no per-leaf timeout catching it "
                      "first; %d of %d leaves are proven by the journal, and the leaf "
                      "in flight was %s -- resume --worker %d re-runs only the "
                      "unproven ones"
                      % (watchdog, len(proven), worker["leafCount"],
                         in_flight[0] if in_flight else "<none: all leaves finished>",
                         worker_number))
    if status["failed"] or status["inconclusive"]:
        return finish("FAIL", "%d failed, %d inconclusive"
                      % (status["failed"], status["inconclusive"]))
    return finish("PASS", "%d/%d passed in %.0fs"
                  % (status["passed"], status["total"], status["wallSeconds"]))


def resume_plan(out_dir, worker_number, plan=None):
    """The leaves this worker still owes, given what its journal already proves.

    A killed worker is not a lost worker. Everything with a journal line ran and its
    result is known; re-running it would only cost time and give contamination a
    second chance. What is returned is the unfinished remainder.
    """
    plan = plan or load_plan(out_dir)
    worker = worker_entry(plan, worker_number)
    paths = worker_paths(out_dir, plan["runId"], worker_number)
    entries, _ = read_journal(paths["journalPath"])
    proven = journal_leaves(entries, plan["sourceSha256"], plan.get("runtimeInputSha256"))

    remaining, finished = [], []
    for group in worker["groups"]:
        # A group is indivisible. It is only finished when every leaf in it is, and a
        # partly-proven group has to be re-run whole because there is no filter that
        # names a subset of it.
        leaves = group["leaves"]
        if all(leaf["fullName"] in proven for leaf in leaves):
            finished.extend(leaf["fullName"] for leaf in leaves)
        else:
            remaining.append(group)
    return {
        "worker": worker_number,
        "runId": plan["runId"],
        "sourceSha256": plan["sourceSha256"],
        "finishedLeaves": sorted(finished),
        "remainingGroups": remaining,
        "runtimeInputSha256": plan.get("runtimeInputSha256"),
        "remainingLeaves": sorted(leaf["fullName"] for group in remaining
                                  for leaf in group["leaves"]),
    }


# ---------------------------------------------------------------------- aggregate

def aggregate(out_dir, plan=None):
    plan = plan or load_plan(out_dir)
    runtime_expected = plan.get("runtimeInputSha256")
    expected = {}
    for worker in plan["workers"]:
        for group in worker["groups"]:
            for leaf in group["leaves"]:
                expected[leaf["fullName"]] = worker["worker"]

    totals = dict.fromkeys(("total", "passed", "failed", "skipped", "inconclusive"), 0)
    seen, problems, workers, not_passed = {}, [], [], []

    for worker in plan["workers"]:
        number = worker["worker"]
        paths = worker_paths(out_dir, plan["runId"], number)
        if not os.path.isfile(paths["statusPath"]):
            problems.append("worker-%d wrote no status file" % number)
            workers.append({"worker": number, "result": "RUN_INVALID",
                            "note": "no status file"})
            continue
        with open(paths["statusPath"], encoding="utf-8") as handle:
            status = json.load(handle)
        workers.append(status)

        if status.get("result") == "RUN_INVALID":
            problems.append("worker-%d RUN_INVALID: %s" % (number, status.get("note")))
        if status.get("compilerErrors"):
            problems.append("worker-%d logged compiler errors" % number)
        # Evidence is only merged when it carries this run's source hash. Never across
        # hashes: two trees are two programs, and adding their numbers together
        # produces a total for a suite that was never executed.
        if status.get("expectedSourceSha256") != plan["sourceSha256"]:
            problems.append("worker-%d ran a different source tree" % number)
            continue
        # And the same refusal on the other hash. Five workers whose C# agrees and
        # whose TMP atlases do not are five results about five different products;
        # summing them produces a total for a suite that was never executed, which
        # is the 581/564/0 failure with the stale half moved out of the code.
        if status.get("expectedRuntimeInputSha256") != runtime_expected:
            problems.append("worker-%d ran different runtime inputs" % number)
            continue

        entries, damaged = read_journal(paths["journalPath"])
        if damaged:
            problems.append("worker-%d journal has %d unreadable line(s)" % (number, damaged))
        proven = journal_leaves(entries, plan["sourceSha256"], runtime_expected)
        if not proven:
            problems.append("worker-%d produced no journal evidence" % number)
        for name, rows in proven.items():
            seen.setdefault(name, []).append(number)
            if len(rows) > 1:
                problems.append("worker-%d journalled %s %d times" % (number, name, len(rows)))
            for row in rows:
                if row.get("result") != "Passed":
                    not_passed.append({"worker": number, "fullName": name,
                                       "result": row.get("result"),
                                       "message": (row.get("message") or "")[:300]})

        if not os.path.isfile(paths["resultPath"]):
            problems.append("worker-%d produced no result XML" % number)
            continue
        if status.get("startEpoch") and os.path.getmtime(paths["resultPath"]) < status["startEpoch"]:
            problems.append("worker-%d result XML predates its run" % number)
            continue
        try:
            root = ET.parse(paths["resultPath"]).getroot()
        except Exception as error:
            problems.append("worker-%d result XML unparseable: %s" % (number, error))
            continue
        if root.get("nebula-source-sha256") and root.get("nebula-source-sha256") != plan["sourceSha256"]:
            problems.append("worker-%d result XML is from a different source tree" % number)
            continue
        xml_runtime = root.get("nebula-runtime-input-sha256")
        if xml_runtime and xml_runtime != runtime_expected:
            problems.append("worker-%d result XML is from different runtime inputs" % number)
            continue
        for key in totals:
            try:
                totals[key] += int(root.get(key) or 0)
            except ValueError:
                pass

    missing = sorted(name for name in expected if name not in seen)
    duplicate = sorted(name for name, owners in seen.items() if len(owners) > 1)
    unknown = sorted(name for name in seen if name not in expected)
    if missing:
        problems.append("%d leaf/leaves never ran: %s"
                        % (len(missing), ", ".join(missing[:10])))
    if duplicate:
        problems.append("%d leaf/leaves ran on more than one worker: %s"
                        % (len(duplicate), ", ".join(duplicate[:10])))
    if unknown:
        problems.append("%d leaf/leaves in the evidence are not in the plan: %s"
                        % (len(unknown), ", ".join(unknown[:10])))

    invalid = any(entry.get("result") == "RUN_INVALID" for entry in workers)
    if invalid or missing or duplicate or unknown or problems:
        verdict = "RUN INVALID"
    elif totals["failed"] or totals["inconclusive"] or totals["skipped"] or not_passed:
        verdict = "FAIL"
    elif len(seen) != len(expected):
        verdict = "RUN INVALID"
    else:
        verdict = "PASS"

    summary = {
        "verdict": verdict,
        "runId": plan["runId"],
        "lane": plan["lane"],
        "sourceSha256": plan["sourceSha256"],
        "runtimeInputSha256": runtime_expected,
        "generated": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "leavesExpected": len(expected),
        "leavesSeen": len(seen),
        "leavesMissing": missing,
        "leavesDuplicated": duplicate,
        "leavesUnknown": unknown,
        "uncategorised": plan.get("uncategorised", []),
        "totals": totals,
        "notPassed": not_passed,
        "problems": problems,
        "workers": [{"worker": entry.get("worker"), "result": entry.get("result"),
                     "note": entry.get("note"), "wallSeconds": entry.get("wallSeconds"),
                     "journalProvenLeaves": entry.get("journalProvenLeaves"),
                     "expectedLeaves": entry.get("expectedLeaves")}
                    for entry in workers],
    }
    with open(os.path.join(out_dir, "summary.json"), "w", encoding="utf-8") as handle:
        json.dump(summary, handle, indent=2, sort_keys=True)
    return summary


def record_durations(out_dir, durations_path=DURATIONS_DEFAULT, plan=None):
    """Fold this run's per-leaf timings into the cache the next plan balances on.

    Journal durations only. A fixture-level duration from an old result XML is the
    sum of an unknown number of leaves, and dividing it by a leaf count is how the
    old planner convinced itself a 31-test fixture cost 102 minutes.
    """
    plan = plan or load_plan(out_dir)
    cache = {"schema": "nebula-leaf-durations/1", "leaves": {}}
    if os.path.isfile(durations_path):
        try:
            with open(durations_path, encoding="utf-8") as handle:
                existing = json.load(handle)
            if isinstance(existing.get("leaves"), dict):
                cache["leaves"] = existing["leaves"]
        except Exception:
            pass

    updated = 0
    for worker in plan["workers"]:
        paths = worker_paths(out_dir, plan["runId"], worker["worker"])
        entries, _ = read_journal(paths["journalPath"])
        for name, rows in journal_leaves(entries, plan["sourceSha256"],
                                         plan.get("runtimeInputSha256")).items():
            seconds = max(float(row.get("duration") or 0.0) for row in rows)
            cache["leaves"][name] = {"seconds": round(seconds, 3),
                                     "updated": time.time(),
                                     "runId": plan["runId"]}
            updated += 1
    cache["updated"] = time.time()
    os.makedirs(os.path.dirname(os.path.abspath(durations_path)), exist_ok=True)
    with open(durations_path, "w", encoding="utf-8") as handle:
        json.dump(cache, handle, indent=2, sort_keys=True)
    return updated


# --------------------------------------------------------------------------- main

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)

    repo_default = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

    p = sub.add_parser("hash", help="the source hash the manifest must be stamped with")
    p.add_argument("--repo", default=repo_default)

    p = sub.add_parser("runtime-hash",
                       help="the runtime input hash the manifest must also be stamped "
                            "with: scenes, prefabs, TMP assets, sprites, materials, "
                            "shaders, ProjectSettings and the package manifest")
    p.add_argument("--repo", default=repo_default)
    p.add_argument("--explain", action="store_true",
                   help="print what the hash covers -- file count, bytes and the "
                        "per-root breakdown -- so coverage is auditable rather than "
                        "asserted")

    p = sub.add_parser("plan", help="classify, balance and refuse")
    p.add_argument("--manifest", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--repo", default=repo_default)
    p.add_argument("--workers", type=int, default=5)
    p.add_argument("--lane", default=DEFAULT_LANE)
    p.add_argument("--durations", default=DURATIONS_DEFAULT)
    p.add_argument("--exemptions", default=EXEMPTIONS_DEFAULT,
                   help="named, capped, justified exceptions to the %.0fs release-leaf "
                        "ceiling. Every applied exemption is printed with its reason."
                        % RELEASE_LEAF_MAX_SECONDS)
    p.add_argument("--expect-source-hash", default=None)
    p.add_argument("--expect-runtime-hash", default=None)
    p.add_argument("--graphics", action="store_true", default=None)

    p = sub.add_parser("run", help="run one worker and validate its evidence")
    p.add_argument("--out", required=True)
    p.add_argument("--worker", type=int, required=True)
    p.add_argument("--watchdog", type=float, default=2700.0)
    p.add_argument("--repo", default=None,
                   help="the tree Unity opens, when it is not the one named in the "
                        "plan -- i.e. this worker's APFS mirror. Results, journals, "
                        "status and every validation stay in the shared --out. The "
                        "mirror must hash to the plan's sourceSha256 or the worker "
                        "refuses before launching Unity.")

    p = sub.add_parser("resume", help="what one worker still owes")
    p.add_argument("--out", required=True)
    p.add_argument("--worker", type=int, required=True)

    p = sub.add_parser("aggregate", help="one verdict over every worker")
    p.add_argument("--out", required=True)
    p.add_argument("--record-durations", action="store_true")
    p.add_argument("--durations", default=DURATIONS_DEFAULT)

    args = parser.parse_args(argv)

    try:
        if args.command == "hash":
            print(source_hash(args.repo))
            return 0

        if args.command == "runtime-hash":
            digests = runtime_input_digests(args.repo)
            print(runtime_input_hash(args.repo, digests))
            if args.explain:
                by_root, total = {}, 0
                for relative in digests:
                    head = relative.split("/", 1)[0]
                    by_root[head] = by_root.get(head, 0) + 1
                    try:
                        total += os.path.getsize(os.path.join(args.repo, relative))
                    except OSError:
                        pass
                print("  %d files, %.1f MiB" % (len(digests), total / 1048576.0))
                for head in sorted(by_root):
                    print("    %-20s %5d files" % (head, by_root[head]))
                print("  excluded by rule: dirs=%s" % ", ".join(sorted(RUNTIME_INPUT_SKIP_DIRS)))
                print("                    suffixes=%s" % ", ".join(RUNTIME_INPUT_SKIP_SUFFIXES))
                print("                    names=%s" % ", ".join(sorted(RUNTIME_INPUT_SKIP_NAMES)))
                print("                    %s" % INIT_TEST_SCENE.pattern)
                print("  excluded by not being under %s: capture PNGs, result XML, logs, "
                      "journals" % ", ".join(RUNTIME_INPUT_ROOTS))
            return 0

        if args.command == "plan":
            plan = build_plan(args.manifest, args.out, args.workers, args.repo,
                              lane=args.lane, durations_path=args.durations,
                              expect_source_hash=args.expect_source_hash,
                              expect_runtime_hash=args.expect_runtime_hash,
                              graphics=args.graphics, exemptions_path=args.exemptions)
            print("PLAN %s lane=%s leaves=%d workers=%d source=%s runtime=%s (%d files)"
                  % (plan["runId"], plan["lane"], plan["leafCount"],
                     plan["workerCount"], plan["sourceSha256"][:16],
                     plan["runtimeInputSha256"][:16], plan["runtimeInputFileCount"]))
            # Fixtures per worker is printed unconditionally, not only when
            # co-location is on. It is the number that makes a split visible, and a
            # count nobody sees on the runs that are fine is a count nobody reads on
            # the run that is not.
            print("  fixtures=%d  coLocateFixtures=%s"
                  % (plan["fixtureCount"], plan["coLocateFixtures"]))
            for worker in plan["workers"]:
                print("  worker %d: %4d leaves, %3d fixture(s), %6.0fs estimated (%d measured)"
                      % (worker["worker"], worker["leafCount"], worker["fixtureCount"],
                         worker["estimatedSeconds"], worker["measuredLeaves"]))
                if plan["coLocateFixtures"]:
                    for fixture in worker["fixtures"]:
                        print("      %s" % fixture)
            if plan["uncategorised"]:
                print("  UNCATEGORISED (defaulted into %s): %d"
                      % (plan["lane"], plan["uncategorisedCount"]))
                for name in plan["uncategorised"][:20]:
                    print("    " + name)
            # Printed unconditionally and before anything else that is merely
            # informational. A ceiling with an exemption nobody reads is a ceiling
            # that is not there, and this is the line that has to make a granted
            # exception impossible to miss in a scrollback.
            for state, heading in (("applied", "CEILING EXEMPTIONS APPLIED"),
                                   ("stale", "STALE EXEMPTIONS -- delete these"),
                                   ("unverified", "EXEMPTIONS NOT YET MEASURABLE")):
                rows = [row for row in plan["exemptionAudit"] if row["state"] == state]
                if not rows:
                    continue
                print("  %s: %d" % (heading, len(rows)))
                for row in rows:
                    print("    %s" % row["fullName"])
                    print("      %s (cap %.0fs, base ceiling %.0fs)"
                          % (row["note"], row["capSeconds"], RELEASE_LEAF_MAX_SECONDS))
                    print("      reason: %s" % row["reason"])

            taken = [row for row in plan["laneConflicts"] if not row["inThisPlan"]]
            if plan["laneConflicts"]:
                print("  MULTI-LANE LEAVES touching %s (precedence %s): %d, of which "
                      "%d routed elsewhere"
                      % (plan["lane"], " > ".join(LANE_PRECEDENCE),
                         len(plan["laneConflicts"]), len(taken)))
                for row in plan["laneConflicts"][:20]:
                    print("    %-64s %s -> %s%s"
                          % (row["fullName"][-64:], "+".join(row["carries"]), row["lane"],
                             "" if row["inThisPlan"] else "   (NOT IN THIS PLAN)"))
            return 0

        if args.command == "run":
            verdict, status = run_worker(args.out, args.worker, watchdog=args.watchdog,
                                         repo=args.repo)
            print("worker-%d %s %s" % (args.worker, verdict, status.get("note") or ""))
            if status.get("repoIsMirror"):
                print("  ran in mirror %s" % status["repo"])
            return 0 if verdict == "PASS" else 1

        if args.command == "resume":
            print(json.dumps(resume_plan(args.out, args.worker), indent=2))
            return 0

        if args.command == "aggregate":
            summary = aggregate(args.out)
            if args.record_durations:
                summary["durationsRecorded"] = record_durations(
                    args.out, durations_path=args.durations)
            print("FULL PLAYMODE GATE: %s" % summary["verdict"])
            print("  lane=%s source=%s runtime=%s"
                  % (summary["lane"], (summary["sourceSha256"] or "")[:16],
                     (summary["runtimeInputSha256"] or "<none>")[:16]))
            print("  leaves %d/%d  missing=%d duplicate=%d unknown=%d"
                  % (summary["leavesSeen"], summary["leavesExpected"],
                     len(summary["leavesMissing"]), len(summary["leavesDuplicated"]),
                     len(summary["leavesUnknown"])))
            print("  total=%(total)d passed=%(passed)d failed=%(failed)d "
                  "skipped=%(skipped)d inconclusive=%(inconclusive)d" % summary["totals"])
            for entry in summary["workers"]:
                print("  worker-%s %-11s %s/%s leaves proven  %s"
                      % (entry["worker"], entry["result"], entry["journalProvenLeaves"],
                         entry["expectedLeaves"], entry["note"] or ""))
            for entry in summary["notPassed"]:
                print("  NOT PASSED [%s] %s -- %s"
                      % (entry["result"], entry["fullName"], entry["message"][:160]))
            for problem in summary["problems"]:
                print("  PROBLEM: %s" % problem)
            return 0 if summary["verdict"] == "PASS" else 1
    except Refusal as refusal:
        print("GATE REFUSES TO START")
        for reason in refusal.reasons:
            print("  " + reason)
        return 2

    return 2


if __name__ == "__main__":
    sys.exit(main())
