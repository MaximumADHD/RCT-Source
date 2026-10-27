"""Shared HTTP, process, and Git helpers for the trackers."""

import subprocess
import sys
from pathlib import Path
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]


def run(arguments, cwd=None):
    try:
        return subprocess.run([str(value) for value in arguments], cwd=cwd, check=True,
                              text=True, stdout=subprocess.PIPE).stdout.strip()
    
    except subprocess.CalledProcessError as error:
        if error.stdout:
            print(error.stdout, file=sys.stderr)
        raise


def git(stage, *arguments):
    return run(["git", "-C", stage, *arguments])


def fetch(url):
    request = Request(url, headers={"User-Agent": "RobloxClientTracker"})

    with urlopen(request, timeout=120) as response:
        return response.read()


def require_clean_stage(stage):
    if Path(git(stage, "rev-parse", "--show-toplevel")).resolve() != stage.resolve():
        raise ValueError("Stage must be the root of its own Git checkout.")

    if git(stage, "status", "--porcelain", "--untracked-files=all"):
        raise ValueError("Stage checkout has local changes; refusing to overwrite them.")

    if git(stage, "rev-parse", "--abbrev-ref", "HEAD") == "HEAD":
        raise ValueError("Stage checkout must be on a branch, not a detached HEAD.")