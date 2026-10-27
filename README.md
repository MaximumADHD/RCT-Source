# RCT-Source
The backend for Roblox-Client-Tracker. Runs on Linux or Windows without launching
Roblox Studio, Wine, or a desktop session.

## Architecture

- [scripts/tracker.py](scripts/tracker.py) is the CLI dispatcher for the unchanged
	`tools`, `client`, and `flags` commands. Every invocation runs once; GitHub Actions
	provides scheduling.
- [scripts/client_tracker.py](scripts/client_tracker.py) handles tool publication,
	version resolution, installation, cloud API dumps, mining, and client publication.
- [scripts/flag_tracker.py](scripts/flag_tracker.py) handles server FastFlags
	snapshots, value normalization, redundancy reduction, and FastFlags publication.
- [scripts/tracker_common.py](scripts/tracker_common.py) contains shared HTTP,
	process, and Git helpers. The lightweight version check imports the client
	resolver directly, without loading the CLI or FastFlags implementation.
- The .NET 10 tracker mines an already prepared **build** directory into a separate
	**stage** directory. It does not install Studio, manage Git, or poll for updates.
- Mod Manager's CLI extracts the Windows Studio packages without executing them.
	All three API dumps come from the CDN, pinned to the same build GUID.
- FVariables are scanned statically from the PE executable and Lua sources. A
	broken C++ scanner fails the build instead of launching Studio or silently
	publishing an empty list.
- Qt extraction is managed C#: x64 PE registration scanning, Qt resource tables
	v1-v3, UTF-16 names, and plain/zlib resource data. No Rust toolchain is needed.
	Unsupported compression or registration patterns fail explicitly.
- C++ type extraction uses `dbghelp` on Windows and `llvm-undname` on Linux.

## Local Setup

Install the .NET 10 SDK, Python 3.10+, and Git. On Linux, also install LLVM
(`llvm-undname` must be on `PATH`, or pass `--undname /path/to/llvm-undname`).
Python uses only its standard library.

Clone these repositories as siblings, retaining their directory names. The
dependency revisions must include their portable CLI implementations:

```text
workspace/
	RCT-Source/
	Roblox-Studio-Mod-Manager/
	Roblox-Deploy-History/
	Roblox-API-Dump-Tool/
	Roblox-File-Format/
```

Publish all tools from RCT-Source:

```sh
python scripts/tracker.py tools
```

This writes portable tools to `artifacts/tools/<tool-name>/`. The equivalent
direct tracker build requires a **global** CLI property so NuGet restore selects
the portable dependency targets:

```sh
dotnet build src/RobloxClientTracker.csproj -c Release -p:CliBuild=true
```

Generate a snapshot without committing or pushing:

```sh
python scripts/tracker.py client --build-dir artifacts/build --stage-dir artifacts/stage
```

The build path must not already exist. The stage must be empty or a clean,
separate Git checkout. Build, stage, and tools paths must not overlap. On failure,
inspect the files and use a new empty stage for a retry; the runner never discards
local changes automatically.

For reproducible historical builds, pass **both** `--version 0.744.0.7441077` and
`--version-guid version-7aaa6838d36c4448`. `--channel` defaults to `LIVE`; restricted
channels use `RBX_CHANNEL_TOKEN`. Unpinned LIVE checks also probe `zbeta` through
`clientsettingscdn.roblox.com` and select it only when its valid version is newer.
Offline or invalid `zbeta` responses fall back to LIVE. Explicit channels and
pinned version/GUID pairs are unchanged. Cloud dumps must exist for that exact GUID:

```text
https://setup.rbxcdn.com/channel/common/<guid>-API-Dump.json
https://setup.rbxcdn.com/channel/common/<guid>-Full-API-Dump.json
https://setup.rbxcdn.com/channel/common/<guid>-API-Dump-2.json
```

`--use-build` reuses prepared Studio files after checking their version and GUID;
it still refreshes cloud dumps. `--force` bypasses the existing-version check.
`--miners GenerateApiDump,ExtractQtResources` and `--conditions ...` support local
debugging, but partial/conditional runs cannot be committed by the runner.

For Git publication, clone the target branch into stage and configure its author
and authentication normally. Add `--commit` to make the existing file-grouped
commits, and `--push` to push them. Without these flags the runner never commits
or pushes. No reset, clean, force-push, or parent-branch merge is performed. Remote
updates during a run cause the normal push to fail rather than overwrite history.

Track server FastFlags independently:

```sh
python scripts/tracker.py flags --stage-dir artifacts/flags-stage
```

This fetches all platforms/buckets before changing stage and retains legacy
platform JSON and compressed per-variable files. `--commit --push` is also
available for a clean FastFlags-repository checkout.

## GitHub Actions

[.github/workflows/tracker.yml](.github/workflows/tracker.yml) builds and tests the
tools on Ubuntu when a client update is needed, then runs the configured trackers.
Its lightweight preflight checks out only the source scripts and compares the
resolved Studio GUID with the target branch's `version-guid.txt` through GitHub's
API. Unchanged scheduled/manual runs skip dependency checkouts, SDK setup, tool
builds, and the client stage checkout. `force` bypasses this comparison. The resolved
version/GUID pair is passed downstream so a deployment during the build does not
change the selected version. API/authentication failures fail the check; a missing
marker requires an existing accessible target branch before an initial build runs.
Pushes and pull requests still build/test tools for CI. Python-only FastFlags
tracking runs independently and does not wait for, or require, a .NET build.

The preflight can also run locally without cloning the target repository:

```sh
python scripts/check_version.py --repository MaximumADHD/Roblox-Client-Tracker --branch roblox
```

It uses `GH_TOKEN` for private target access and writes job outputs when
`GITHUB_OUTPUT` is set. Enable Actions and add these
repository variables under **Settings > Secrets and variables > Actions**:

| Variable | Purpose |
| --- | --- |
| `RCT_CLIENT_REPOSITORY` | Client output repository, e.g. `MaximumADHD/Roblox-Client-Tracker` |
| `RCT_FLAGS_REPOSITORY` | Optional FastFlags output repository; unset disables this job |
| `RCT_BRANCH` | Client target branch; defaults to `roblox` |
| `RCT_FLAGS_BRANCH` | FastFlags target branch; defaults to `main` |
| `RCT_CHANNEL` | Studio deployment channel; defaults to `LIVE` |
| `RCT_BOT_NAME`, `RCT_BOT_EMAIL` | Optional commit author overrides |

Add secret `RCT_PUBLISH_TOKEN` with Contents read/write permission on the target
repositories, and optional secret `RBX_CHANNEL_TOKEN` for a restricted channel.
The source workflow's `GITHUB_TOKEN` has read-only permission and normally cannot
publish to another repository. Target branches must already exist. Dependency
CLI changes must be pushed before the workflow can build them from GitHub.

Scheduled runs publish every five minutes, subject to GitHub's scheduler delays.
Public-repository schedules can be disabled by GitHub after extended inactivity.
Manual runs default to committing and pushing; deselect **publish** for a dry run
that uploads stage artifacts instead. Push-triggered runs remain non-publishing.
The `tracker-tools` artifact is always used to transfer binaries between jobs and
does not replace publishing tracker results. Manual inputs also support channel, target branch, a pinned
version/GUID pair, and forced rebuilds. Pull requests only build/test tools.
Concurrency prevents overlapping tracker workflows. Unset target variables leave
the corresponding jobs disabled, making the initial deployment opt-in.

## Tests

```sh
RCT_CLI="$PWD/artifacts/tools/RobloxClientTracker/RobloxClientTracker.dll" python -m unittest discover -s tests -v
```

On PowerShell, set `$env:RCT_CLI` to that DLL path first. Tests cover version
pinning, download validation, Git commit/deletion behavior, FastFlags conversion,
and synthetic Qt resources including compression, cycles, and unsafe paths.
Without `RCT_CLI`, only the Python orchestration tests run.
