"""Fetch, normalize, and optionally publish server FastFlags snapshots."""

import json
import re
from datetime import datetime, timezone

if __package__:
    from .tracker_common import fetch, git, require_clean_stage
else:
    from tracker_common import fetch, git, require_clean_stage

PLATFORMS = ("PCDesktopClient", "MacDesktopClient", "PlayStationClient", "XboxClient", "iOSApp",
             "UWPApp", "AndroidApp", "PCStudioApp", "MacStudioApp", "PCStudioBootstrapper",
             "MacStudioBootstrapper", "PCClientBootstrapper", "MacClientBootstrapper")

BUCKETS = ("LIVE", "zcanary", "zintegration")

PREFIXES = ("FFlag", "FString", "FInt", "FLog",
            "DFFlag", "DFString", "DFInt", "DFLog",
            "SFFlag", "SFString", "SFInt", "SFLog")


def ordered_json(value):
    if isinstance(value, dict):
        key_order = str.casefold if all(key in PLATFORMS for key in value) else None
        return {key: ordered_json(value[key]) for key in sorted(value, key=key_order)}
    if isinstance(value, (list, tuple)):
        return [ordered_json(item) for item in value]
    return value


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(ordered_json(value), indent=2, ensure_ascii=False), encoding="utf-8", newline="")


def flag_value(value):
    if not isinstance(value, str):
        return value

    if value in ("True", "False"):
        return value == "True"

    if re.fullmatch(r"[+-]?\d+", value):
        number = int(value)

        if -(2 ** 63) <= number < 2 ** 63:
            return number

    if value.startswith(("True", "False")):
        return value[0].lower() + value[1:]

    return value


def compress_flag(buckets):
    first = next(iter(buckets.values()))

    if len(buckets) == len(BUCKETS) and all(platforms == first for platforms in buckets.values()):
        value = next(iter(first.values()))
        present = set(first)

        clients = {platform for platform in PLATFORMS if not platform.endswith("Bootstrapper")}
        bootstrappers = set(PLATFORMS) - clients

        if present in (set(PLATFORMS), clients, bootstrappers) and all(other == value for other in first.values()):
            return {"Type": "Unified", "Value": value}

        return {"Type": "Platforms", "Value": first}
    
    if all(len(set(json.dumps(value) for value in platforms.values())) == 1 for platforms in buckets.values()):
        return {"Type": "Channels", "Value": {bucket: next(iter(platforms.values())) for bucket, platforms in buckets.items()}}

    return {"Type": "ChannelsAndPlatforms", "Value": buckets}


def track_flags(args):
    stage = args.stage_dir.resolve()

    if args.push and not args.commit:
        raise ValueError("--push requires --commit.")

    if (stage / ".git").exists() or args.commit:
        require_clean_stage(stage)
    elif stage.exists() and any(stage.iterdir()):
        raise ValueError("Non-Git stage must be empty.")

    flags = {}
    legacy = {platform: {} for platform in PLATFORMS}

    for bucket in BUCKETS:
        suffix = "" if bucket == "LIVE" else "/bucket/" + bucket

        for platform in PLATFORMS:
            print(f"Fetching {bucket}/{platform}...", flush=True)
            document = json.loads(fetch("https://clientsettingscdn.roblox.com/v2/settings/application/" + platform + suffix))
            settings = document.get("applicationSettings")

            if not isinstance(settings, dict) or not settings:
                raise ValueError(f"Missing or empty settings: {bucket}/{platform}")

            for name, raw in settings.items():
                if not re.fullmatch(r"[A-Za-z0-9_. -]+", name) or name in (".", ".."):
                    raise ValueError("Unsafe FastVariable name: " + name)

                value = flag_value(raw)
                flags.setdefault(name, {}).setdefault(bucket, {})[platform] = value

                if bucket == "LIVE":
                    legacy[platform][name] = value

    for platform, settings in legacy.items():
        base = "PCClientBootstrapper" if platform.endswith("Bootstrapper") else "PCDesktopClient"
        output = settings if platform == base else {name: value for name, value in settings.items() if name not in legacy[base]}
        write_json(stage / (platform + ".json"), output)

    expected = set()

    for name, buckets in flags.items():
        prefix = next((prefix for prefix in PREFIXES if name.startswith(prefix)), "Misc")
        short = name[len(prefix):] if prefix != "Misc" else name

        path = stage / "FVariables" / prefix / (short[:1] or "_") / (name + ".json")
        expected.add(path)

        write_json(path, compress_flag(buckets))

    for path in (stage / "FVariables").rglob("*.json"):
        if path not in expected:
            path.unlink()

    if args.commit:
        git(stage, "add", "--all")

        if git(stage, "diff", "--cached", "--name-only"):
            git(stage, "commit", "-m", datetime.now(timezone.utc).isoformat(timespec="seconds"))

            if args.push:
                git(stage, "push", "origin", "HEAD")