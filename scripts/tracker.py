"""One-shot build preparation, mining, and optional Git publication."""


import sys
import argparse
import subprocess

from pathlib import Path
from urllib.error import HTTPError, URLError

if __package__:
    from . import client_tracker, flag_tracker
    from .tracker_common import ROOT
else:
    import client_tracker
    import flag_tracker
    from tracker_common import ROOT


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    commands = result.add_subparsers(dest="command", required=True)

    tools = commands.add_parser("tools", help="Publish portable CLI binaries from sibling repositories.")
    tools.add_argument("--tools-dir", type=Path, default=ROOT / "artifacts/tools")

    client = commands.add_parser("client", help="Download and mine one pinned Studio build.")
    client.add_argument("--build-dir", type=Path, required=True)
    client.add_argument("--stage-dir", type=Path, required=True)
    client.add_argument("--tools-dir", type=Path, default=ROOT / "artifacts/tools")
    client.add_argument("--channel", default="LIVE")
    client.add_argument("--version")
    client.add_argument("--version-guid")
    client.add_argument("--miners")
    client.add_argument("--conditions")
    client.add_argument("--undname", default="llvm-undname")
    client.add_argument("--force", action="store_true")
    client.add_argument("--use-build", action="store_true", help="Use prepared files whose version matches the requested version.")
    client.add_argument("--commit", action="store_true")
    client.add_argument("--push", action="store_true")

    flags = commands.add_parser("flags", help="Fetch and stage one complete FastFlags snapshot.")
    flags.add_argument("--stage-dir", type=Path, required=True)
    flags.add_argument("--commit", action="store_true")
    flags.add_argument("--push", action="store_true")

    return result

def main():
    args = parser().parse_args()

    try:
        if args.command == "tools":
            client_tracker.publish_tools(args.tools_dir.resolve())
        elif args.command == "client":
            args.build_dir.parent.mkdir(parents=True, exist_ok=True)
            client_tracker.track_client(args)
        else:
            flag_tracker.track_flags(args)
        
        return 0
    except (ValueError, KeyError, OSError, HTTPError, URLError, subprocess.CalledProcessError) as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())