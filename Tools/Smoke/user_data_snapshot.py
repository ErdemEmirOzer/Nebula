#!/usr/bin/env python3
"""Prove a smoke run did not touch the operator's real game data.

Why this exists
---------------
A smoke run of the Development player was once started without data isolation.
It rewrote ``~/Library/Application Support/DefaultCompany/Nebula/nebula/
telemetry/funnel-index.json`` on the operator's own machine, and the file could
not be restored: only its *hash* had been recorded beforehand, not its contents.
A hash tells you that something changed. It does not give you back what was
there.

So this tool records both. ``snapshot`` writes the digest, size and mtime of
every file under the two real per-app roots, and -- with ``--keep-contents`` --
the bytes of every small file as well, so a damaged file can actually be put
back rather than merely mourned. ``compare`` diffs two snapshots and exits
non-zero on any difference at all, including a mtime that moved while the bytes
stayed the same: a file that was rewritten with identical contents still means
something wrote where it should not have.

Usage
-----
    python3 user_data_snapshot.py snapshot --output before.json
    ...run the smoke...
    python3 user_data_snapshot.py snapshot --output after.json
    python3 user_data_snapshot.py compare before.json after.json

Exit codes: 0 identical, 1 differences found, 2 usage error.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import sys
from typing import Any, Dict, List, Optional

#: The two per-app roots this project has ever written to on macOS. The legacy
#: pair is the one the incident damaged; the active pair is where the renamed
#: build writes now. Both are watched, because a migration reads one and writes
#: the other and either side being touched is a failure of isolation.
MACOS_ROOTS = (
    "~/Library/Application Support/DefaultCompany/Nebula",
    "~/Library/Application Support/Erdem/NEBULA",
)

#: The Windows equivalents, listed so a snapshot taken on Windows watches the
#: same two identities rather than silently watching nothing.
WINDOWS_ROOTS = (
    "~/AppData/LocalLow/DefaultCompany/Nebula",
    "~/AppData/LocalLow/Erdem/NEBULA",
)

#: Files at or under this size have their contents kept verbatim in the
#: snapshot with --keep-contents. Every file this project writes -- saves,
#: rebinds, the funnel index, the session log -- is comfortably under it, and
#: the cap stops a stray capture turning a snapshot into a backup of the disk.
MAX_KEPT_BYTES = 4 * 1024 * 1024


def default_roots() -> List[str]:
    return list(WINDOWS_ROOTS if os.name == "nt" else MACOS_ROOTS)


def sha256_file(path: str) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def snapshot_root(root: str, keep_contents: bool) -> Dict[str, Any]:
    """Every file under one root, or an explicit record that the root is absent.

    An absent root is recorded rather than skipped: "the folder did not exist
    before and does exist after" is one of the failures this is looking for,
    and a skipped root cannot express it.
    """
    expanded = os.path.abspath(os.path.expanduser(root))
    entry: Dict[str, Any] = {
        "root": root,
        "resolved": expanded,
        "exists": os.path.isdir(expanded),
        "files": {},
        "directories": [],
        "errors": [],
    }
    if not entry["exists"]:
        return entry

    for dirpath, dirnames, filenames in os.walk(expanded):
        dirnames.sort()
        rel_dir = os.path.relpath(dirpath, expanded).replace(os.sep, "/")
        if rel_dir != ".":
            entry["directories"].append(rel_dir)
        for filename in sorted(filenames):
            full = os.path.join(dirpath, filename)
            rel = os.path.relpath(full, expanded).replace(os.sep, "/")
            try:
                stat = os.lstat(full)
                record: Dict[str, Any] = {
                    "size": stat.st_size,
                    "mtime": round(stat.st_mtime, 6),
                    "mode": oct(stat.st_mode),
                }
                if os.path.islink(full):
                    record["symlink"] = os.readlink(full)
                else:
                    record["sha256"] = sha256_file(full)
                    if keep_contents and stat.st_size <= MAX_KEPT_BYTES:
                        with open(full, "rb") as handle:
                            record["contents_b64"] = base64.b64encode(handle.read()).decode("ascii")
                entry["files"][rel] = record
            except OSError as exc:
                entry["errors"].append("%s: %s" % (rel, exc))

    entry["directories"].sort()
    return entry


def take_snapshot(roots: List[str], keep_contents: bool) -> Dict[str, Any]:
    return {
        "snapshotSchema": 1,
        "keptContents": keep_contents,
        "roots": [snapshot_root(root, keep_contents) for root in roots],
    }


def compare(before: Dict[str, Any], after: Dict[str, Any]) -> List[str]:
    """Every way the two snapshots disagree, in words.

    Compared field by field rather than by hashing the whole snapshot: "these
    differ" is not a useful thing to tell somebody whose save folder may have
    been rewritten. Which file, and how, is.
    """
    problems: List[str] = []
    before_roots = {r["resolved"]: r for r in before.get("roots", [])}
    after_roots = {r["resolved"]: r for r in after.get("roots", [])}

    for resolved in sorted(set(before_roots) | set(after_roots)):
        left = before_roots.get(resolved)
        right = after_roots.get(resolved)
        if left is None or right is None:
            problems.append("%s: present in only one snapshot" % resolved)
            continue

        if left["exists"] != right["exists"]:
            problems.append("%s: existence changed %s -> %s"
                            % (resolved, left["exists"], right["exists"]))

        left_files, right_files = left["files"], right["files"]
        for rel in sorted(set(left_files) - set(right_files)):
            problems.append("%s/%s: DELETED" % (resolved, rel))
        for rel in sorted(set(right_files) - set(left_files)):
            problems.append("%s/%s: CREATED" % (resolved, rel))
        for rel in sorted(set(left_files) & set(right_files)):
            a, b = left_files[rel], right_files[rel]
            for field in ("sha256", "size", "mtime", "mode", "symlink"):
                if a.get(field) != b.get(field):
                    problems.append("%s/%s: %s %r -> %r"
                                    % (resolved, rel, field, a.get(field), b.get(field)))

        for rel in sorted(set(left["directories"]) ^ set(right["directories"])):
            problems.append("%s/%s: directory added or removed" % (resolved, rel))

    return problems


def restore(snapshot: Dict[str, Any], only: List[str], dry_run: bool) -> List[str]:
    """Put files back to the bytes a snapshot recorded.

    The half of this tool that the original incident lacked. It restores only
    files whose contents were actually captured, only files that differ right
    now, and never creates a file the snapshot did not have -- a "restore" that
    invented files, or that wrote over something newer without saying so, would
    be a second way to lose data rather than the cure for the first.
    """
    actions: List[str] = []
    for root in snapshot.get("roots", []):
        base = root["resolved"]
        for rel, record in sorted(root.get("files", {}).items()):
            if only and not any(fragment in rel for fragment in only):
                continue
            if "contents_b64" not in record:
                continue
            target = os.path.join(base, rel.replace("/", os.sep))
            payload = base64.b64decode(record["contents_b64"])
            if os.path.isfile(target):
                with open(target, "rb") as handle:
                    if handle.read() == payload:
                        continue
                verb = "RESTORE (differs)"
            else:
                verb = "RESTORE (missing)"

            actions.append("%s %s" % (verb, target))
            if dry_run:
                continue
            os.makedirs(os.path.dirname(target), exist_ok=True)
            with open(target, "wb") as handle:
                handle.write(payload)
            # The mtime is part of what was snapshotted, so putting the bytes
            # back without it would leave the folder still visibly disturbed.
            if "mtime" in record:
                os.utime(target, (record["mtime"], record["mtime"]))
    return actions


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        prog="user_data_snapshot.py",
        description="Snapshot and compare the operator's real Nebula data folders.")
    sub = parser.add_subparsers(dest="command", required=True)

    take = sub.add_parser("snapshot", help="record the current state of both real roots")
    take.add_argument("--root", action="append", default=None,
                      help="override the watched roots (repeatable)")
    take.add_argument("--output", required=True, help="where to write the snapshot JSON")
    take.add_argument("--keep-contents", action="store_true", default=True,
                      help="store the bytes of small files so damage is recoverable (default)")
    take.add_argument("--no-keep-contents", dest="keep_contents", action="store_false",
                      help="hashes and metadata only")

    diff = sub.add_parser("compare", help="diff two snapshots; non-zero exit on any change")
    diff.add_argument("before")
    diff.add_argument("after")
    diff.add_argument("--output", default=None, help="also write the differences as JSON")

    back = sub.add_parser("restore", help="put files back to the bytes a snapshot recorded")
    back.add_argument("snapshot")
    back.add_argument("--only", action="append", default=None,
                      help="restore only paths containing this fragment (repeatable)")
    back.add_argument("--dry-run", action="store_true", help="print what would change, write nothing")

    args = parser.parse_args(argv)

    if args.command == "snapshot":
        roots = args.root or default_roots()
        data = take_snapshot(roots, args.keep_contents)
        with open(args.output, "w", encoding="utf-8") as handle:
            json.dump(data, handle, indent=2, ensure_ascii=False)
            handle.write("\n")
        total = sum(len(r["files"]) for r in data["roots"])
        print("snapshot: %d file(s) across %d root(s) -> %s" % (total, len(roots), args.output))
        for root in data["roots"]:
            print("  %s: %s, %d file(s)"
                  % (root["resolved"], "present" if root["exists"] else "ABSENT",
                     len(root["files"])))
        return 0

    if args.command == "restore":
        with open(args.snapshot, "r", encoding="utf-8") as handle:
            data = json.load(handle)
        if not data.get("keptContents"):
            print("error: that snapshot has no file contents; it cannot restore anything",
                  file=sys.stderr)
            return 2
        actions = restore(data, args.only or [], args.dry_run)
        if not actions:
            print("nothing to restore: every captured file already matches the snapshot.")
            return 0
        for line in actions:
            print(("would " if args.dry_run else "") + line)
        return 0

    with open(args.before, "r", encoding="utf-8") as handle:
        before = json.load(handle)
    with open(args.after, "r", encoding="utf-8") as handle:
        after = json.load(handle)

    problems = compare(before, after)
    if args.output:
        with open(args.output, "w", encoding="utf-8") as handle:
            json.dump({"identical": not problems, "differences": problems}, handle,
                      indent=2, ensure_ascii=False)
            handle.write("\n")

    if not problems:
        print("IDENTICAL: the real user data folders were not touched.")
        return 0

    print("CHANGED: %d difference(s) in the real user data folders:" % len(problems))
    for line in problems:
        print("  %s" % line)
    return 1


if __name__ == "__main__":
    sys.exit(main())
