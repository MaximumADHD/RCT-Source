"""Prepare Studio builds, run miners, and optionally publish client snapshots."""

import json
import re
import shutil
import subprocess
import tempfile
from pathlib import Path
from urllib.parse import quote

if __package__:
    from .tracker_common import ROOT, fetch, git, require_clean_stage, run
else:
    from tracker_common import ROOT, fetch, git, require_clean_stage, run

CDN = "https://setup.rbxcdn.com/channel/common"
API_DUMPS = ("API-Dump.json", "Full-API-Dump.json", "API-Dump-2.json")

DEPENDENCIES = {
    "RobloxApiDumpTool": "Roblox-API-Dump-Tool/RobloxApiDumpTool.csproj",
    "RobloxDeployHistory": "Roblox-Deploy-History/RobloxDeployHistory.csproj",
    "RobloxStudioModManager": "Roblox-Studio-Mod-Manager/ProjectSrc/RobloxStudioModManager.csproj",
}


def validate_version(version, guid):
    if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", version):
        raise ValueError("Version must have four numeric components.")

    if not re.fullmatch(r"version-[a-fA-F0-9]{16}", guid):
        raise ValueError("Invalid version GUID.")

    return version, guid


def resolve_version(channel, version=None, guid=None):
    if bool(version) != bool(guid):
        raise ValueError("--version and --version-guid must be supplied together.")

    if not guid:
        endpoint = "https://clientsettingscdn.roblox.com/v2/client-version/WindowsStudio64/channel/"
        url = endpoint + quote(channel, safe="")
        info = json.loads(fetch(url))
        version, guid = info["version"], info["clientVersionUpload"]
        validate_version(version, guid)

        if channel.upper() == "LIVE":
            try:
                beta = json.loads(fetch(endpoint + "zbeta"))
                beta_version, beta_guid = validate_version(beta["version"], beta["clientVersionUpload"])
                if tuple(map(int, beta_version.split("."))) > tuple(map(int, version.split("."))):
                    version, guid = beta_version, beta_guid
            except (OSError, ValueError, KeyError, TypeError):
                pass

    return validate_version(version, guid)


def download_api_dumps(build, guid):
    pending = {}

    for name in API_DUMPS:
        data = fetch(f"{CDN}/{guid}-{name}")
        document = json.loads(data)

        classes = document.get("classes" if name == "API-Dump-2.json" else "Classes")
        enums = document.get("enums" if name == "API-Dump-2.json" else "Enums")

        if not isinstance(classes, list) or not classes or not isinstance(enums, list):
            raise ValueError("Invalid or empty cloud API dump: " + name)

        pending[name] = data

    for name, data in pending.items():
        (build / name).write_bytes(data)


def publish_tools(output):
    output.mkdir(parents=True, exist_ok=True)
    projects = dict(DEPENDENCIES)
    projects["RobloxClientTracker"] = str(ROOT / "src/RobloxClientTracker.csproj")

    for name, project in projects.items():
        path = ROOT.parent / project
        print(f"Publishing {name}...", flush=True)

        run(["dotnet", "publish", path, "-c", "Release", "-p:CliBuild=true",
             "--self-contained", "false", "-p:PublishSingleFile=false",
             "--output", output / name, "--nologo"])


def invoke_tool(tools, name, *arguments):
    executable = tools / name / (name + ".dll")

    if not executable.is_file():
        raise FileNotFoundError(f"Missing {executable}; run the 'tools' command first.")

    print(f"Running {name}...", flush=True)
    subprocess.run(["dotnet", str(executable), *map(str, arguments)], check=True)


def validate_paths(build, stage, tools):
    paths = [path.resolve() for path in (build, stage, tools)]

    for index, first in enumerate(paths):
        for second in paths[index + 1:]:
            if first == second or first in second.parents or second in first.parents:
                raise ValueError("Build, stage, and tools directories must not overlap.")


def commit_stage(stage, version):
    groups = [
        (" (Compiled Scripts)", ["*.luac", "*.luac.s"]),
        (" (Scripts)", ["*.lua"]),
        (" (Shaders)", ["*.frag", "*.vert"]),
        (" (Images)", ["*.png", "*.jpg", "*.bmp", "*.gif"]),
        (" (JSON)", ["*.json"]),
        (" (CSV)", ["*.csv"]),
        ("", ["."]),
    ]

    changed = False

    for suffix, patterns in groups:
        if patterns == ["."]:
            git(stage, "add", "--all")
        else:
            candidates = git(stage, "ls-files", "--cached", "--others", "--exclude-standard", "--", *patterns)

            if not candidates:
                continue

            git(stage, "add", "--all", "--", *patterns)

        if git(stage, "diff", "--cached", "--name-only"):
            git(stage, "commit", "-m", version + suffix)
            changed = True

    return changed


def track_client(args):
    build, stage, tools = (path.resolve() for path in (args.build_dir, args.stage_dir, args.tools_dir))
    validate_paths(build, stage, tools)

    if args.push and not args.commit:
        raise ValueError("--push requires --commit.")

    if (args.miners or args.conditions) and (args.commit or args.push):
        raise ValueError("Partial miner runs cannot be published.")

    if (stage / ".git").exists() or args.commit:
        require_clean_stage(stage)
    elif stage.exists() and any(stage.iterdir()):
        raise ValueError("Non-Git stage must be empty.")

    version, guid = resolve_version(args.channel, args.version, args.version_guid)
    marker = stage / "version-guid.txt"

    if not args.force and not args.miners and marker.is_file() and marker.read_text().strip() == guid:
        print("No update: " + guid)
        return

    if args.use_build:
        if (build / "version.txt").read_text().strip() != version or (build / "version-guid.txt").read_text().strip() != guid:
            raise ValueError("Prepared build does not match the requested version and GUID.")

        download_api_dumps(build, guid)
    else:
        if build.exists():
            raise ValueError("Build directory already exists; choose an empty build path.")

        with tempfile.TemporaryDirectory(prefix="rct-build-", dir=build.parent) as temporary:
            pending_build = Path(temporary)

            invoke_tool(tools, "RobloxStudioModManager", "--output", pending_build,
                        "--channel", args.channel, "--version", version, "--version-guid", guid)

            if (pending_build / "version.txt").read_text().strip() != version or (pending_build / "version-guid.txt").read_text().strip() != guid:
                raise ValueError("Bootstrapper returned a different build version or GUID.")

            download_api_dumps(pending_build, guid)
            shutil.move(str(pending_build), str(build))

    command = ["build", "--build-dir", build, "--stage-dir", stage, "--undname", args.undname]

    if args.miners:
        command.extend(["--miners", args.miners])

    if args.conditions:
        command.extend(["--conditions", args.conditions])

    invoke_tool(tools, "RobloxClientTracker", *command)

    if args.commit and commit_stage(stage, version) and args.push:
        git(stage, "push", "origin", "HEAD")