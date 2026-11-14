#!/usr/bin/env python3
"""
Stomper: run the CI checks locally and stamp the commit when they pass.

Stomper runs the same checks as the "Build & Test Debug" and "YAML Linter"
workflows. If all of them pass, it adds a `Stomper:` trailer to the commit.
The trailer records the git tree hash that was tested. On a pull request, CI
reads the trailer and skips the heavy jobs if the tree is still the same.

The stamp is bound to the tree, not to the commit SHA, so adding the trailer
(which changes the SHA but not the tree) does not invalidate it.

Usage:
    python Tools/_PS/stomper/stomper.py            # run checks, then stamp
    python Tools/_PS/stomper/stomper.py --verify   # only check the HEAD stamp

How the stamp is written:
    - If HEAD is not on any remote branch, Stomper amends HEAD.
    - If HEAD is already pushed, Stomper adds an empty commit, so that you
      do not have to force-push.
"""

import argparse
import os
import subprocess
import sys
import time

TRAILER_KEY = "Stomper"
CHECKS = ["build", "unit", "integration", "yaml"]
CONFIGURATION = "DebugOpt"


def git(*args: str, check: bool = True) -> str:
    result = subprocess.run(["git", *args], capture_output=True, text=True, check=check)
    return result.stdout.strip()


def fail(message: str) -> None:
    print(f"stomper: {message}", file=sys.stderr)
    sys.exit(1)


def head_tree() -> str:
    return git("rev-parse", "HEAD^{tree}")


def head_stamp() -> str | None:
    """Return the tree hash from the newest Stomper trailer on HEAD, if any."""
    values = git("log", "-1", f"--format=%(trailers:key={TRAILER_KEY},valueonly)", "HEAD")
    lines = [line.strip() for line in values.splitlines() if line.strip()]
    if not lines:
        return None
    return lines[-1].split()[0]


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
        fail(f"{name} failed (exit code {result.returncode}) after {elapsed:.0f} s. HEAD is not stamped.")
    print(f"=== stomper: {name} passed in {elapsed:.0f} s ===", flush=True)


def run_checks() -> None:
    # Keep test output inside .git so that it never makes the tree dirty.
    results = git("rev-parse", "--git-path", "stomper-results")
    run_step("build", ["dotnet", "build", "--configuration", CONFIGURATION, "/m"])
    run_step("unit", [
        "dotnet", "test", "--no-build", "--configuration", CONFIGURATION,
        "Content.Tests/Content.Tests.csproj", "--",
        "NUnit.ConsoleOut=0", f"NUnit.WorkDirectory={os.path.abspath(results)}",
    ])
    run_step("integration", [
        "dotnet", "test", "--no-build", "--configuration", CONFIGURATION,
        "Content.IntegrationTests/Content.IntegrationTests.csproj", "--",
        "NUnit.ConsoleOut=0", "NUnit.MapWarningTo=Failed",
        f"NUnit.WorkDirectory={os.path.abspath(results)}",
    ], env={"DOTNET_gcServer": "1"})
    run_step("yaml", [
        "dotnet", "run", "--no-build", "--configuration", CONFIGURATION,
        "--project", "Content.YAMLLinter/Content.YAMLLinter.csproj",
    ])


def head_is_pushed() -> bool:
    return bool(git("branch", "--remotes", "--contains", "HEAD", check=False))


def stamp(tree: str) -> None:
    trailer = f"{TRAILER_KEY}: {tree} {','.join(CHECKS)}"
    if head_is_pushed():
        git("commit", "--allow-empty", "--no-verify", "-m", "Stomper: local CI passed", "--trailer", trailer)
        how = "added an empty commit"
    else:
        git("commit", "--amend", "--no-edit", "--no-verify", "--trailer", trailer)
        how = "amended HEAD"

    if head_tree() != tree:
        fail("internal error: the tree changed while stamping.")
    print(f"\nstomper: all checks passed. Stomper {how}: {git('rev-parse', '--short', 'HEAD')}")
    print(f"stomper: {trailer}")
    print("stomper: push the branch. CI skips the checks that Stomper already ran.")


def main() -> None:
    parser = argparse.ArgumentParser(description="Run the CI checks locally and stamp the commit.")
    parser.add_argument("--verify", action="store_true", help="only check that HEAD has a valid stamp")
    args = parser.parse_args()

    os.chdir(git("rev-parse", "--show-toplevel"))

    if args.verify:
        stamped = head_stamp()
        if stamped is None:
            fail("HEAD has no Stomper stamp.")
        if stamped != head_tree():
            fail(f"the stamp is for tree {stamped}, but HEAD has tree {head_tree()}.")
        print(f"stomper: HEAD has a valid stamp for tree {stamped}.")
        return

    ensure_clean()
    tree = head_tree()
    if head_stamp() == tree:
        print(f"stomper: HEAD is already stamped for tree {tree}. Nothing to do.")
        return

    run_checks()

    if head_tree() != tree:
        fail("HEAD changed while the checks ran. HEAD is not stamped.")
    ensure_clean()
    stamp(tree)


if __name__ == "__main__":
    main()
