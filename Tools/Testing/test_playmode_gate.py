#!/usr/bin/env python3
"""Self-tests for playmode_gate.py. Pure Python, no Unity, about a second.

    python3 Tools/Testing/test_playmode_gate.py

Every test here exists to prove that one specific guard FIRES. That is a different
thing from proving the gate works, and it is the property this project has been
burned by not having: a screenshot suite once shipped seventeen blank PNGs and three
tests that asserted nothing, all green. So each test below is written so that
deleting the guard it names makes it fail, and each says which guard that is. If you
are tempted to relax one, delete the guard first and watch the test go red -- if it
does not, the test is decoration and should be rewritten, not kept.

Unity is never launched. `run_worker` takes its launcher as an argument for exactly
this reason: the launcher's whole contract is "some process ran and left files
behind", and a Python function can leave those files behind in a millisecond,
including the malformed and stale ones a real Unity only produces when something has
already gone wrong.
"""
import json
import os
import shutil
import sys
import tempfile
import time
import traceback

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import playmode_gate as gate  # noqa: E402


# ------------------------------------------------------------------ scaffolding

ASSEMBLY = gate.KNOWN_ASSEMBLIES[0]

# The checkout the few tests that assert against the REAL tree should look at.
# Overridable so those tests can still be run against this project while the gate
# module itself is a mutated copy somewhere else -- which is how every guard here
# is verified to fire: copy, break one line, watch the named test go red.
REPO = os.environ.get(
    "NEBULA_GATE_REPO",
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))


def leaf(name, fixture="Nebula.Tests.PlayMode.FooTests", categories=None,
         selection=None, unique=None, run_state="Runnable", assembly=ASSEMBLY,
         child_index=-1):
    full = "%s.%s" % (fixture, name)
    return {
        "fullName": full,
        "selectionName": selection or full,
        "uniqueName": unique or ("%s.dll/%s/[%s][%s]" % (assembly, assembly, assembly, full)),
        "childIndex": child_index,
        "fixture": fixture,
        "fixtureName": fixture.rsplit(".", 1)[-1],
        "assembly": assembly,
        "categories": list(["ReleaseRegression"] if categories is None else categories),
        "runState": run_state,
        "testMode": "PlayMode",
        "skipReason": None,
        "source": "Assets/Tests/PlayMode/%s.cs" % fixture.rsplit(".", 1)[-1],
    }


# The runtime inputs a scaffolded tree carries. One file per rule the hash has to
# obey, so a change to the walk shows up as a named test rather than as a digest
# that moved. `write_tree` is what both the scaffold and the mirrors go through, so
# a mirror is a mirror of ALL of it and not only of the C#.
RUNTIME_TREE = {
    "Assets/Tests/PlayMode/FooTests.cs":
        "// fixture source, present so the tree has something to hash\n",
    "Assets/Tests/PlayMode/Nebula.Tests.PlayMode.asmdef": '{"name":"Nebula.Tests.PlayMode"}\n',
    "Assets/Scenes/Nebula.unity": "%YAML 1.1\n# the scene the fixtures load\n",
    "Assets/Scenes/Nebula.unity.meta": "guid: 1111111111111111\n",
    "Assets/TextMesh Pro/Resources/TMP Settings.asset": "TMP Settings v1\n",
    "Assets/TextMesh Pro/Resources/Fonts & Materials/Nebula SDF.asset": "font atlas v1\n",
    "Assets/TextMesh Pro/Resources/Fonts & Materials/Nebula SDF.asset.meta":
        "guid: 2222222222222222\n",
    "Assets/Resources/UI/Sprites/button.png": "PNG-the-product-ships\n",
    "Assets/Resources/UI/Sprites/button.png.meta": "textureType: 8\n",
    "Assets/Resources/UI/Materials/panel.mat": "material v1\n",
    "ProjectSettings/ProjectSettings.asset": "productName: Nebula\n",
    "ProjectSettings/QualitySettings.asset": "pixelLightCount: 1\n",
    "ProjectSettings/ProjectVersion.txt": "m_EditorVersion: 2022.3.22f1\n",
    "Packages/manifest.json": '{"dependencies":{"com.unity.testtools":"1.3.9"}}\n',
    "Packages/packages-lock.json": '{"dependencies":{}}\n',
}

# Written into every scaffolded tree, and every one of them must be invisible to the
# hash. The first two are Unity's and this gate's own output; the last is the scene
# Unity's PlayMode launcher creates inside Assets while a run is in flight.
GENERATED_TREE = {
    "UIDesign/Runtime/Part4/capture-01.png": "PNG-written-by-the-test-being-gated\n",
    "UIDesign/Review/results-worker-1.xml": "<test-run total='1' />\n",
    "Logs/worker-1.log": "Batchmode quit successfully\n",
    "TestResults/full.xml": "<test-run total='1' />\n",
    "Assets/InitTestScene639222486175928850.unity": "%YAML 1.1\n# Unity made this\n",
    "Assets/InitTestScene639222486175928850.unity.meta": "guid: 3333333333333333\n",
    "Assets/Resources/UI/Sprites/.DS_Store": "finder was here\n",
    "Assets/Nebula.Editor.csproj": "<Project />\n",
}


def write_tree(root, files):
    for relative, content in files.items():
        path = os.path.join(root, relative.replace("/", os.sep))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w") as handle:
            handle.write(content)
    return root


def poke(root, relative, content):
    """Change one file in a tree. Returns the path, so a test can read it back."""
    path = os.path.join(root, relative.replace("/", os.sep))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as handle:
        handle.write(content)
    return path


def scaffold(root, leaves, source_sha=None, durations=None, runtime_sha=None):
    """A minimal tree the gate can hash, plus a manifest stamped to match it."""
    write_tree(root, RUNTIME_TREE)
    write_tree(root, GENERATED_TREE)

    real = gate.source_hash(root)
    real_runtime = gate.runtime_input_hash(root)
    manifest_path = os.path.join(root, "leaf-manifest.json")
    with open(manifest_path, "w") as handle:
        json.dump({
            "schema": "nebula-leaf-manifest/2",
            "generatedUtc": "2026-08-13T00:00:00Z",
            "unityVersion": "2022.3.22f1",
            "projectPath": root,
            "sourceSha256": real if source_sha is None else source_sha,
            "runtimeInputSha256": real_runtime if runtime_sha is None else runtime_sha,
            "modes": ["PlayMode"],
            "leafCount": len(leaves),
            "leaves": leaves,
        }, handle)

    durations_path = os.path.join(root, "durations.json")
    with open(durations_path, "w") as handle:
        json.dump({"schema": "nebula-leaf-durations/1", "leaves": durations or {}}, handle)
    return manifest_path, durations_path, real


def plan_for(root, leaves, workers=2, lane=gate.DEFAULT_LANE, durations=None,
             graphics=None, source_sha=None, runtime_sha=None):
    manifest_path, durations_path, _ = scaffold(root, leaves, source_sha, durations,
                                                runtime_sha)
    out = os.path.join(root, "out")
    return gate.build_plan(manifest_path, out, workers, root, lane=lane,
                           durations_path=durations_path, graphics=graphics), out


def xml_document(cases, source_sha, failed=0, skipped=0, inconclusive=0,
                 runtime_sha=None):
    body = ["<test-run id='2' total='%d' passed='%d' failed='%d' skipped='%d' "
            "inconclusive='%d' nebula-source-sha256='%s' "
            "nebula-runtime-input-sha256='%s'>"
            % (len(cases), len(cases) - failed - skipped - inconclusive, failed,
               skipped, inconclusive, source_sha, runtime_sha or "")]
    for name in cases:
        body.append("<test-case fullname='%s' result='Passed' />" % name)
    body.append("</test-run>")
    return "".join(body)


def journal_lines(entries):
    return "".join(json.dumps(entry) + "\n" for entry in entries)


def leaf_line(name, source_sha, worker=1, result="Passed", duration=1.5, sequence=0,
              runtime_sha=None):
    return {"kind": "leaf", "worker": worker, "runId": "r", "sourceSha256": source_sha,
            "runtimeInputSha256": runtime_sha,
            "pid": 1234, "sequence": sequence, "fullName": name, "uniqueName": name,
            "selectionName": name, "start": "2026-08-13T00:00:00.0000000Z",
            "end": "2026-08-13T00:00:01.0000000Z", "duration": duration,
            "result": result, "resultState": result, "message": None}


def run_cli(argv):
    """gate.main() with stdout captured.

    Some of what this gate produces is only correct if it reaches a terminal -- an
    exemption recorded in plan.json and not printed is an exception nobody reviews.
    Asserting on the JSON alone would let that regress silently.
    """
    import contextlib
    import io
    buffer = io.StringIO()
    with contextlib.redirect_stdout(buffer):
        gate.main(argv)
    return buffer.getvalue()


def faker(write):
    """A launcher that does whatever `write` says and reports a clean exit."""
    def launch(command, watchdog):
        write()
        return 0, False, 4242
    return launch


def complete_worker(out, plan, number, source_sha, **override):
    """A launcher that leaves behind exactly what a healthy worker leaves behind."""
    paths = gate.worker_paths(out, plan["runId"], number)
    worker = gate.worker_entry(plan, number)
    names = [row["fullName"] for group in worker["groups"] for row in group["leaves"]]
    names = override.get("names", names)
    sha = override.get("journalSha", source_sha)
    runtime = plan.get("runtimeInputSha256")
    journal_runtime = override.get("journalRuntimeSha", runtime)

    def write():
        os.makedirs(os.path.dirname(paths["resultPath"]), exist_ok=True)
        os.makedirs(os.path.dirname(paths["journalPath"]), exist_ok=True)
        os.makedirs(os.path.dirname(paths["logPath"]), exist_ok=True)
        with open(paths["logPath"], "w") as handle:
            handle.write(override.get("log", "Batchmode quit successfully\n"))
        with open(paths["resultPath"], "w") as handle:
            handle.write(override.get(
                "xml", xml_document(names, source_sha, runtime_sha=runtime)))
        with open(paths["journalPath"], "w") as handle:
            handle.write(journal_lines(
                [leaf_line(name, sha, number, sequence=i, runtime_sha=journal_runtime)
                 for i, name in enumerate(names)]))
    return faker(write)


# ------------------------------------------------------------------------ tests

CASES = []


def case(function):
    CASES.append(function)
    return function


def refusal(callable_, *args, **kwargs):
    try:
        callable_(*args, **kwargs)
    except gate.Refusal as error:
        return error.reasons
    raise AssertionError("expected the gate to refuse, it did not")


@case
def plan_refuses_duplicate_fullname(root):
    """Pins check_plan's `duplicate FullName across the plan` branch.

    Two leaves with one name cannot both be accounted for: the aggregate keys
    evidence by fullName, so the second one is invisible and its failure is a pass.
    """
    rows = [leaf("Alpha"), leaf("Alpha", unique="other"), leaf("Beta")]
    reasons = refusal(plan_for, root, rows)
    assert any("duplicate FullName" in reason for reason in reasons), reasons


@case
def plan_refuses_duplicate_uniquename(root):
    """Pins check_plan's `duplicate UniqueName` branch.

    UniqueName is the framework's own stable identity. Two leaves sharing one means
    the manifest was built from a tree the framework would not agree with, and any
    resume keyed on it re-runs or skips the wrong test.
    """
    rows = [leaf("Alpha", unique="same"), leaf("Beta", unique="same")]
    reasons = refusal(plan_for, root, rows)
    assert any("duplicate UniqueName" in reason for reason in reasons), reasons


@case
def plan_refuses_soak_leaf_in_release_lane(root):
    """Pins check_plan's `is Soak/Diagnostic but planned into release lane` branch.

    Reached through a plan.json on disk rather than through build_plan: lane_of
    classifies a Soak leaf out of the release lane, so the only way this lands in a
    release worker is a plan that was edited, resumed or produced elsewhere -- which
    is exactly why run_worker re-checks the plan it reads instead of trusting it.
    """
    plan, out = plan_for(root, [leaf("Alpha"), leaf("Beta")])
    plan["workers"][0]["groups"][0]["leaves"][0]["categories"] = ["Soak"]
    reasons = gate.check_plan(plan, {})
    assert any("Soak/Diagnostic but planned into release lane" in r for r in reasons), reasons

    with open(os.path.join(out, "plan.json"), "w") as handle:
        json.dump(plan, handle)
    verdict, status = gate.run_worker(out, 1, launch=faker(lambda: None))
    assert verdict == "RUN_INVALID", (verdict, status)
    assert "does not pass its own checks" in status["note"], status["note"]


@case
def plan_refuses_graphics_leaf_on_headless_worker(root):
    """Pins check_plan's `is Graphics but planned onto headless worker` branch.

    A Graphics leaf on -nographics does not fail, which is the problem: it passes
    without rendering anything, and the pass is then cited as evidence the rendering
    works. Reached through build_plan by asking for the Graphics lane headless.
    """
    rows = [leaf("Alpha", categories=["Graphics"]), leaf("Beta", categories=["Graphics"])]
    reasons = refusal(plan_for, root, rows, lane="Graphics", graphics=False)
    assert any("Graphics but planned onto headless worker" in r for r in reasons), reasons


@case
def graphics_outranks_every_other_lane_category(root):
    """Pins LANE_PRECEDENCE having Graphics first.

    NUnit propagates a class-level category to its methods, so leaves carrying two
    lane names are normal, not exotic. The tie-break has to be Graphics because the
    lanes are not symmetric: landing in the wrong gating lane is a visible reporting
    error, while landing on a headless worker is invisible -- a Graphics leaf on
    -nographics passes without rendering, and that pass gets cited as proof the
    rendering works.
    """
    for other in ("ReleaseRegression", "Soak", "Diagnostic"):
        both = leaf("Alpha", categories=[other, "Graphics"])
        assert gate.lane_of(both) == "Graphics", (other, gate.lane_of(both))
        # Order within the attribute list must not matter either.
        flipped = leaf("Alpha", categories=["Graphics", other])
        assert gate.lane_of(flipped) == "Graphics", (other, gate.lane_of(flipped))

    # And the consequence that matters: such a leaf is absent from the headless
    # release plan entirely, rather than present and refused. Move Graphics after
    # Soak in LANE_PRECEDENCE and the Soak assertion above goes red; move it after
    # ReleaseRegression and this plan picks the leaf up and then refuses.
    rows = [leaf("Alpha", categories=["ReleaseRegression", "Graphics"]),
            leaf("Beta"), leaf("Gamma")]
    plan, _ = plan_for(root, rows, workers=2)
    placed = [row["fullName"] for worker in plan["workers"] for group in worker["groups"]
              for row in group["leaves"]]
    assert rows[0]["fullName"] not in placed, placed
    assert sorted(placed) == sorted([rows[1]["fullName"], rows[2]["fullName"]]), placed

    # The move is reported, not silent, and reported to the lane that LOST it --
    # that is the plan whose completeness the operator would otherwise misread.
    assert [row["fullName"] for row in plan["laneConflicts"]] == [rows[0]["fullName"]], plan
    assert plan["laneConflicts"][0]["lane"] == "Graphics", plan["laneConflicts"]
    assert plan["laneConflicts"][0]["inThisPlan"] is False, plan["laneConflicts"]


@case
def a_soak_leaf_that_also_needs_a_device_does_not_take_down_the_soak_plan(root):
    """The regression this precedence change fixes.

    With Graphics ranked below Soak, a Soak+Graphics leaf classified into the
    headless Soak lane and check_plan then refused the WHOLE soak plan over one
    leaf -- a mislabelled test became an outage for the lane. Graphics-first makes
    the property hold by construction, so the rest of the soak lane still plans.
    """
    rows = [leaf("Slow1", categories=["Soak"]),
            leaf("Slow2", categories=["Soak"]),
            leaf("SlowAndVisual", categories=["Soak", "Graphics"])]
    plan, _ = plan_for(root, rows, workers=2, lane="Soak")
    placed = [row["fullName"] for worker in plan["workers"] for group in worker["groups"]
              for row in group["leaves"]]
    assert sorted(placed) == sorted([rows[0]["fullName"], rows[1]["fullName"]]), placed
    # The soak plan says so rather than looking complete: the leaf it lost is named.
    assert [row["fullName"] for row in plan["laneConflicts"]] == [rows[2]["fullName"]], plan
    assert plan["laneConflicts"][0]["inThisPlan"] is False, plan["laneConflicts"]

    # It is not lost -- it is in the Graphics lane, where a device exists.
    graphics, _ = plan_for(os.path.join(root, "gfx"), rows, workers=1, lane="Graphics")
    assert [row["fullName"] for worker in graphics["workers"]
            for group in worker["groups"] for row in group["leaves"]] == [rows[2]["fullName"]]
    assert graphics["workers"][0]["graphics"] is True, graphics["workers"][0]
    assert graphics["laneConflicts"][0]["inThisPlan"] is True, graphics["laneConflicts"]


@case
def a_lane_is_not_told_about_conflicts_that_never_concerned_it(root):
    """Pins lane_conflicts' `lane in carried` filter.

    The first version listed every multi-lane leaf in the manifest on every plan, so
    the Diagnostic plan printed rows about Graphics+Soak leaves that were never
    candidates for it. This report is the only place a leaf leaving your lane is
    visible, so it has to be short enough to actually be read.
    """
    rows = [leaf("Diag1", categories=["Diagnostic"]),
            leaf("Diag2", categories=["Diagnostic"]),
            leaf("Plain", categories=["ReleaseRegression"]),
            leaf("Visual", categories=["ReleaseRegression", "Graphics"])]
    plan, _ = plan_for(root, rows, workers=2, lane="Diagnostic")
    assert plan["laneConflicts"] == [], plan["laneConflicts"]
    # The lane it was taken from does hear about it.
    release, _ = plan_for(os.path.join(root, "rel"), rows, workers=1,
                          lane="ReleaseRegression")
    assert [row["fullName"] for row in release["laneConflicts"]] == [rows[3]["fullName"]]


@case
def a_lane_emptied_by_precedence_refuses_rather_than_planning_nothing(root):
    """Pins build_plan's `no leaves classify into lane` branch.

    Found by writing the test above with data where every ReleaseRegression leaf
    also carried Graphics. Precedence emptied the release lane completely, and the
    gate refused instead of producing a plan for zero leaves -- which is right, and
    is also the shape of a real mistake: label a whole fixture Graphics and its
    regression coverage silently stops being gated. An empty plan would have
    aggregated to a clean 0/0 PASS.
    """
    rows = [leaf("Visual1", categories=["ReleaseRegression", "Graphics"]),
            leaf("Visual2", categories=["ReleaseRegression", "Graphics"])]
    reasons = refusal(plan_for, root, rows, lane="ReleaseRegression")
    assert any("no leaves classify into lane ReleaseRegression" in r for r in reasons), reasons


@case
def plan_refuses_slow_release_leaf(root):
    """Pins check_plan's RELEASE_LEAF_MAX_SECONDS branch.

    Only a MEASURED duration trips it. An unmeasured leaf gets the conservative
    budget and must not be refused for a number nobody has observed.
    """
    rows = [leaf("Alpha"), leaf("Beta")]
    durations = {rows[0]["fullName"]: {"seconds": gate.RELEASE_LEAF_MAX_SECONDS + 1}}
    reasons = refusal(plan_for, root, rows, durations=durations)
    assert any("over the 30s ceiling" in reason for reason in reasons), reasons

    fresh = os.path.join(root, "unmeasured")
    os.makedirs(fresh)
    plan, _ = plan_for(fresh, [leaf("Alpha"), leaf("Beta")])
    assert plan["leafCount"] == 2


def exemption_file(root, entries, schema="nebula-ceiling-exemptions/1"):
    path = os.path.join(root, "exemptions.json")
    with open(path, "w") as handle:
        json.dump({"schema": schema, "exemptions": entries}, handle)
    return path


def plan_with_exemptions(root, rows, entries, durations=None, **kwargs):
    manifest_path, durations_path, _ = scaffold(root, rows, None, durations)
    return gate.build_plan(manifest_path, os.path.join(root, "out"), 1, root,
                           durations_path=durations_path,
                           exemptions_path=exemption_file(root, entries), **kwargs)


@case
def an_exemption_without_a_reason_is_refused(root):
    """Pins load_exemptions' mandatory-reason branch.

    The value of a ceiling is not that it is never crossed -- it is that crossing it
    is a decision somebody made and signed. An exemption with no stated reason is the
    ceiling deleted for one test, with no trace of who deleted it or why, and it is
    indistinguishable a year later from a line that was always there.
    """
    rows = [leaf("Alpha"), leaf("Beta")]
    slow = {rows[0]["fullName"]: {"seconds": 54.3}}
    for bad in ({"fullName": rows[0]["fullName"], "capSeconds": 75.0},
                {"fullName": rows[0]["fullName"], "capSeconds": 75.0, "reason": "   "}):
        reasons = refusal(plan_with_exemptions, root, rows, [bad], durations=slow)
        assert any("has no reason" in reason for reason in reasons), reasons


@case
def an_exemption_without_a_cap_is_refused(root):
    """Pins load_exemptions' mandatory-capSeconds branch: an exemption has to commit
    to a number, or it is a waiver with no expiry."""
    rows = [leaf("Alpha"), leaf("Beta")]
    slow = {rows[0]["fullName"]: {"seconds": 54.3}}
    reasons = refusal(plan_with_exemptions, root, rows,
                      [{"fullName": rows[0]["fullName"], "reason": "because"}],
                      durations=slow)
    assert any("no usable capSeconds" in reason for reason in reasons), reasons


@case
def an_exempted_leaf_over_its_own_cap_is_still_refused(root):
    """Pins check_plan's `the exemption does not cover this` branch.

    This is what makes an exemption a commitment rather than a waiver. Grant one
    without enforcing its cap and it becomes an unbounded hole that widens by itself:
    the test that was 54s at the time of writing is 200s three years later and
    nothing ever said so.
    """
    rows = [leaf("Alpha"), leaf("Beta")]
    entry = {"fullName": rows[0]["fullName"], "capSeconds": 75.0,
             "reason": "34 real product transitions, not an endurance ladder"}

    within = plan_with_exemptions(root, rows, [entry],
                                  durations={rows[0]["fullName"]: {"seconds": 54.3}})
    assert within["leafCount"] == 2, within
    assert [row["state"] for row in within["exemptionAudit"]] == ["applied"], within

    over = os.path.join(root, "over")
    reasons = refusal(plan_with_exemptions, over, rows, [entry],
                      durations={rows[0]["fullName"]: {"seconds": 90.0}})
    assert any("exemption does not cover this" in reason for reason in reasons), reasons
    assert not any("over the 30s ceiling" in reason for reason in reasons), reasons


@case
def an_applied_exemption_is_reported_with_its_reason(root):
    """Pins the exemptionAudit record and the `plan` output that prints it.

    An exemption nobody sees is the same as no ceiling. The reason has to reach the
    terminal, not just the JSON, because the terminal is what gets pasted into a
    report when someone asks why a release test takes a minute.
    """
    rows = [leaf("Alpha"), leaf("Beta")]
    reason = "34 real product transitions, not an endurance ladder"
    plan = plan_with_exemptions(root, rows,
                                [{"fullName": rows[0]["fullName"], "capSeconds": 75.0,
                                  "reason": reason}],
                                durations={rows[0]["fullName"]: {"seconds": 54.3}})
    applied = [row for row in plan["exemptionAudit"] if row["state"] == "applied"]
    assert len(applied) == 1, plan["exemptionAudit"]
    assert applied[0]["reason"] == reason, applied
    assert applied[0]["measuredSeconds"] == 54.3, applied

    printed = run_cli(["plan", "--manifest", os.path.join(root, "leaf-manifest.json"),
                       "--out", os.path.join(root, "cli-out"), "--repo", root,
                       "--workers", "1", "--durations", os.path.join(root, "durations.json"),
                       "--exemptions", os.path.join(root, "exemptions.json")])
    assert "CEILING EXEMPTIONS APPLIED: 1" in printed, printed
    assert reason in printed, printed
    assert rows[0]["fullName"] in printed, printed


@case
def an_exemption_that_is_no_longer_needed_is_reported_stale(root):
    """Pins audit_exemptions' stale branches, so the list cannot rot.

    Four ways an entry stops meaning anything: the leaf got faster, the leaf moved to
    a lane with no release ceiling, the leaf was renamed away, or the cap was written
    at or below the base ceiling so it could never have applied. All four leave a
    line that reads like a live exception and is not one.
    """
    rows = [leaf("Fast"), leaf("Moved", categories=["Soak"]), leaf("Other")]
    entries = [
        {"fullName": rows[0]["fullName"], "capSeconds": 75.0, "reason": "got faster"},
        {"fullName": rows[1]["fullName"], "capSeconds": 75.0, "reason": "moved lane"},
        {"fullName": "Nebula.Tests.PlayMode.FooTests.Renamed", "capSeconds": 75.0,
         "reason": "renamed away"},
        {"fullName": rows[2]["fullName"], "capSeconds": 10.0, "reason": "cap too low"},
    ]
    plan = plan_with_exemptions(root, rows, entries,
                                durations={rows[0]["fullName"]: {"seconds": 4.0},
                                           rows[2]["fullName"]: {"seconds": 4.0}})
    states = {row["fullName"]: row["state"] for row in plan["exemptionAudit"]}
    assert set(states.values()) == {"stale"}, plan["exemptionAudit"]
    notes = " ".join(row["note"] for row in plan["exemptionAudit"])
    for expected in ("inside the 30s ceiling", "Soak lane", "names no leaf",
                     "can never apply"):
        assert expected in notes, (expected, notes)


@case
def the_committed_exemption_file_is_valid_and_covers_the_leaf_it_names(root):
    """The real file, not a fixture. A malformed exemptions file refuses every plan,
    so it must never be possible to commit one that has not been parsed."""
    exemptions, refusals = gate.load_exemptions(gate.EXEMPTIONS_DEFAULT)
    assert refusals == [], refusals
    assert len(exemptions) == 1, sorted(exemptions)
    entry = exemptions[
        "Nebula.Tests.PlayMode.UiFinalLifecyclePerformanceTests."
        "SaveLoadRestartAndReopen_GrowNothingAndLeaveNoStaleSubscriber"]
    assert entry["capSeconds"] == 75.0, entry
    # The measured 54.3s is inside the cap, and the cap is above the base ceiling --
    # so the entry can actually apply, rather than being decorative.
    assert entry["capSeconds"] > gate.RELEASE_LEAF_MAX_SECONDS, entry
    assert entry["capSeconds"] > 54.3, entry
    assert len(entry["reason"]) > 80, "the reason has to explain, not label"


@case
def plan_refuses_heavy_fixture(root):
    """Pins check_plan's FIXTURE_TOTAL_MAX_SECONDS branch.

    This is the AuditPart2LifecyclePlayModeTests failure stated as a rule: a fixture
    heavier than the ceiling cannot be pinned to one worker without that worker
    setting the makespan on its own.
    """
    rows = [leaf("L%d" % i) for i in range(5)]
    durations = {row["fullName"]: {"seconds": 25.0} for row in rows}
    reasons = refusal(plan_for, root, rows, durations=durations)
    assert any("over the 120s ceiling" in reason for reason in reasons), reasons
    # Each leaf on its own is well inside the 30s release ceiling: what the gate
    # refuses here is the fixture as a unit, not any one test in it.
    assert not any("release leaf" in reason for reason in reasons), reasons


@case
def a_cold_duration_cache_does_not_refuse_the_whole_suite(root):
    """The counterweight to the fixture ceiling, and the reason it counts only
    measured seconds.

    Charging every unmeasured leaf the conservative unknown budget put any fixture
    with seven never-run leaves over the ceiling -- so on a cold cache, which is
    every first run and the only kind this gate has ever had, it refused everything
    and could never produce the measurements that would let it start. Delete the
    `origin == "measured"` condition on the fixture total and this goes red.
    """
    rows = [leaf("L%d" % i) for i in range(12)]
    plan, _ = plan_for(root, rows, workers=3, durations={})
    assert plan["leafCount"] == 12, plan
    assert all(worker["measuredLeaves"] == 0 for worker in plan["workers"]), plan
    # Balancing still uses the conservative budget: guessing is right for placement
    # and wrong for refusal, and those are two different numbers.
    assert all(worker["estimatedSeconds"] > 0 for worker in plan["workers"]), plan


@case
def plan_refuses_unknown_assembly(root):
    """Pins check_plan's `is in unknown assembly` branch.

    Filter.assemblyNames is passed to the worker; a leaf from an assembly the worker
    does not name is selected by nothing and shows up as a missing leaf with no
    explanation.
    """
    rows = [leaf("Alpha"), leaf("Beta", assembly="Some.Other.Tests")]
    reasons = refusal(plan_for, root, rows)
    assert any("unknown assembly" in reason for reason in reasons), reasons


@case
def plan_refuses_non_runnable_release_leaf(root):
    """Pins check_plan's `is X, not Runnable` branch.

    An Ignored or NotRunnable leaf in the release lane is counted as expected and
    never produces evidence, so the gate would report it as a lost leaf forever. It
    has to be labelled out of the lane, not silently tolerated.
    """
    rows = [leaf("Alpha"), leaf("Beta", run_state="Ignored")]
    reasons = refusal(plan_for, root, rows)
    assert any("not Runnable" in reason for reason in reasons), reasons


@case
def plan_refuses_leaf_name_that_reads_as_an_exclusion(root):
    """Pins check_plan's leading-'!' branch.

    RuntimeTestRunnerFilter.AddFilters treats a name starting with '!' as an
    exclusion, so one such leaf subtracts itself AND everything else from the run --
    the worker then runs almost nothing and reports it as a clean pass.
    """
    rows = [leaf("Alpha"), leaf("Beta")]
    rows[1]["fullName"] = "!" + rows[1]["fullName"]
    rows[1]["selectionName"] = rows[1]["fullName"]
    reasons = refusal(plan_for, root, rows)
    assert any("reads as an exclusion" in reason for reason in reasons), reasons


@case
def plan_refuses_manifest_from_another_tree(root):
    """Pins build_plan's `manifest source hash does not match` branch.

    A manifest is a list of what exists. Planning today's run from yesterday's list
    is how a leaf that was deleted becomes permanently "missing" and a leaf that was
    added never runs at all.
    """
    reasons = refusal(plan_for, root, [leaf("Alpha"), leaf("Beta")], source_sha="deadbeef")
    assert any("does not match the tree" in reason for reason in reasons), reasons


@case
def plan_refuses_manifest_with_no_source_hash(root):
    """Pins build_plan's `carries no sourceSha256` branch: an unstamped manifest
    cannot be tied to any tree, so every later hash comparison would be vacuous."""
    reasons = refusal(plan_for, root, [leaf("Alpha"), leaf("Beta")], source_sha="")
    assert any("carries no sourceSha256" in reason for reason in reasons), reasons


@case
def plan_reports_uncategorised_leaves(root):
    """Pins build_plan's `uncategorised` accounting.

    Defaulting an unlabelled leaf into the release lane is the safe direction. Doing
    it silently is not: the lane rules only mean something if somebody can see how
    many leaves arrived there by default rather than by decision.
    """
    rows = [leaf("Alpha", categories=[]), leaf("Beta", categories=["ReleaseRegression"])]
    plan, _ = plan_for(root, rows)
    assert plan["uncategorisedCount"] == 1, plan["uncategorised"]
    assert plan["uncategorised"] == [rows[0]["fullName"]]


@case
def plan_keeps_a_selection_group_on_one_worker(root):
    """Pins group_leaves + check_plan's `selectionName is split` branch.

    Generated [TestCaseSource] leaves share one raw NUnit name. Naming it selects all
    of them, so a plan that put two of them on different workers would run the whole
    group twice and the aggregate would report duplicates that look like a
    double-scheduled worker.
    """
    shared = "Nebula.Tests.PlayMode.FooTests.Generated"
    rows = [
        leaf("Generated GeneratedTestCase0", selection=shared, unique="u0", child_index=0),
        leaf("Generated GeneratedTestCase1", selection=shared, unique="u1", child_index=1),
        leaf("Alpha"), leaf("Beta"),
    ]
    plan, _ = plan_for(root, rows, workers=2)
    owners = {worker["worker"] for worker in plan["workers"]
              for group in worker["groups"] if group["selectionName"] == shared}
    assert len(owners) == 1, owners
    placed = [leaf["fullName"] for worker in plan["workers"] for group in worker["groups"]
              for leaf in group["leaves"]]
    assert len(placed) == len(set(placed)) == 4, placed

    plan["workers"][0]["groups"] = [{"selectionName": shared, "leaves": [rows[0]],
                                     "seconds": 1.0}]
    plan["workers"][1]["groups"] = [{"selectionName": shared, "leaves": [rows[1]],
                                     "seconds": 1.0}]
    reasons = gate.check_plan(plan, {})
    assert any("is split across workers" in reason for reason in reasons), reasons


@case
def plan_refuses_to_start_a_worker_with_nothing_to_do(root):
    """Pins check_plan's `were given nothing to run` branch.

    An empty worker costs a whole Editor boot to produce a zero-test result, and a
    zero-test result is RUN_INVALID -- so the gate would spend twenty minutes
    arriving at a verdict it could have refused in a millisecond.
    """
    reasons = refusal(plan_for, root, [leaf("Alpha"), leaf("Beta")], workers=5)
    assert any("given nothing to run" in reason for reason in reasons), reasons


@case
def plan_refuses_a_manifest_that_was_read_while_it_was_written(root):
    """Pins load_manifest's leafCount-versus-length branch.

    The Editor writes the manifest to a sibling and moves it into place, so this
    should be unreachable. It is checked anyway because the failure it prevents --
    planning a smaller suite than exists and calling the result complete -- is
    silent, and the check costs one comparison.
    """
    manifest_path, durations_path, _ = scaffold(root, [leaf("Alpha"), leaf("Beta")])
    with open(manifest_path) as handle:
        manifest = json.load(handle)
    manifest["leaves"] = manifest["leaves"][:1]
    with open(manifest_path, "w") as handle:
        json.dump(manifest, handle)
    reasons = refusal(gate.build_plan, manifest_path, os.path.join(root, "out"), 1, root,
                      durations_path=durations_path)
    assert any("read while it was being written" in reason for reason in reasons), reasons


@case
def run_is_invalid_when_unity_writes_no_xml(root):
    """Pins run_worker's `without writing a result XML` branch.

    Unity writes its XML only when a run reaches its end. No file means the run did
    not finish, and a worker that reports anything else is inventing a number.
    """
    plan, out = plan_for(root, [leaf("Alpha"), leaf("Beta")])
    verdict, status = gate.run_worker(out, 1, launch=faker(lambda: None))
    assert verdict == "RUN_INVALID", (verdict, status)
    assert "without writing a result XML" in status["note"], status["note"]


@case
def run_is_invalid_when_the_xml_predates_the_run(root):
    """Pins run_worker's mtime-versus-start-epoch branch.

    This is the 581/564/0 failure, reproduced: a parseable, plausible, complete XML
    that belongs to another run. The run-id in the path already makes it unreachable
    in practice; the mtime check is what catches it if the path is ever reused.
    """
    plan, out = plan_for(root, [leaf("Alpha"), leaf("Beta")])
    paths = gate.worker_paths(out, plan["runId"], 1)
    names = ["Nebula.Tests.PlayMode.FooTests.Alpha"]

    def write():
        os.makedirs(os.path.dirname(paths["resultPath"]), exist_ok=True)
        with open(paths["resultPath"], "w") as handle:
            handle.write(xml_document(names, plan["sourceSha256"]))
        stale = time.time() - 5 * 86400
        os.utime(paths["resultPath"], (stale, stale))

    verdict, status = gate.run_worker(out, 1, launch=faker(write))
    assert verdict == "RUN_INVALID", (verdict, status)
    assert "predates this run" in status["note"], status["note"]


@case
def run_is_invalid_when_the_log_shows_compiler_errors(root):
    """Pins run_worker's scan_log branch.

    Unity keeps the last good assemblies when a compile fails and still runs and
    writes results, so a broken project reports a full green run against code that
    is not in the working tree.
    """
    plan, out = plan_for(root, [leaf("Alpha"), leaf("Beta")])
    launch = complete_worker(out, plan, 1, plan["sourceSha256"],
                             log="Assets/Foo.cs(1,1): error CS0246: nope\n")
    verdict, status = gate.run_worker(out, 1, launch=launch)
    assert verdict == "RUN_INVALID", (verdict, status)
    assert "did not compile" in status["note"], status["note"]


@case
def run_is_invalid_when_the_source_changes_underneath_it(root):
    """Pins run_worker's before/after source-hash comparison.

    A run that starts on one tree and ends on another has tested neither. Somebody
    saving a file in an editor during a twenty-minute worker is enough.
    """
    plan, out = plan_for(root, [leaf("Alpha"), leaf("Beta")])
    paths = gate.worker_paths(out, plan["runId"], 1)
    inner = complete_worker(out, plan, 1, plan["sourceSha256"])

    def launch(command, watchdog):
        result = inner(command, watchdog)
        with open(os.path.join(root, "Assets", "Tests", "PlayMode", "FooTests.cs"), "a") as handle:
            handle.write("// edited while the worker was running\n")
        return result

    verdict, status = gate.run_worker(out, 1, launch=launch)
    assert verdict == "RUN_INVALID", (verdict, status)
    assert "changed while the worker ran" in status["note"], status["note"]
    assert os.path.isfile(paths["resultPath"]), "the XML existed; the hash is what refused it"


@case
def run_is_invalid_when_the_filter_matched_nothing(root):
    """Pins run_worker's `ran zero tests` branch.

    A filter that names nothing produces a valid, fresh, perfectly green XML with
    total=0. Without this branch that is a passing worker.
    """
    plan, out = plan_for(root, [leaf("Alpha"), leaf("Beta")])
    launch = complete_worker(out, plan, 1, plan["sourceSha256"],
                             xml=xml_document([], plan["sourceSha256"]), names=[])
    verdict, status = gate.run_worker(out, 1, launch=launch)
    assert verdict == "RUN_INVALID", (verdict, status)
    assert "zero tests" in status["note"], status["note"]


def mirror_of(root, target, content=None, runtime=None):
    """A tree at `target` that hashes the same as `root` -- or deliberately does not.

    Both hashes walk by RELATIVE path, so a copy of the hashed files at the same
    relative paths is hash-identical no matter where it lives. That is what makes
    `cp -Rc` mirrors interchangeable, and what the --repo override relies on.

    `content` desyncs the C# and leaves the assets matching; `runtime` desyncs one
    asset and leaves the C# matching. The second is the case the second hash exists
    for, and before it existed that mirror was accepted.
    """
    write_tree(target, RUNTIME_TREE)
    if content is not None:
        poke(target, "Assets/Tests/PlayMode/FooTests.cs", content)
    if runtime is not None:
        poke(target, "Assets/TextMesh Pro/Resources/Fonts & Materials/Nebula SDF.asset",
             runtime)
    return target


@case
def run_refuses_a_mirror_that_is_not_the_planned_tree_before_launching_unity(root):
    """Pins run_worker's pre-launch source-hash check on the --repo override.

    The override exists because five parallel workers need five APFS mirrors but one
    shared --out. It is only safe because of this check: -projectPath now points
    somewhere the plan never named, so the ONLY thing tying that tree to the plan is
    the hash. Delete the check and a worker will happily spend twenty minutes testing
    a mirror somebody forgot to re-sync, and every downstream guard would pass it --
    the XML would be fresh, complete, parseable and entirely about other code.

    `launched` proves the refusal is free. A check that fires after Unity has run
    costs the whole run to reach the same answer.
    """
    plan, out = plan_for(root, [leaf("Alpha"), leaf("Beta")])
    stale = mirror_of(root, os.path.join(root, "stale-mirror"),
                      content="// a mirror nobody re-synced\n")

    launched = []
    def launch(command, watchdog):
        launched.append(command)
        return 0, False, 1

    verdict, status = gate.run_worker(out, 1, launch=launch, repo=stale)
    assert verdict == "RUN_INVALID", (verdict, status)
    assert launched == [], "Unity was launched against a tree that is not the plan's"
    assert "this is not the planned tree" in status["note"], status["note"]
    assert status["repo"] == os.path.abspath(stale), status
    assert status["repoIsMirror"] is True, status


@case
def a_refused_run_destroys_no_evidence(root):
    """Pins the ordering: every refusal happens before the result/journal deletion.

    The earlier order deleted this worker's journal and THEN hashed the tree. That
    was survivable while `run` was called once per worker and stops being survivable
    the moment retries exist -- a retry pointed at the wrong mirror would delete the
    journal proving what the good attempt had finished, then refuse, and `resume`
    would ask for leaves that had already passed. Move the delete loop back above the
    hash check and this goes red.
    """
    plan, out = plan_for(root, [leaf("Alpha"), leaf("Beta")])
    paths = gate.worker_paths(out, plan["runId"], 1)
    os.makedirs(os.path.dirname(paths["journalPath"]), exist_ok=True)
    proof = journal_lines([leaf_line("Nebula.Tests.PlayMode.FooTests.Alpha",
                                     plan["sourceSha256"], 1)])
    with open(paths["journalPath"], "w") as handle:
        handle.write(proof)

    stale = mirror_of(root, os.path.join(root, "stale-mirror"), content="// different\n")
    verdict, _ = gate.run_worker(out, 1, launch=faker(lambda: None), repo=stale)
    assert verdict == "RUN_INVALID", verdict
    assert open(paths["journalPath"]).read() == proof, "the refusal ate the evidence"


@case
def run_accepts_a_matching_mirror_and_keeps_all_evidence_in_the_shared_out(root):
    """Pins the --repo override doing its actual job.

    Unity opens the mirror; results, journals, status and every validation stay in
    the shared --out. Splitting those was the whole bug: the workaround gave each
    worker its own --out, the evidence landed in the shared directory, and all five
    workers then validated in their own empty ones and reported "Unity exited without
    writing a result XML" over 839 leaves that had been written and proven.
    """
    rows = [leaf("Alpha"), leaf("Beta")]
    plan, out = plan_for(root, rows, workers=1)
    mirror = mirror_of(root, os.path.join(root, "worker-1", "Nebula"))
    assert gate.source_hash(mirror) == plan["sourceSha256"], "mirror must hash equal"

    launch = complete_worker(out, plan, 1, plan["sourceSha256"])
    verdict, status = gate.run_worker(out, 1, launch=launch, repo=mirror)
    assert verdict == "PASS", (verdict, status)
    assert status["repo"] == os.path.abspath(mirror), status
    assert status["repoIsMirror"] is True, status
    # -projectPath is the mirror ...
    assert "-projectPath %s" % os.path.abspath(mirror) in status["command"], status["command"]
    # ... and nothing else is.
    for key in ("resultPath", "journalPath", "logPath", "statusPath", "planPath"):
        assert os.path.abspath(status[key]).startswith(os.path.abspath(out)), (key, status[key])
        assert not os.path.abspath(status[key]).startswith(os.path.abspath(mirror)), key
    assert gate.aggregate(out)["verdict"] == "PASS"


@case
def five_mirrors_aggregate_into_one_verdict(root):
    """The topology that failed, end to end: N mirrors, one --out, one verdict."""
    rows = [leaf("L%d" % i) for i in range(10)]
    plan, out = plan_for(root, rows, workers=5)
    for number in range(1, 6):
        mirror = mirror_of(root, os.path.join(root, "mirrors", "worker-%d" % number, "Nebula"))
        verdict, status = gate.run_worker(
            out, number, launch=complete_worker(out, plan, number, plan["sourceSha256"]),
            repo=mirror)
        assert verdict == "PASS", (number, status)
        assert status["repoIsMirror"] is True, status
    summary = gate.aggregate(out)
    assert summary["verdict"] == "PASS", summary
    assert summary["leavesSeen"] == summary["leavesExpected"] == 10, summary


@case
def a_killed_worker_keeps_its_journal_and_resumes_only_the_rest(root):
    """Pins the journal + resume_plan pair, which is the reason the journal exists.

    A worker is killed after two of its four leaves. It writes no XML, so it is
    RUN_INVALID -- correctly, it did not finish. But the two finished leaves are on
    disk and resume_plan must offer back only the other two. Remove the journal read
    from resume_plan and this test asks for all four.
    """
    rows = [leaf("L%d" % i) for i in range(4)]
    plan, out = plan_for(root, rows, workers=1)
    paths = gate.worker_paths(out, plan["runId"], 1)
    done = [rows[0]["fullName"], rows[1]["fullName"]]

    def launch(command, watchdog):
        os.makedirs(os.path.dirname(paths["journalPath"]), exist_ok=True)
        with open(paths["journalPath"], "w") as handle:
            handle.write(journal_lines(
                [leaf_line(name, plan["sourceSha256"], 1, sequence=i,
                           runtime_sha=plan["runtimeInputSha256"])
                 for i, name in enumerate(done)]))
            # A process killed mid-write leaves a half line. It must cost that one
            # line and nothing else; the lines before it are still proof.
            handle.write('{"kind":"leaf","fullName":"Nebula.Tests.Play')
        return -9, True, 5150

    verdict, status = gate.run_worker(out, 1, launch=launch)
    assert verdict == "RUN_INVALID", (verdict, status)
    assert status["journalProvenLeaves"] == 2, status
    assert status["journalDamagedLines"] == 1, status

    remaining = gate.resume_plan(out, 1)
    assert sorted(remaining["finishedLeaves"]) == sorted(done), remaining
    assert sorted(remaining["remainingLeaves"]) == sorted(
        [rows[2]["fullName"], rows[3]["fullName"]]), remaining


@case
def a_watchdog_kill_names_the_leaf_in_flight_and_says_nothing_caught_it_first(root):
    """Pins run_worker's watchdog note, which is a diagnosis and not just a verdict.

    Not every soak leaf carries a per-leaf [Timeout]:
    UiFinalLifecyclePerformanceTests.AThousandOpenCloseCyclesGrowNothing has none,
    so nothing inside the run will ever stop it and the watchdog is the only
    backstop there is. An operator reading a bare "watchdog fired at 5400s" starts
    by looking for the per-leaf timeout that should have caught it first, and for
    those leaves that guard does not exist -- hunting a phantom is the most
    expensive way to open a diagnosis.

    So the note says outright that nothing caught it first, names the leaf that was
    in flight (the first in this worker's documented order with no journal line),
    and gives the resume command. Delete any of the three and this goes red.
    """
    rows = [leaf("L%d" % i) for i in range(4)]
    plan, out = plan_for(root, rows, workers=1)
    paths = gate.worker_paths(out, plan["runId"], 1)
    ordered = [row["fullName"] for row in rows]
    done = ordered[:2]

    def launch(command, watchdog):
        os.makedirs(os.path.dirname(paths["journalPath"]), exist_ok=True)
        os.makedirs(os.path.dirname(paths["resultPath"]), exist_ok=True)
        with open(paths["logPath"], "w") as handle:
            handle.write("ok\n")
        # An XML exists, so the run reached its end -- the watchdog branch is only
        # reachable past the no-XML branch, and this is what makes it distinct.
        with open(paths["resultPath"], "w") as handle:
            handle.write(xml_document(done, plan["sourceSha256"],
                                      runtime_sha=plan["runtimeInputSha256"]))
        with open(paths["journalPath"], "w") as handle:
            handle.write(journal_lines(
                [leaf_line(name, plan["sourceSha256"], 1, sequence=i,
                           runtime_sha=plan["runtimeInputSha256"])
                 for i, name in enumerate(done)]))
        return -15, True, 7788

    verdict, status = gate.run_worker(out, 1, launch=launch, watchdog=5400.0)
    assert verdict == "RUN_INVALID", (verdict, status)
    assert "no per-leaf timeout catching it first" in status["note"], status["note"]
    assert "2 of 4 leaves are proven" in status["note"], status["note"]
    # The leaf in flight is the third, not the first unproven one in some other
    # order and not the last one that finished.
    assert ordered[2] in status["note"], status["note"]
    assert "resume --worker 1" in status["note"], status["note"]
    assert status["leavesUnproven"] == ordered[2:], status


@case
def aggregate_is_red_when_a_leaf_never_ran(root):
    """Pins aggregate's `never ran` branch.

    Four green workers and one that produced nothing add up to a smaller green
    number, not a passing suite. Coverage is the check; the arithmetic is not.
    """
    rows = [leaf("L%d" % i) for i in range(4)]
    plan, out = plan_for(root, rows, workers=1)
    names = [row["fullName"] for row in rows][:3]
    gate.run_worker(out, 1, launch=complete_worker(out, plan, 1, plan["sourceSha256"],
                                                   names=names))
    summary = gate.aggregate(out)
    assert summary["verdict"] == "RUN INVALID", summary
    assert summary["leavesMissing"] == [rows[3]["fullName"]], summary["leavesMissing"]


@case
def aggregate_is_red_when_a_leaf_ran_on_two_workers(root):
    """Pins aggregate's `ran on more than one worker` branch.

    A leaf running twice is not a harmless duplicate: with a regex filter it is the
    first symptom of over-matching, and the two runs can disagree.
    """
    rows = [leaf("Alpha"), leaf("Beta")]
    plan, out = plan_for(root, rows, workers=2)
    everything = [row["fullName"] for row in rows]
    for number in (1, 2):
        gate.run_worker(out, number,
                        launch=complete_worker(out, plan, number, plan["sourceSha256"],
                                               names=everything))
    summary = gate.aggregate(out)
    assert summary["verdict"] == "RUN INVALID", summary
    assert sorted(summary["leavesDuplicated"]) == sorted(everything), summary


@case
def aggregate_is_red_when_the_evidence_names_a_test_nobody_planned(root):
    """Pins aggregate's `not in the plan` branch.

    A result for a test the plan never contained means the filter selected something
    else. The extra test passing does not make that acceptable -- it means the
    selection is not doing what the plan says it does.
    """
    rows = [leaf("Alpha"), leaf("Beta")]
    plan, out = plan_for(root, rows, workers=1)
    names = [row["fullName"] for row in rows] + ["Nebula.Tests.PlayMode.FooTests.Ghost"]
    gate.run_worker(out, 1, launch=complete_worker(out, plan, 1, plan["sourceSha256"],
                                                   names=names))
    summary = gate.aggregate(out)
    assert summary["verdict"] == "RUN INVALID", summary
    assert summary["leavesUnknown"] == ["Nebula.Tests.PlayMode.FooTests.Ghost"], summary


@case
def aggregate_never_merges_evidence_from_another_source_hash(root):
    """Pins journal_leaves' sourceSha256 filter, used by aggregate.

    Evidence from a different tree is evidence about a different program. Counted in,
    it fills the coverage check with results for code that is not the code under
    test -- which is the 581/564/0 failure again, one level down.
    """
    rows = [leaf("Alpha"), leaf("Beta")]
    plan, out = plan_for(root, rows, workers=1)
    gate.run_worker(out, 1, launch=complete_worker(out, plan, 1, plan["sourceSha256"],
                                                   journalSha="a-different-tree"))
    summary = gate.aggregate(out)
    assert summary["verdict"] == "RUN INVALID", summary
    assert sorted(summary["leavesMissing"]) == sorted(row["fullName"] for row in rows), summary
    assert any("no journal evidence" in problem for problem in summary["problems"]), summary


@case
def aggregate_passes_only_on_complete_matching_evidence(root):
    """The control. Without a case that CAN go green, every red above proves only
    that the gate refuses everything."""
    rows = [leaf("Alpha"), leaf("Beta"), leaf("Gamma"), leaf("Delta")]
    plan, out = plan_for(root, rows, workers=2)
    for number in (1, 2):
        verdict, status = gate.run_worker(
            out, number, launch=complete_worker(out, plan, number, plan["sourceSha256"]))
        assert verdict == "PASS", (number, status)
    summary = gate.aggregate(out)
    assert summary["verdict"] == "PASS", summary
    assert summary["leavesSeen"] == summary["leavesExpected"] == 4, summary

    recorded = gate.record_durations(out, durations_path=os.path.join(root, "cache.json"))
    assert recorded == 4, recorded
    with open(os.path.join(root, "cache.json")) as handle:
        cache = json.load(handle)
    assert cache["leaves"][rows[0]["fullName"]]["seconds"] == 1.5, cache


@case
def aggregate_is_red_on_a_failed_leaf(root):
    """Pins aggregate's notPassed collection. A journal line saying Failed has to
    reach the verdict even when the XML totals were written optimistically."""
    rows = [leaf("Alpha"), leaf("Beta")]
    plan, out = plan_for(root, rows, workers=1)
    paths = gate.worker_paths(out, plan["runId"], 1)
    names = [row["fullName"] for row in rows]

    def write():
        os.makedirs(os.path.dirname(paths["resultPath"]), exist_ok=True)
        os.makedirs(os.path.dirname(paths["journalPath"]), exist_ok=True)
        with open(paths["logPath"], "w") as handle:
            handle.write("ok\n")
        runtime = plan["runtimeInputSha256"]
        with open(paths["resultPath"], "w") as handle:
            handle.write(xml_document(names, plan["sourceSha256"], runtime_sha=runtime))
        with open(paths["journalPath"], "w") as handle:
            handle.write(journal_lines([
                leaf_line(names[0], plan["sourceSha256"], 1, sequence=0,
                          runtime_sha=runtime),
                leaf_line(names[1], plan["sourceSha256"], 1, result="Failed", sequence=1,
                          runtime_sha=runtime),
            ]))

    gate.run_worker(out, 1, launch=faker(write))
    summary = gate.aggregate(out)
    assert summary["verdict"] != "PASS", summary
    assert [entry["fullName"] for entry in summary["notPassed"]] == [names[1]], summary


@case
def dotnet_regex_escape_produces_an_exactly_matching_pattern(root):
    """Pins dotnet_regex_escape + anchored_filter, the legacy -testFilter path.

    Filter.cs:25 documents -testFilter as regex. A raw leaf name is not a literal
    pattern: the dots match any character and the parentheses of a [TestCase] name
    are groups, so the name neither matches itself nor fails to match its siblings.
    Python's re.escape is not a substitute -- it escapes '-', '&' and '~', which .NET
    does not recognise as escapes.
    """
    name = "Nebula.Tests.PlayMode.FooTests.WithArgs(1, \"a-b\")"
    pattern = gate.anchored_filter(name)
    assert pattern.startswith("^") and pattern.endswith("$"), pattern
    for character in ".()":
        assert "\\" + character in pattern, (character, pattern)
    assert "\\-" not in pattern, "escaping '-' is a Python habit .NET does not share"

    import re
    python_equivalent = pattern.replace("\\#", "#")
    assert re.match(python_equivalent, name), pattern
    assert not re.match(python_equivalent, name.replace(".Foo", "XFoo")), pattern


# ------------------------------------------------------- the runtime input hash
#
# sourceSha256 answers "is this the same C#". These answer "is this the same
# product", which is a strictly larger question and the one a gate is actually
# asked. Everything below is a case where the two answers differ.


@case
def changing_a_tmp_asset_changes_the_runtime_hash(root):
    """Pins the TMP files being inside RUNTIME_INPUT_ROOTS.

    This is the whole reason the second hash exists, stated as small as it goes. A
    TMP font atlas and TMP Settings decide what every string in the product looks
    like and whether it fits its box; regenerate an atlas and not one byte of C#
    moves. With sourceSha256 alone the gate calls that the same run and merges the
    results, so a caption that now overflows is gated by evidence from the atlas
    that did not overflow.
    """
    write_tree(root, RUNTIME_TREE)
    write_tree(root, GENERATED_TREE)
    source_before = gate.source_hash(root)
    before = gate.runtime_input_hash(root)

    for path in ("Assets/TextMesh Pro/Resources/Fonts & Materials/Nebula SDF.asset",
                 "Assets/TextMesh Pro/Resources/TMP Settings.asset"):
        poke(root, path, "regenerated\n")
        after = gate.runtime_input_hash(root)
        assert after != before, "%s did not move the runtime hash" % path
        before = after

    # And the point of the pair: the C# is untouched throughout, so the hash the
    # gate had before today would have said nothing happened.
    assert gate.source_hash(root) == source_before, "the C# was supposed to be identical"


@case
def changing_a_capture_png_does_not_change_the_runtime_hash(root):
    """Pins the exclusion of everything the run itself writes.

    Fourteen PlayMode fixtures here write PNGs, and the gate is what runs them. If a
    capture counted as a runtime input then every visual worker would invalidate
    itself on its own output -- the hash before it started and the hash after it
    finished could not agree, by construction, and the lane could never be green for
    any reason connected to the product.

    Result XML, logs and journals are the same case and are checked with it. All of
    them are excluded structurally rather than by name: they are not under Assets,
    ProjectSettings or Packages. Add UIDesign to RUNTIME_INPUT_ROOTS and this goes
    red immediately.
    """
    write_tree(root, RUNTIME_TREE)
    write_tree(root, GENERATED_TREE)
    before = gate.runtime_input_hash(root)

    for path in ("UIDesign/Runtime/Part4/capture-01.png",
                 "UIDesign/Review/results-worker-1.xml",
                 "Logs/worker-1.log",
                 "TestResults/full.xml",
                 "Assets/Resources/UI/Sprites/.DS_Store",
                 "Assets/Nebula.Editor.csproj"):
        poke(root, path, "rewritten by the run being gated\n")
        assert gate.runtime_input_hash(root) == before, \
            "%s reached the runtime hash; the run now invalidates itself" % path

    # A new capture, not just a rewritten one -- the normal case, since the file
    # names carry resolutions and states that vary run to run.
    poke(root, "UIDesign/Runtime/Part4/capture-99-3840x2160.png", "brand new\n")
    assert gate.runtime_input_hash(root) == before, "a NEW capture reached the hash"


@case
def a_scene_unity_makes_during_the_run_does_not_invalidate_the_run(root):
    """Pins INIT_TEST_SCENE, which is the one exclusion inside Assets.

    Unity's PlayMode launcher writes Assets/InitTestScene<DateTime.Ticks>.unity to
    boot a run and removes it at the end. It is created by the process being
    measured, inside the window being measured, at a path under a root the hash
    covers -- so without this rule every single PlayMode worker reports "runtime
    inputs changed while the worker ran" and the gate has no green state at all.
    Twenty-three of them are in this checkout from runs that died before cleanup.

    The rule is narrow on purpose: only that exact name shape. A real scene added to
    Assets still counts, which the second half checks.
    """
    write_tree(root, RUNTIME_TREE)
    before = gate.runtime_input_hash(root)

    poke(root, "Assets/InitTestScene639999999999999999.unity", "%YAML 1.1\n")
    poke(root, "Assets/InitTestScene639999999999999999.unity.meta", "guid: 9\n")
    assert gate.runtime_input_hash(root) == before, \
        "Unity's own boot scene invalidated the run that made it"

    poke(root, "Assets/Scenes/AnActualNewScene.unity", "%YAML 1.1\n")
    assert gate.runtime_input_hash(root) != before, \
        "the exclusion is too wide; a real scene slipped through it"


@case
def changing_a_projectsettings_entry_changes_the_runtime_hash(root):
    """Pins ProjectSettings and Packages being inside RUNTIME_INPUT_ROOTS.

    QualitySettings is not decoration on this project: SessionLeakRegressionTests
    exists because a pixel-light budget written into
    ProjectSettings/QualitySettings.asset is inherited by every later run and was
    read as a leaked light. A gate that cannot see that file cannot tell a product
    change from a settings change, and the package manifest is the same argument one
    level up -- a different test-framework version is a different runner.
    """
    write_tree(root, RUNTIME_TREE)
    source_before = gate.source_hash(root)
    before = gate.runtime_input_hash(root)

    for path, content in (("ProjectSettings/QualitySettings.asset", "pixelLightCount: 4\n"),
                          ("ProjectSettings/ProjectSettings.asset", "productName: Other\n"),
                          ("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 2022.3.23f1\n"),
                          ("Packages/manifest.json", '{"dependencies":{}}\n'),
                          ("Packages/packages-lock.json", '{"dependencies":{"x":"1"}}\n')):
        poke(root, path, content)
        after = gate.runtime_input_hash(root)
        assert after != before, "%s did not move the runtime hash" % path
        before = after

    assert gate.source_hash(root) == source_before, "the C# was supposed to be identical"


@case
def a_mirror_whose_assets_do_not_match_is_refused_before_unity_starts(root):
    """Pins run_worker's pre-launch runtime-hash check on the --repo override.

    The mirror this catches is not hypothetical: it is what `git checkout --
    Assets/Scripts` produces, and what a `cp -Rc` re-sync that ran while an importer
    was still writing produces. Its C# is right, so the source hash accepts it, and
    every downstream guard then passes -- the XML is fresh, complete, parseable, and
    about a different set of assets.

    `launched` proves the refusal costs nothing. Delete the check and the same
    answer arrives after a full soak lane.
    """
    plan, out = plan_for(root, [leaf("Alpha"), leaf("Beta")])
    stale = mirror_of(root, os.path.join(root, "stale-mirror"),
                      runtime="an atlas nobody re-exported\n")
    assert gate.source_hash(stale) == plan["sourceSha256"], \
        "the point of this mirror is that its C# DOES match"

    launched = []
    def launch(command, watchdog):
        launched.append(command)
        return 0, False, 1

    verdict, status = gate.run_worker(out, 1, launch=launch, repo=stale)
    assert verdict == "RUN_INVALID", (verdict, status)
    assert launched == [], "Unity ran against a tree whose assets are not the plan's"
    assert "the C# matches and the assets do not" in status["note"], status["note"]


@case
def runtime_input_changing_during_a_worker_invalidates_the_run(root):
    """Pins run_worker's before/after runtime-hash comparison, and its diagnosis.

    A run that starts against one font atlas and ends against another has tested
    neither, exactly as with the source hash -- and this half is the more likely of
    the two on this project, because rebuilding an atlas or re-importing a sprite is
    something a person does without thinking of it as changing the code.

    The status must NAME the file. Over four and a half thousand runtime inputs,
    "something changed" sends the operator to bisect by hand; "the atlas changed" is
    actionable in a minute, and when the answer turns out to be
    ProjectSettings/QualitySettings.asset it is not an operator error at all, it is
    a test that failed to restore what it altered.
    """
    plan, out = plan_for(root, [leaf("Alpha"), leaf("Beta")])
    paths = gate.worker_paths(out, plan["runId"], 1)
    inner = complete_worker(out, plan, 1, plan["sourceSha256"])

    def launch(command, watchdog):
        result = inner(command, watchdog)
        poke(root, "Assets/TextMesh Pro/Resources/Fonts & Materials/Nebula SDF.asset",
             "re-exported while the worker was running\n")
        return result

    verdict, status = gate.run_worker(out, 1, launch=launch)
    assert verdict == "RUN_INVALID", (verdict, status)
    assert "runtime inputs changed while the worker ran" in status["note"], status["note"]
    assert "Nebula SDF.asset" in status["note"], status["note"]
    assert status["runtimeInputChanges"]["changed"] == [
        "Assets/TextMesh Pro/Resources/Fonts & Materials/Nebula SDF.asset"], status
    # The C# never moved, so the source-hash guard had nothing to say -- which is
    # the point. Revert the runtime comparison and this run is a clean PASS.
    assert status["sourceHashAfter"] == status["sourceHashBefore"], status
    assert os.path.isfile(paths["resultPath"]), "the XML existed; the hash is what refused it"


@case
def an_added_or_deleted_asset_during_a_worker_is_caught_and_named(root):
    """The other two shapes of the same failure: files appearing and disappearing.

    A rolling digest over content alone would miss a deletion whose bytes happened
    to be re-added elsewhere, and an added file has no `before` entry to compare. So
    the comparison is over the path SET as well as the contents, and each of the
    three shapes is reported separately -- "added" and "removed" read very
    differently to a person deciding whether to re-run.
    """
    plan, out = plan_for(root, [leaf("Alpha"), leaf("Beta")])
    inner = complete_worker(out, plan, 1, plan["sourceSha256"])

    def launch(command, watchdog):
        result = inner(command, watchdog)
        poke(root, "Assets/Resources/UI/Sprites/late-arrival.png", "imported mid-run\n")
        os.remove(os.path.join(root, "Assets", "Resources", "UI", "Materials", "panel.mat"))
        return result

    verdict, status = gate.run_worker(out, 1, launch=launch)
    assert verdict == "RUN_INVALID", (verdict, status)
    changes = status["runtimeInputChanges"]
    assert changes["added"] == ["Assets/Resources/UI/Sprites/late-arrival.png"], changes
    assert changes["removed"] == ["Assets/Resources/UI/Materials/panel.mat"], changes
    assert changes["changedCount"] == 0, changes


@case
def results_with_a_different_runtime_hash_refuse_to_aggregate(root):
    """Pins aggregate's runtimeInputSha256 barrier, at all three of its layers.

    Two workers whose C# agrees and whose assets do not are two results about two
    products. Summing them produces a total for a suite that was never executed --
    the 581/564/0 failure again, with the stale half moved out of the code and into
    the assets, where the source hash cannot see it.

    Three layers because each catches a different lie. The status file catches a
    worker that ran the wrong mirror; the journal filter catches per-leaf evidence
    that survived a re-plan; the XML attribute catches a result file that arrived
    after the worker validated. Any one of them alone leaves a way through, so each
    is reached here through a scenario the other two cannot see -- the first draft
    of this test reached the XML branch through a worker that had already refused
    the same XML itself, and deleting the aggregate's check left the test green.
    """
    rows = [leaf("Alpha"), leaf("Beta")]
    names = [row["fullName"] for row in rows]

    # Layer 1: a status file naming another tree, with this run's own journal and
    # XML beside it -- the shape of a worker pointed at the wrong mirror by a
    # re-run. Only the status comparison can see it.
    plan, out = plan_for(root, rows, workers=1)
    gate.run_worker(out, 1, launch=complete_worker(out, plan, 1, plan["sourceSha256"]))
    status_path = gate.worker_paths(out, plan["runId"], 1)["statusPath"]
    with open(status_path) as handle:
        status = json.load(handle)
    status["expectedRuntimeInputSha256"] = "another-atlas"
    with open(status_path, "w") as handle:
        json.dump(status, handle)
    summary = gate.aggregate(out)
    assert summary["verdict"] == "RUN INVALID", summary
    assert any("ran different runtime inputs" in problem
               for problem in summary["problems"]), summary

    # Layer 2: per-leaf evidence stamped with another tree. The leaves it proves are
    # not this run's leaves, so they count as never having run.
    second = os.path.join(root, "journal")
    plan2, out2 = plan_for(second, rows, workers=1)
    gate.run_worker(out2, 1, launch=complete_worker(
        out2, plan2, 1, plan2["sourceSha256"], journalRuntimeSha="another-atlas"))
    summary2 = gate.aggregate(out2)
    assert summary2["verdict"] == "RUN INVALID", summary2
    assert sorted(summary2["leavesMissing"]) == sorted(names), summary2
    assert any("no journal evidence" in problem for problem in summary2["problems"]), summary2

    # Layer 3: a healthy, validated worker whose result XML is replaced afterwards.
    # The worker cannot catch this -- it had already finished and passed -- so the
    # aggregate is the only thing standing between a swapped result file and a
    # green verdict.
    third = os.path.join(root, "xml")
    plan3, out3 = plan_for(third, rows, workers=1)
    verdict, _ = gate.run_worker(out3, 1,
                                 launch=complete_worker(out3, plan3, 1, plan3["sourceSha256"]))
    assert verdict == "PASS", verdict
    result_path = gate.worker_paths(out3, plan3["runId"], 1)["resultPath"]
    with open(result_path, "w") as handle:
        handle.write(xml_document(names, plan3["sourceSha256"], runtime_sha="another-atlas"))
    summary3 = gate.aggregate(out3)
    assert summary3["verdict"] == "RUN INVALID", summary3
    assert any("result XML is from different runtime inputs" in problem
               for problem in summary3["problems"]), summary3


@case
def a_worker_refuses_a_result_xml_stamped_with_other_runtime_inputs(root):
    """Pins run_worker's own nebula-runtime-input-sha256 check, which the aggregate's
    identical-looking check does not cover.

    The Editor stamps the XML from the plan it was handed, so a mismatch here means
    the worker was driven by a different plan than the one being validated -- a
    second gate sharing the --out, or a plan.json rewritten under a running worker.
    Catching it at the worker is what stops the run before four more workers spend
    half an hour producing evidence that will be thrown away.
    """
    rows = [leaf("Alpha"), leaf("Beta")]
    plan, out = plan_for(root, rows, workers=1)
    names = [row["fullName"] for row in rows]
    verdict, status = gate.run_worker(out, 1, launch=complete_worker(
        out, plan, 1, plan["sourceSha256"],
        xml=xml_document(names, plan["sourceSha256"], runtime_sha="another-atlas")))
    assert verdict == "RUN_INVALID", (verdict, status)
    assert "result XML was produced from different runtime inputs" in status["note"], status
    # The source stamp on that XML is this run's, so the older check saw nothing.
    assert status["xmlSourceSha256"] == plan["sourceSha256"], status


@case
def a_manifest_without_a_runtime_hash_is_refused(root):
    """Pins build_plan's mandatory runtimeInputSha256, and the mismatch beside it.

    An optional hash is worse than no hash: it looks like a check in the plan, in
    the status file and in the summary, and it is satisfied by every manifest that
    omits it. So a manifest that carries only sourceSha256 is refused with the
    command that fixes it, and one carrying the wrong value is refused as a
    different tree.
    """
    reasons = refusal(plan_for, root, [leaf("Alpha"), leaf("Beta")], runtime_sha="")
    assert any("carries no runtimeInputSha256" in reason for reason in reasons), reasons
    assert any("runtime-hash" in reason for reason in reasons), reasons

    other = os.path.join(root, "other")
    reasons = refusal(plan_for, other, [leaf("Alpha"), leaf("Beta")],
                      runtime_sha="deadbeef")
    assert any("runtime input hash" in reason and "does not match" in reason
               for reason in reasons), reasons


@case
def both_hashes_reach_the_plan_the_worker_and_the_summary(root):
    """The control for this group, and the wiring check.

    Every red above proves a refusal fires. This proves the value is actually
    carried end to end -- plan.json, the worker plan the Editor reads, the status
    file and the summary -- because a hash that is computed and then not written
    anywhere refuses nothing while looking exactly like this feature.
    """
    rows = [leaf("Alpha"), leaf("Beta")]
    plan, out = plan_for(root, rows, workers=1)
    expected = gate.runtime_input_hash(root)
    assert plan["runtimeInputSha256"] == expected, plan
    assert plan["runtimeInputFileCount"] == len(gate.runtime_input_digests(root)), plan
    assert plan["runtimeInputSha256"] != plan["sourceSha256"], "two hashes, not one twice"

    with open(gate.worker_paths(out, plan["runId"], 1)["planPath"]) as handle:
        worker_plan = json.load(handle)
    assert worker_plan["runtimeInputSha256"] == expected, worker_plan

    mirror = mirror_of(root, os.path.join(root, "worker-1", "Nebula"))
    assert gate.runtime_input_hash(mirror) == expected, "mirror must hash equal"
    verdict, status = gate.run_worker(
        out, 1, launch=complete_worker(out, plan, 1, plan["sourceSha256"]), repo=mirror)
    assert verdict == "PASS", (verdict, status)
    assert status["runtimeInputHashBefore"] == expected, status
    assert status["runtimeInputHashAfter"] == expected, status
    assert status["expectedRuntimeInputSha256"] == expected, status

    summary = gate.aggregate(out)
    assert summary["verdict"] == "PASS", summary
    assert summary["runtimeInputSha256"] == expected, summary
    assert gate.resume_plan(out, 1)["runtimeInputSha256"] == expected


@case
def the_runtime_hash_is_stable_across_walks_and_ignores_where_the_tree_lives(root):
    """Pins the two properties five parallel mirrors depend on.

    Deterministic: two walks of one tree must agree, or a worker invalidates itself
    at random and the failure looks like a flaky test rather than a broken hash.
    Location-independent: the digest is folded over RELATIVE paths, so a `cp -Rc`
    clone at any path is interchangeable with the original -- which is the entire
    basis of the --repo override.
    """
    write_tree(root, RUNTIME_TREE)
    assert gate.runtime_input_hash(root) == gate.runtime_input_hash(root)

    elsewhere = mirror_of(root, os.path.join(root, "somewhere", "else", "Nebula"))
    assert gate.runtime_input_hash(elsewhere) == gate.runtime_input_hash(root), \
        "the hash depends on where the tree lives; no mirror can ever match"


@case
def the_real_checkout_hashes_and_excludes_what_it_claims_to(root):
    """The rules against the actual project, not a fixture.

    A fixture proves the walk obeys its own rules; only the real tree proves the
    rules describe this project. Three claims that a fixture cannot make: the hash
    covers a four-figure number of real files, the TMP assets and ProjectSettings
    are among them by name, and none of the InitTestScene leftovers sitting in this
    checkout are.
    """
    digests = gate.runtime_input_digests(REPO)
    repo = REPO
    assert len(digests) > 1000, len(digests)

    for name in ("Assets/TextMesh Pro/Resources/TMP Settings.asset",
                 "ProjectSettings/ProjectSettings.asset",
                 "ProjectSettings/QualitySettings.asset",
                 "Packages/manifest.json", "Packages/packages-lock.json",
                 "Assets/Scenes/Nebula.unity"):
        assert name in digests, name
        assert os.path.isfile(os.path.join(repo, name)), name

    leftovers = [name for name in digests if "InitTestScene" in name]
    assert leftovers == [], leftovers
    assert not [name for name in digests if name.endswith(".DS_Store")]
    assert not [name for name in digests
                if name.split("/", 1)[0] not in gate.RUNTIME_INPUT_ROOTS]
    # Deterministic on the real tree too, and cheap enough to run twice per worker.
    assert gate.runtime_input_hash(repo, digests) == gate.runtime_input_hash(repo)


@case
def the_fixture_ceiling_binds_the_short_lanes_and_not_the_endurance_lanes(root):
    """Pins LANES[...]["fixtureCeiling"] for ALL FOUR lanes, in both directions.

    The 120s fixture ceiling is a BALANCE rule: a fixture heavier than it cannot be
    moved between workers as a unit, so pinning it hands one worker the tail of the
    run. That premise holds only where leaves are meant to be short. In a lane whose
    leaves are long BY DESIGN it is unsatisfiable -- no ceiling below one leaf's own
    runtime is reachable -- so the rule cannot be met, only tripped, and a lane that
    cannot be planned is a trap set for whoever needs it next.

    Both endurance lanes were in it. Soak: one 193.5s TutorialLifecycleTests leaf
    refused the ENTIRE lane. Diagnostic: EngineDestroyProbePlayModeTests totals
    220.7s and refused that one. Nothing about those tests moves -- not a cycle
    count, not a threshold, not a lane label.

    Every lane is asserted by name rather than looped over LANES, so flipping one
    setting fails a specific line instead of quietly changing what the test checks.
    Flip Soak or Diagnostic on and the first half goes red; flip ReleaseRegression
    or Graphics off and the second does.
    """
    assert gate.LANES["Soak"]["fixtureCeiling"] is False
    assert gate.LANES["Diagnostic"]["fixtureCeiling"] is False
    assert gate.LANES["ReleaseRegression"]["fixtureCeiling"] is True
    assert gate.LANES["Graphics"]["fixtureCeiling"] is True

    # The endurance lanes plan a fixture far over the ceiling without complaint ...
    for lane in ("Soak", "Diagnostic"):
        fresh = os.path.join(root, lane)
        rows = [leaf("Slow1", categories=[lane]), leaf("Slow2", categories=[lane])]
        durations = {rows[0]["fullName"]: {"seconds": 800.0},
                     rows[1]["fullName"]: {"seconds": 700.0}}
        plan, _ = plan_for(fresh, rows, workers=2, lane=lane, durations=durations)
        assert plan["leafCount"] == 2, (lane, plan)
        assert sorted(worker["leafCount"] for worker in plan["workers"]) == [1, 1], plan

    # ... and the short lanes still refuse one, naming the lane it was refused in.
    for lane in ("ReleaseRegression", "Graphics"):
        fresh = os.path.join(root, lane)
        heavy = [leaf("L%d" % i, categories=[lane]) for i in range(5)]
        measured = {row["fullName"]: {"seconds": 25.0} for row in heavy}
        reasons = refusal(plan_for, fresh, heavy, workers=2, lane=lane,
                          durations=measured, graphics=(lane == "Graphics"))
        assert any("over the 120s ceiling" in reason for reason in reasons), (lane, reasons)
        assert any("in lane %s" % lane in reason for reason in reasons), (lane, reasons)


@case
def the_graphics_lane_keeps_a_fixture_whole_and_the_release_lane_does_not(root):
    """Pins LANES[...]["coLocateFixtures"] in both directions, and the refusal.

    A capture fixture writes state a sibling leaf then reads. Split across two
    processes,
    NebulaBrandMatrixCaptureTests.TheManifestHasNoPendingCells does not fail
    honestly -- it reports "the capture case did not run in this process; the
    manifest cannot be checked", which is a leaf saying it was never able to measure
    anything, and which reads like a product bug. Measured on a real 4-worker
    Graphics run.

    Off for ReleaseRegression, and that is not an oversight: 833 leaves over five
    workers with wildly uneven fixtures must stay leaf-balanced or the makespan
    collapses. Where it is ON, worse balance is the correct trade -- a lane of 60
    leaves at about a minute per worker has no makespan worth protecting, and a
    split fixture produces a green that measured nothing.
    """
    assert gate.LANES["Graphics"]["coLocateFixtures"] is True
    assert gate.LANES["ReleaseRegression"]["coLocateFixtures"] is False
    assert gate.LANES["Soak"]["coLocateFixtures"] is False
    assert gate.LANES["Diagnostic"]["coLocateFixtures"] is False

    def rows(lane, sizes):
        out = []
        for index, size in enumerate(sizes):
            fixture = "Nebula.Tests.PlayMode.Capture%dTests" % index
            out.extend(leaf("L%d" % n, fixture=fixture, categories=[lane])
                       for n in range(size))
        return out

    # ON: every fixture lands whole, however lopsided that makes the split.
    graphics = rows("Graphics", [4, 3, 2, 1])
    plan, out = plan_for(root, graphics, workers=3, lane="Graphics", graphics=True)
    assert plan["coLocateFixtures"] is True, plan
    assert plan["fixtureCount"] == 4, plan
    owners = {}
    for worker in plan["workers"]:
        for group in worker["groups"]:
            for row in group["leaves"]:
                owners.setdefault(row["fixture"], set()).add(worker["worker"])
    assert all(len(seen) == 1 for seen in owners.values()), owners
    assert sum(worker["fixtureCount"] for worker in plan["workers"]) == 4, plan

    # A fixture-level group still selects by LEAF name. Handing a worker the fixture
    # name instead would select nothing: Filter.testNames is ordinal equality against
    # leaf full names, and the worker would report a clean zero-test run.
    with open(gate.worker_paths(out, plan["runId"], 1)["planPath"]) as handle:
        names = json.load(handle)["testNames"]
    assert names and all(".L" in name for name in names), names

    # The count reaches the terminal, on every lane, not just this one.
    printed = run_cli(["plan", "--manifest", os.path.join(root, "leaf-manifest.json"),
                       "--out", os.path.join(root, "cli-out"), "--repo", root,
                       "--workers", "3", "--lane", "Graphics", "--graphics",
                       "--durations", os.path.join(root, "durations.json")])
    assert "coLocateFixtures=True" in printed, printed
    assert "fixture(s)" in printed, printed

    # OFF: the release lane still balances by leaf, so one heavy fixture is spread.
    release = os.path.join(root, "release")
    plan2, _ = plan_for(release, rows("ReleaseRegression", [6, 1, 1]), workers=3,
                        lane="ReleaseRegression")
    assert plan2["coLocateFixtures"] is False, plan2
    spread = {}
    for worker in plan2["workers"]:
        for group in worker["groups"]:
            for row in group["leaves"]:
                spread.setdefault(row["fixture"], set()).add(worker["worker"])
    assert max(len(seen) for seen in spread.values()) > 1, spread
    assert sorted(w["leafCount"] for w in plan2["workers"]) == [2, 3, 3], plan2

    # And a co-locating plan that was edited rather than planned is refused.
    split = os.path.join(root, "split")
    plan3, _ = plan_for(split, rows("Graphics", [2, 2]), workers=2, lane="Graphics",
                        graphics=True)
    moved = plan3["workers"][0]["groups"][0]["leaves"].pop()
    plan3["workers"][1]["groups"][0]["leaves"].append(moved)
    reasons = gate.check_plan(plan3, {})
    assert any("requires each fixture whole in one process" in reason
               for reason in reasons), reasons


# ------------------------------------------------------------------------ runner

def main(argv=None):
    # Any arguments are substring filters over test names. Only for verifying that
    # a guard fires: break one line in a copy of the gate, run the one test that
    # names it, watch it go red. The unfiltered run is the one that counts.
    wanted = list(argv if argv is not None else sys.argv[1:])
    cases = [f for f in CASES
             if not wanted or any(token in f.__name__ for token in wanted)]
    if wanted and not cases:
        print("no test matches %s" % ", ".join(wanted))
        return 1

    failures = 0
    for function in cases:
        root = tempfile.mkdtemp(prefix="nebula-gate-")
        try:
            function(root)
            print("  ok   %s" % function.__name__)
        except Exception:
            failures += 1
            print("  FAIL %s" % function.__name__)
            print("".join("       " + line for line in
                          traceback.format_exc().splitlines(keepends=True)))
        finally:
            shutil.rmtree(root, ignore_errors=True)

    print("\n%d test(s), %d failure(s)" % (len(cases), failures))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
