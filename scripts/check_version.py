"""Check whether a target branch needs a client update without cloning it."""

import argparse
import os
from pathlib import Path
import re
import sys
from urllib.error import HTTPError
from urllib.parse import quote
from urllib.request import Request, urlopen

if __package__:
    from .client_tracker import resolve_version, validate_version
else:
    from client_tracker import resolve_version, validate_version


def github_read(repository, path, token):
    request = Request("https://api.github.com/repos/" + repository + "/" + path,
                      headers={"Accept": "application/vnd.github.raw+json",
                               "User-Agent": "RobloxClientTracker",
                               "X-GitHub-Api-Version": "2022-11-28"})
    
    if token:
        request.add_header("Authorization", "Bearer " + token)
    
    with urlopen(request, timeout=30) as response:
        return response.read()


def check_version(repository, branch, channel="LIVE", version=None, guid=None, force=False, token=""):
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
        raise ValueError("Repository must be owner/name.")
    
    if not branch:
        raise ValueError("Target branch is required.")
    
    version, guid = resolve_version(channel, version, guid)
    changed = force
    
    if not force:
        try:
            marker = github_read(repository, "contents/version-guid.txt?ref=" + quote(branch, safe=""), token)
        except HTTPError as error:
            if error.code != 404:
                raise

            github_read(repository, "branches/" + quote(branch, safe=""), token)
            changed = True
        else:
            current = marker.decode("utf-8-sig").strip()
            validate_version(version, current)
            changed = current != guid
    
    return {"update_required": str(changed).lower(), "version": version, "version_guid": guid}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--branch", default="roblox")
    parser.add_argument("--channel", default="LIVE")
    parser.add_argument("--version")
    parser.add_argument("--version-guid")
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--github-output", type=Path, default=os.environ.get("GITHUB_OUTPUT"))

    args = parser.parse_args()

    try:
        result = check_version(args.repository, args.branch, args.channel, args.version,
                               args.version_guid, args.force, os.environ.get("GH_TOKEN", ""))
        
        print("Client update required: " + result["update_required"] + " (" + result["version_guid"] + ")")

        if args.github_output:
            with args.github_output.open("a", encoding="utf-8") as output:
                for name, value in result.items():
                    output.write(f"{name}={value}\n")
        
        return 0
    except (ValueError, KeyError, OSError) as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())