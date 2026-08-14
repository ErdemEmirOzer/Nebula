#!/bin/bash
#
# Run the real, non-Development macOS player against an isolated data root and
# prove afterwards that the operator's own game folders were not touched.
#
# WHY THIS SCRIPT EXISTS
# ----------------------
# A previous smoke run of the Development player was started by hand with
# "-batchmode -nographics" and no data isolation. It wrote into
# ~/Library/Application Support/DefaultCompany/Nebula -- the operator's real
# legacy folder -- and damaged a telemetry file that could not be restored.
# Every rule below is a direct consequence of that run:
#
#   * the sandbox is created here, stamped with the marker the player insists
#     on, and passed explicitly; the player refuses to isolate without it, so a
#     forgotten flag fails closed instead of silently using the real folder
#   * both real roots are snapshotted before and after, contents included, so a
#     difference is both detected and recoverable
#   * cleanup deletes exactly one directory: the one this script created, in
#     /tmp, still carrying its marker. Anything else is left alone.
#   * the player runs WITH graphics. Running it with -nographics makes the null
#     device report every shader as unsupported, which produces a Player.log
#     full of errors that describe the harness rather than the build.
#
# Usage:  Tools/Smoke/smoke_macos.sh [path/to/NEBULA.app] [results-dir]
# Exit:   0 all checks passed and the real folders are unchanged
#         1 a check failed, the player misbehaved, or a real folder changed

set -u -o pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APP="${1:-$REPO_ROOT/Builds/Release/macOS/NEBULA.app}"
RESULTS="${2:-$REPO_ROOT/Builds/Release/Reports/smoke-macos}"
SNAPSHOT="$REPO_ROOT/Tools/Smoke/user_data_snapshot.py"

# The name the player's own gate looks for. Restated here rather than shared,
# because this script and the C# constant are on opposite sides of a process
# boundary; if they ever disagree the player refuses to isolate and says so,
# which is the safe direction.
MARKER_NAME="nebula-smoke-root.marker"

# Long enough for a cold first launch on a slow disk, short enough that a hung
# player is reported as a hang rather than left running.
TIMEOUT_SECONDS="${NEBULA_SMOKE_TIMEOUT:-240}"

fail() { echo "SMOKE FAILED: $*" >&2; exit 1; }

[ -d "$APP" ] || fail "no app bundle at $APP (build it with Nebula/Build Release macOS)"
EXE="$APP/Contents/MacOS/$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist" 2>/dev/null)"
[ -x "$EXE" ] || fail "no executable inside $APP"

mkdir -p "$RESULTS" || fail "cannot create $RESULTS"

# --------------------------------------------------------------------------
# The sandbox. Created under /tmp with a name that is unmistakably ours, so the
# cleanup guard below can refuse anything that is not exactly this shape.
# --------------------------------------------------------------------------
SANDBOX="/tmp/nebula-smoke-$$-$(date +%Y%m%d%H%M%S)"
mkdir -p "$SANDBOX/active" "$SANDBOX/legacy" || fail "cannot create sandbox $SANDBOX"
printf 'created by Tools/Smoke/smoke_macos.sh for pid %s at %s\n' "$$" "$(date -u +%FT%TZ)" \
    > "$SANDBOX/$MARKER_NAME"

cleanup() {
    # Deliberately paranoid, and deliberately not "rm -rf $SANDBOX". Three
    # independent conditions, each of which alone would be enough to prevent
    # the worst case, because the worst case here is deleting somebody's data
    # with a script whose entire purpose is not deleting somebody's data.
    case "$SANDBOX" in
        /tmp/nebula-smoke-*) ;;
        *) echo "refusing to clean up unexpected path: $SANDBOX" >&2; return ;;
    esac
    [ -d "$SANDBOX" ] || return
    [ -f "$SANDBOX/$MARKER_NAME" ] || {
        echo "refusing to clean up $SANDBOX: our marker is gone" >&2; return
    }
    rm -rf "$SANDBOX"
}
trap cleanup EXIT

echo "sandbox : $SANDBOX"
echo "app     : $APP"
echo "results : $RESULTS"

# --------------------------------------------------------------------------
# Before
# --------------------------------------------------------------------------
python3 "$SNAPSHOT" snapshot --output "$RESULTS/user-data-before.json" \
    || fail "could not snapshot the real user data folders"

# --------------------------------------------------------------------------
# The run itself.
#
# -nebulaSmoke        arms the data isolation (and nothing else on a release
#                     player: the Development smoke runner is gated on
#                     Debug.isDebugBuild and stays inert here)
# -nebulaDataRoot     the sandbox, absolute, under /tmp, marker present
# -nebulaReleaseCheck the release visibility probe, the only smoke that runs on
#                     a non-Development player
# windowed 1280x720   so the run does not take over the operator's display
# --------------------------------------------------------------------------
PLAYER_LOG="$SANDBOX/Player.log"
PROBE_JSON="$SANDBOX/release-visibility.json"

"$EXE" \
    -nebulaSmoke \
    -nebulaDataRoot "$SANDBOX" \
    -nebulaReleaseCheck \
    -nebulaReleaseCheckOutput "$PROBE_JSON" \
    -logFile "$PLAYER_LOG" \
    -screen-fullscreen 0 -screen-width 1280 -screen-height 720 &
PLAYER_PID=$!

WAITED=0
while kill -0 "$PLAYER_PID" 2>/dev/null; do
    sleep 2
    WAITED=$((WAITED + 2))
    if [ "$WAITED" -ge "$TIMEOUT_SECONDS" ]; then
        kill -9 "$PLAYER_PID" 2>/dev/null
        PLAYER_STATUS="timeout after ${TIMEOUT_SECONDS}s"
        break
    fi
done
if [ -z "${PLAYER_STATUS:-}" ]; then
    wait "$PLAYER_PID"
    PLAYER_STATUS="exit $?"
fi
echo "player  : $PLAYER_STATUS"

# Copied out before cleanup, because the sandbox is about to stop existing.
[ -f "$PLAYER_LOG" ] && cp "$PLAYER_LOG" "$RESULTS/Player.log"
[ -f "$PROBE_JSON" ] && cp "$PROBE_JSON" "$RESULTS/release-visibility.json"
# What the run left behind in the sandbox, so "the writes landed here" is
# evidence and not an inference from the real folders being unchanged.
find "$SANDBOX" -type f | sed "s|^$SANDBOX/||" | sort > "$RESULTS/sandbox-contents.txt"

# --------------------------------------------------------------------------
# After
# --------------------------------------------------------------------------
python3 "$SNAPSHOT" snapshot --output "$RESULTS/user-data-after.json" \
    || fail "could not re-snapshot the real user data folders"

python3 "$SNAPSHOT" compare "$RESULTS/user-data-before.json" "$RESULTS/user-data-after.json" \
    --output "$RESULTS/user-data-diff.json"
DATA_UNCHANGED=$?

# --------------------------------------------------------------------------
# Player.log scan. Each pattern is a class of failure that does not necessarily
# fail a check but always means the build is wrong.
# --------------------------------------------------------------------------
SCAN="$RESULTS/player-log-scan.txt"
: > "$SCAN"
LOG_PROBLEMS=0
if [ -f "$RESULTS/Player.log" ]; then
    while IFS='|' read -r label pattern; do
        [ -z "$label" ] && continue
        hits=$(grep -icE "$pattern" "$RESULTS/Player.log" 2>/dev/null || true)
        printf '%-28s %s\n' "$label" "$hits" >> "$SCAN"
        [ "${hits:-0}" -gt 0 ] && LOG_PROBLEMS=$((LOG_PROBLEMS + hits))
    done <<'PATTERNS'
compile errors|error CS[0-9]
exceptions|Exception:|UnhandledException
null reference|NullReferenceException
missing asset/material|missing (asset|material|prefab|reference)|The referenced script
shader error|Shader error|error in shader|Shader .* not supported
error shader|Hidden/InternalErrorShader
import error|Failed to import|import error
save migration error|migration failed|Legacy data migration failed
duplicate singleton|already exists|duplicate singleton
orphan overlay|orphan overlay|orphan loading
PATTERNS
    echo "" >> "$SCAN"
    echo "total flagged lines: $LOG_PROBLEMS" >> "$SCAN"
    cat "$SCAN"
else
    echo "no Player.log was produced" | tee -a "$SCAN"
    LOG_PROBLEMS=1
fi

# --------------------------------------------------------------------------
# Verdict
# --------------------------------------------------------------------------
STATUS=0
if [ "$DATA_UNCHANGED" -ne 0 ]; then
    echo "FAIL: the real user data folders CHANGED -- see $RESULTS/user-data-diff.json" >&2
    STATUS=1
else
    echo "OK  : the real user data folders are byte-identical"
fi

if [ -f "$RESULTS/release-visibility.json" ]; then
    python3 - "$RESULTS/release-visibility.json" <<'PY'
import json, sys
report = json.load(open(sys.argv[1]))
failed = [c for c in report.get("checks", []) if not c.get("passed")]
print("probe   : %d check(s), %d failed" % (len(report.get("checks", [])), len(failed)))
for check in failed:
    print("  FAIL %s: %s" % (check["name"], check.get("detail", "")))
sys.exit(0 if report.get("allPassed") else 1)
PY
    [ $? -ne 0 ] && STATUS=1
else
    echo "FAIL: the probe wrote no report" >&2
    STATUS=1
fi

[ "$LOG_PROBLEMS" -gt 0 ] && { echo "FAIL: Player.log has flagged lines -- see $SCAN" >&2; STATUS=1; }
case "$PLAYER_STATUS" in
    "exit 0") ;;
    *) echo "FAIL: player finished with: $PLAYER_STATUS" >&2; STATUS=1 ;;
esac

echo "verdict : $([ $STATUS -eq 0 ] && echo PASS || echo FAIL)"
exit $STATUS
