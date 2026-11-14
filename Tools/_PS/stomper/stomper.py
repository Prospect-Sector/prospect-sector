#!/usr/bin/env python3
"""
Stomper: run the CI checks locally, then stamp the commit on GitHub.

Stomper runs the same checks as the "Build & Test Debug" and "YAML Linter"
workflows. If all of them pass, Stomper records the git tree hash locally.
When you push, the pre-push hook (BuildChecker/hooks/pre-push) starts a
background process. That process waits until the push is done, then posts a
"Stomper" commit status on GitHub. On a pull request, CI reads that status
and skips its heavy jobs.

The local record is bound to the tree, not to the commit SHA. A commit with
the same tree (for example after `git commit --amend` of the message only)
is still stampable.

Usage:
    python Tools/_PS/stomper/stomper.py           # run the checks, record the tree
    python Tools/_PS/stomper/stomper.py status    # show the stamp state of HEAD

Used by the hook:
    stomper.py hook <remote-url> <git-pid> <sha>...   # start the background poster
    stomper.py post <remote-url> <git-pid> <sha>...   # wait for the push, post the status

Requirements: the GitHub CLI (`gh`), logged in with write access to the repo.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

CONTEXT = "Stomper"
CHECKS = ["build", "unit", "integration", "yaml"]
CONFIGURATION = "DebugOpt"
# How long the poster retries until GitHub knows the pushed commit.
KNOWN_TIMEOUT_S = 30
# How long the poster waits for `git push` to exit before it gives up.
PUSH_TIMEOUT_S = 15 * 60


def git(*args: str, check: bool = True) -> str:
    result = subprocess.run(["git", *args], capture_output=True, text=True, check=check)
    return result.stdout.strip()


def fail(message: str) -> None:
    print(f"stomper: {message}", file=sys.stderr)
    sys.exit(1)


def state_dir() -> Path:
    # The common git dir is shared by all worktrees. Trees are content
    # addressed, so a record from one worktree is valid in all of them.
    path = Path(git("rev-parse", "--path-format=absolute", "--git-common-dir")) / "stomper"
    (path / "passed").mkdir(parents=True, exist_ok=True)
    return path


def tree_of(rev: str) -> str:
    return git("rev-parse", f"{rev}^{{tree}}")


def record_path(tree: str) -> Path:
    return state_dir() / "passed" / tree


def has_passed(tree: str) -> bool:
    return record_path(tree).is_file()


# --- checks ---------------------------------------------------------------

def ensure_clean() -> None:
    status = git("status", "--porcelain", "--ignore-submodules=none")
    if status:
        fail("the working tree is not clean. Commit or remove these changes first:\n" + status)

    for line in git("submodule", "status", "--recursive").splitlines():
        if line[:1] in ("+", "-", "U"):
            fail(f"a submodule does not match the commit: {line.strip()}\n"
                 "Run: git submodule update --init --recursive")


def run_step(name: str, command: list[str], env: dict[str, str] | None = None) -> None:
    print(f"\n=== stomper: {name} ===\n$ {' '.join(command)}", flush=True)
    start = time.monotonic()
    result = subprocess.run(command, env={**os.environ, **(env or {})})
    elapsed = time.monotonic() - start
    if result.returncode != 0:
        fail(f"{name} failed (exit code {result.returncode}) after {elapsed:.0f} s. Nothing is stamped.")
    print(f"=== stomper: {name} passed in {elapsed:.0f} s ===", flush=True)


def run_checks() -> None:
    results = str(state_dir() / "results")
    run_step("build", ["dotnet", "build", "--configuration", CONFIGURATION, "/m"])
    run_step("unit", [
        "dotnet", "test", "--no-build", "--configuration", CONFIGURATION,
        "Content.Tests/Content.Tests.csproj", "--",
        "NUnit.ConsoleOut=0", f"NUnit.WorkDirectory={results}",
    ])
    run_step("integration", [
        "dotnet", "test", "--no-build", "--configuration", CONFIGURATION,
        "Content.IntegrationTests/Content.IntegrationTests.csproj", "--",
        "NUnit.ConsoleOut=0", "NUnit.MapWarningTo=Failed", f"NUnit.WorkDirectory={results}",
    ], env={"DOTNET_gcServer": "1"})
    run_step("yaml", [
        "dotnet", "run", "--no-build", "--configuration", CONFIGURATION,
        "--project", "Content.YAMLLinter/Content.YAMLLinter.csproj",
    ])


def cmd_run() -> None:
    ensure_clean()
    tree = tree_of("HEAD")
    if not has_passed(tree):
        run_checks()
        if tree_of("HEAD") != tree:
            fail("HEAD changed while the checks ran. Nothing is stamped.")
        ensure_clean()
        record_path(tree).write_text(json.dumps({
            "checks": CHECKS,
            "commit": git("rev-parse", "HEAD"),
            "time": datetime.now(timezone.utc).isoformat(),
        }))
        print(f"\nstomper: all checks passed for tree {tree}.")
    else:
        print(f"stomper: tree {tree} already passed the checks.")

    # If HEAD is already on GitHub, post the status now. Otherwise the
    # pre-push hook posts it after the next push.
    repo = repo_from_url(git("remote", "get-url", "origin", check=False))
    sha = git("rev-parse", "HEAD")
    if repo and commit_known(repo, sha):
        post_status(repo, sha, tree)
    else:
        print("stomper: push the branch. The pre-push hook posts the stamp after the push.")


# --- GitHub ---------------------------------------------------------------

def repo_from_url(url: str) -> str | None:
    match = re.search(r"github\.com[:/]([^/]+)/([^/]+?)(?:\.git)?/?$", url.strip())
    return f"{match.group(1)}/{match.group(2)}" if match else None


def gh(*args: str) -> subprocess.CompletedProcess:
    if shutil.which("gh") is None:
        return subprocess.CompletedProcess(["gh", *args], 127, "", "the GitHub CLI (gh) is not installed")
    return subprocess.run(["gh", *args], capture_output=True, text=True)


def commit_known(repo: str, sha: str) -> bool:
    return gh("api", f"repos/{repo}/commits/{sha}", "--silent").returncode == 0


def post_status(repo: str, sha: str, tree: str) -> bool:
    result = gh("api", "--method", "POST", f"repos/{repo}/statuses/{sha}",
                "-f", "state=success",
                "-f", f"context={CONTEXT}",
                "-f", f"description={', '.join(CHECKS)} passed locally (tree {tree[:10]})")
    if result.returncode != 0:
        print(f"stomper: could not post the status on {sha[:10]}: {result.stderr.strip()}")
        return False
    print(f"stomper: posted the {CONTEXT} status on {repo}@{sha[:10]}.")
    return True


def pid_alive(pid: int) -> bool:
    if os.name == "nt":
        # On Windows, os.kill() terminates the process, and $PPID from Git
        # Bash is not a Windows PID. Do not wait; retry longer instead.
        return False
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


# --- hook -----------------------------------------------------------------

def stampable(shas: list[str]) -> list[tuple[str, str]]:
    found = []
    for sha in shas:
        commit = git("rev-parse", "--verify", "--quiet", f"{sha}^{{commit}}", check=False)
        if not commit:
            continue
        tree = tree_of(commit)
        if has_passed(tree) and (commit, tree) not in found:
            found.append((commit, tree))
    return found


def cmd_hook(url: str, git_pid: int, shas: list[str]) -> None:
    """Runs inside pre-push. Must be fast and must never block the push."""
    repo = repo_from_url(url)
    if not repo or not stampable(shas):
        return

    log = state_dir() / "post.log"
    with open(log, "a") as out:
        subprocess.Popen(
            [sys.executable, os.path.abspath(__file__), "post", url, str(git_pid), *shas],
            stdin=subprocess.DEVNULL, stdout=out, stderr=out,
            start_new_session=True,
        )
    print(f"stomper: posting the stamp after the push (log: {log})")


def cmd_post(url: str, git_pid: int, shas: list[str]) -> None:
    """Runs in the background. Waits for the push, then posts the status."""
    repo = repo_from_url(url)
    print(f"--- {datetime.now(timezone.utc).isoformat()} stomper post {repo} {' '.join(s[:10] for s in shas)}")
    if not repo:
        return

    deadline = time.monotonic() + PUSH_TIMEOUT_S
    while pid_alive(git_pid) and time.monotonic() < deadline:
        time.sleep(1)

    # Without a PID to wait for (Windows), allow time for the push itself.
    known_timeout = KNOWN_TIMEOUT_S if os.name != "nt" else KNOWN_TIMEOUT_S + 5 * 60

    for sha, tree in stampable(shas):
        known_deadline = time.monotonic() + known_timeout
        while not commit_known(repo, sha):
            if time.monotonic() >= known_deadline:
                print(f"stomper: GitHub does not know {sha[:10]} after {known_timeout} s. "
                      "Was the push rejected? Nothing is stamped.")
                break
            time.sleep(2)
        else:
            post_status(repo, sha, tree)


# --- status ---------------------------------------------------------------

def cmd_status() -> None:
    sha = git("rev-parse", "HEAD")
    tree = tree_of("HEAD")
    print(f"HEAD {sha[:10]}, tree {tree[:10]}")
    print(f"  local checks: {'passed' if has_passed(tree) else 'not run for this tree'}")
    repo = repo_from_url(git("remote", "get-url", "origin", check=False))
    if not repo:
        return
    result = gh("api", f"repos/{repo}/commits/{sha}/status",
                "--jq", f'.statuses[] | select(.context=="{CONTEXT}") | .state')
    if result.returncode != 0:
        print("  GitHub: the commit is not on GitHub yet")
    else:
        print(f"  GitHub: {result.stdout.strip() or 'no Stomper status'}")


def main() -> None:
    parser = argparse.ArgumentParser(description="Run the CI checks locally and stamp the commit on GitHub.")
    sub = parser.add_subparsers(dest="command")
    sub.add_parser("status", help="show the stamp state of HEAD")
    for name in ("hook", "post"):
        p = sub.add_parser(name, help=argparse.SUPPRESS)
        p.add_argument("url")
        p.add_argument("git_pid", type=int)
        p.add_argument("shas", nargs="+")
    args = parser.parse_args()

    os.chdir(git("rev-parse", "--show-toplevel"))

    if args.command == "status":
        cmd_status()
    elif args.command == "hook":
        cmd_hook(args.url, args.git_pid, args.shas)
    elif args.command == "post":
        cmd_post(args.url, args.git_pid, args.shas)
    else:
        cmd_run()


if __name__ == "__main__":
    main()
