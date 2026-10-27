import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zlib

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from scripts import tracker as tracker
from scripts import flag_tracker
from scripts import client_tracker
from scripts import tracker_common
from scripts import check_version as preflight
from urllib.error import HTTPError, URLError


class TrackerCommandTests(unittest.TestCase):
    def test_existing_commands_delegate_to_their_modules(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            commands = {
                "tools": ["--tools-dir", str(root / "tools")],
                "client": ["--build-dir", str(root / "build"), "--stage-dir", str(root / "stage")],
                "flags": ["--stage-dir", str(root / "flags")],
            }
            for command, arguments in commands.items():
                with self.subTest(command=command), \
                        patch.object(sys, "argv", ["tracker.py", command, *arguments]), \
                        patch.object(client_tracker, "publish_tools") as tools, \
                        patch.object(client_tracker, "track_client") as client, \
                        patch.object(flag_tracker, "track_flags") as flags:
                    self.assertEqual(tracker.main(), 0)
                    for name, handler in {"tools": tools, "client": client, "flags": flags}.items():
                        if name == command:
                            handler.assert_called_once()
                        else:
                            handler.assert_not_called()

    def test_direct_and_module_invocations(self):
        script = Path(tracker.__file__).resolve()
        invocations = [([str(script), "--help"], script.parent),
                       (["-m", "scripts.tracker", "--help"], script.parents[1])]
        for arguments, directory in invocations:
            with self.subTest(arguments=arguments):
                process = subprocess.run([sys.executable, *arguments], cwd=directory,
                                         capture_output=True, text=True)
                self.assertEqual(process.returncode, 0, process.stderr)


class VersionCheckTests(unittest.TestCase):
    version = "0.744.0.123"
    guid = "version-0123456789abcdef"

    def test_direct_script_invocation(self):
        script = Path(preflight.__file__).resolve()
        process = subprocess.run([sys.executable, str(script), "--help"],
                                 cwd=script.parent, capture_output=True, text=True)
        self.assertEqual(process.returncode, 0, process.stderr)

    def check(self, **options):
        return preflight.check_version("owner/repo", "release/test", version=self.version,
                                       guid=self.guid, **options)

    def test_unchanged_build_skips_update(self):
        with patch.object(preflight, "github_read", return_value=(self.guid + "\n").encode()) as read:
            result = self.check()
        self.assertEqual(result, {"update_required": "false", "version": self.version, "version_guid": self.guid})
        self.assertIn("ref=release%2Ftest", read.call_args.args[1])

    def test_latest_build_comes_from_clientsettings_cdn(self):
        response = json.dumps({"version": self.version, "clientVersionUpload": self.guid}).encode()
        with patch.object(client_tracker, "fetch", return_value=response) as fetch, \
                patch.object(preflight, "github_read", return_value=self.guid.encode()):
            result = preflight.check_version("owner/repo", "roblox", channel="LIVE")
        self.assertEqual([call.args[0] for call in fetch.call_args_list], [
            "https://clientsettingscdn.roblox.com/v2/client-version/WindowsStudio64/channel/LIVE",
            "https://clientsettingscdn.roblox.com/v2/client-version/WindowsStudio64/channel/zbeta",
        ])
        self.assertEqual(result["update_required"], "false")

    def test_changed_build_requires_update(self):
        with patch.object(preflight, "github_read", return_value=b"version-fedcba9876543210"):
            self.assertEqual(self.check()["update_required"], "true")

    def test_force_bypasses_target_lookup(self):
        with patch.object(preflight, "github_read", side_effect=AssertionError("Unexpected lookup")):
            self.assertEqual(self.check(force=True)["update_required"], "true")

    def test_missing_marker_requires_existing_branch(self):
        missing = HTTPError("https://api.github.com/", 404, "Not Found", None, None)
        with patch.object(preflight, "github_read", side_effect=[missing, b'{}']) as read:
            self.assertEqual(self.check()["update_required"], "true")
        self.assertEqual(read.call_args.args[1], "branches/release%2Ftest")

    def test_missing_branch_and_api_errors_fail(self):
        for status in [403, 404, 500]:
            with self.subTest(status=status):
                error = HTTPError("https://api.github.com/", status, "Error", None, None)
                with patch.object(preflight, "github_read", side_effect=error), self.assertRaises(HTTPError):
                    self.check()

    def test_invalid_marker_fails(self):
        with patch.object(preflight, "github_read", return_value=b"invalid"), self.assertRaises(ValueError):
            self.check()

    def test_token_is_only_sent_to_github(self):
        with patch.object(preflight, "urlopen") as open_url:
            open_url.return_value.__enter__.return_value.read.return_value = b"marker"
            preflight.github_read("owner/repo", "contents/version-guid.txt?ref=roblox", "test-token")
        request = open_url.call_args.args[0]
        self.assertEqual(request.host, "api.github.com")
        self.assertEqual(request.get_header("Authorization"), "Bearer test-token")

    def test_cli_writes_github_outputs(self):
        result = {"update_required": "false", "version": self.version, "version_guid": self.guid}
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "outputs"
            with patch.object(sys, "argv", ["check_version.py", "--repository", "owner/repo"]), \
                    patch.dict(os.environ, {"GITHUB_OUTPUT": str(output)}), \
                    patch.object(preflight, "check_version", return_value=result):
                self.assertEqual(preflight.main(), 0)
            self.assertEqual(output.read_text(), "".join(f"{name}={value}\n" for name, value in result.items()))

    def test_cli_failure_does_not_write_outputs(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "outputs"
            with patch.object(sys, "argv", ["check_version.py", "--repository", "owner/repo"]), \
                    patch.dict(os.environ, {"GITHUB_OUTPUT": str(output)}), \
                    patch.object(preflight, "check_version", side_effect=OSError("Network failed")), \
                    patch.object(sys, "stderr"):
                self.assertEqual(preflight.main(), 1)
            self.assertFalse(output.exists())

class OrchestrationTests(unittest.TestCase):
    def test_flag_platform_keys_follow_invariant_order(self):
        expected = ["AndroidApp", "iOSApp", "MacClientBootstrapper", "MacDesktopClient",
                    "MacStudioApp", "MacStudioBootstrapper", "PCClientBootstrapper",
                    "PCDesktopClient", "PCStudioApp", "PCStudioBootstrapper",
                    "PlayStationClient", "UWPApp", "XboxClient"]
        platforms = {platform: index for index, platform in enumerate(reversed(flag_tracker.PLATFORMS))}
        documents = [
            {"Type": "Platforms", "Value": platforms},
            {"Type": "ChannelsAndPlatforms", "Value": {"zintegration": platforms, "LIVE": platforms}},
        ]
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "flag.json"
            for document in documents:
                with self.subTest(type=document["Type"]):
                    flag_tracker.write_json(path, document)
                    output = json.loads(path.read_text())
                    mappings = [output["Value"]] if document["Type"] == "Platforms" else output["Value"].values()
                    for mapping in mappings:
                        self.assertEqual(list(mapping), expected)
                        self.assertEqual(mapping, platforms)
                    if document["Type"] == "ChannelsAndPlatforms":
                        self.assertEqual(list(output["Value"]), ["LIVE", "zintegration"])

    def test_non_platform_flag_keys_keep_ordinal_order(self):
        value = {"FFlagapple": True, "FFlagZebra": False, "FFlagApple": True}
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "legacy.json"
            flag_tracker.write_json(path, value)
            output = json.loads(path.read_text())
        self.assertEqual(list(output), ["FFlagApple", "FFlagZebra", "FFlagapple"])
        self.assertEqual(output, value)

    def test_flag_snapshot_preserves_spaces_in_real_keys(self):
        name = "FFlagDebugAlwaysDisplayRenderStats "
        response = json.dumps({"applicationSettings": {name: "True"}}).encode()
        with tempfile.TemporaryDirectory() as temporary:
            args = tracker.parser().parse_args(["flags", "--stage-dir", temporary])
            with patch.object(flag_tracker, "fetch", return_value=response), patch("builtins.print"):
                flag_tracker.track_flags(args)
            path = Path(temporary) / "FVariables/FFlag/D" / (name + ".json")
            self.assertEqual(json.loads(path.read_text()), {"Type": "Unified", "Value": True})

    def test_flag_values_and_compression(self):
        self.assertIs(flag_tracker.flag_value("True"), True)
        self.assertIs(flag_tracker.flag_value("False"), False)
        self.assertEqual(flag_tracker.flag_value("-42"), -42)
        self.assertEqual(flag_tracker.flag_value("True;123"), "true;123")
        clients = {platform: True for platform in flag_tracker.PLATFORMS if not platform.endswith("Bootstrapper")}
        buckets = {bucket: dict(clients) for bucket in flag_tracker.BUCKETS}
        self.assertEqual(flag_tracker.compress_flag(buckets), {"Type": "Unified", "Value": True})
        buckets["zcanary"] = {platform: False for platform in clients}
        self.assertEqual(flag_tracker.compress_flag(buckets)["Type"], "Channels")
        del buckets["LIVE"]["PCDesktopClient"]
        buckets["LIVE"]["MacDesktopClient"] = False
        self.assertEqual(flag_tracker.compress_flag(buckets)["Type"], "ChannelsAndPlatforms")

    def test_flag_fetch_failure_does_not_change_stage(self):
        with tempfile.TemporaryDirectory() as temporary:
            args = tracker.parser().parse_args(["flags", "--stage-dir", temporary])
            with patch.object(flag_tracker, "fetch", side_effect=OSError("Network failed")):
                with self.assertRaises(OSError):
                    flag_tracker.track_flags(args)
            self.assertEqual(list(Path(temporary).iterdir()), [])

    def test_explicit_version_is_pinned_without_network(self):
        with patch.object(client_tracker, "fetch", side_effect=AssertionError("Unexpected network")):
            self.assertEqual(client_tracker.resolve_version("LIVE", "0.744.0.123", "version-0123456789abcdef"),
                             ("0.744.0.123", "version-0123456789abcdef"))

    def test_live_prefers_newer_online_zbeta(self):
        live = json.dumps({"version": "0.744.0.9", "clientVersionUpload": "version-0123456789abcdef"}).encode()
        beta = json.dumps({"version": "0.744.0.10", "clientVersionUpload": "version-fedcba9876543210"}).encode()
        with patch.object(client_tracker, "fetch", side_effect=[live, beta]):
            self.assertEqual(client_tracker.resolve_version("LIVE"), ("0.744.0.10", "version-fedcba9876543210"))

    def test_live_does_not_select_older_zbeta(self):
        live = json.dumps({"version": "0.744.0.123", "clientVersionUpload": "version-0123456789abcdef"}).encode()
        beta = json.dumps({"version": "0.743.0.999", "clientVersionUpload": "version-fedcba9876543210"}).encode()
        with patch.object(client_tracker, "fetch", side_effect=[live, beta]):
            self.assertEqual(client_tracker.resolve_version("LIVE"), ("0.744.0.123", "version-0123456789abcdef"))

    def test_live_survives_offline_or_invalid_zbeta(self):
        live = json.dumps({"version": "0.744.0.123", "clientVersionUpload": "version-0123456789abcdef"}).encode()
        for unavailable in [HTTPError("https://clientsettingscdn.roblox.com/", 404, "Offline", None, None),
                            URLError("Offline"), TimeoutError("Timeout"), b"not json", b'{}',
                            b'{"version": null, "clientVersionUpload": null}']:
            with self.subTest(unavailable=unavailable), patch.object(client_tracker, "fetch", side_effect=[live, unavailable]):
                self.assertEqual(client_tracker.resolve_version("LIVE"), ("0.744.0.123", "version-0123456789abcdef"))

    def test_explicit_channels_do_not_probe_zbeta(self):
        response = json.dumps({"version": "0.744.0.123", "clientVersionUpload": "version-0123456789abcdef"}).encode()
        for channel in ["zbeta", "custom-channel"]:
            with self.subTest(channel=channel), patch.object(client_tracker, "fetch", return_value=response) as fetch:
                self.assertEqual(client_tracker.resolve_version(channel), ("0.744.0.123", "version-0123456789abcdef"))
                fetch.assert_called_once_with("https://clientsettingscdn.roblox.com/v2/client-version/WindowsStudio64/channel/" + channel)

    def test_rejects_partial_or_invalid_versions(self):
        for version, guid in [("0.744.0.123", None), (None, "version-0123456789abcdef"),
                              ("../bad", "version-0123456789abcdef"), ("0.744.0.123", "../bad")]:
            with self.subTest(version=version, guid=guid), self.assertRaises(ValueError):
                client_tracker.resolve_version("LIVE", version, guid)

    def test_api_download_is_validated_before_writing(self):
        with tempfile.TemporaryDirectory() as temporary:
            build = Path(temporary)
            valid = json.dumps({"Classes": [{}], "Enums": []}).encode()
            with patch.object(client_tracker, "fetch", side_effect=[valid, valid, b'{}']):
                with self.assertRaises(ValueError):
                    client_tracker.download_api_dumps(build, "version-0123456789abcdef")
            self.assertEqual(list(build.iterdir()), [])

    def test_paths_must_not_overlap(self):
        root = Path(tempfile.gettempdir()) / "rct-path-test"
        with self.assertRaises(ValueError):
            client_tracker.validate_paths(root, root / "stage", root.parent / "tools")

    def test_up_to_date_stage_skips_downloads_and_tools(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            stage = root / "stage"
            stage.mkdir()
            guid = "version-0123456789abcdef"
            tracker_common.git(stage, "init", "-b", "test")
            tracker_common.git(stage, "config", "user.name", "RCT Test")
            tracker_common.git(stage, "config", "user.email", "test@example.invalid")
            (stage / "version-guid.txt").write_text(guid)
            tracker_common.git(stage, "add", ".")
            tracker_common.git(stage, "commit", "-m", "initial")
            args = tracker.parser().parse_args(["client", "--stage-dir", str(stage),
                                               "--build-dir", str(root / "build"),
                                               "--tools-dir", str(root / "tools"),
                                               "--version", "0.744.0.123", "--version-guid", guid])
            with patch.object(client_tracker, "invoke_tool", side_effect=AssertionError("Unexpected tool invocation")), \
                    patch.object(client_tracker, "fetch", side_effect=AssertionError("Unexpected network")):
                client_tracker.track_client(args)
            self.assertFalse((root / "build").exists())
            tracker_common.require_clean_stage(stage)

    def test_git_commits_deletions_and_skips_no_change(self):
        with tempfile.TemporaryDirectory() as temporary:
            stage = Path(temporary).resolve()
            tracker_common.git(stage, "init", "-b", "test")
            tracker_common.git(stage, "config", "user.name", "RCT Test")
            tracker_common.git(stage, "config", "user.email", "test@example.invalid")
            (stage / "old.lua").write_text("return true")
            tracker_common.git(stage, "add", ".")
            tracker_common.git(stage, "commit", "-m", "initial")
            tracker_common.require_clean_stage(stage)
            self.assertFalse(client_tracker.commit_stage(stage, "unchanged"))
            (stage / "old.lua").unlink()
            (stage / "new.json").write_text("{}")
            with self.assertRaises(ValueError):
                tracker_common.require_clean_stage(stage)
            self.assertTrue(client_tracker.commit_stage(stage, "0.744.0.123"))
            self.assertEqual(tracker_common.git(stage, "status", "--porcelain"), "")
            self.assertEqual(tracker_common.git(stage, "log", "-1", "--format=%s"), "0.744.0.123 (JSON)")


def qt_executable(compressed=False, unsafe_name=False, version=2, cyclic=False):
    binary = bytearray(0x1400)
    binary[:2] = b"MZ"
    struct.pack_into("<I", binary, 0x3C, 0x80)
    binary[0x80:0x84] = b"PE\0\0"
    struct.pack_into("<HHIIIHH", binary, 0x84, 0x8664, 2, 0, 0, 0, 240, 0x22)
    optional = 0x98
    struct.pack_into("<H", binary, optional, 0x20B)
    struct.pack_into("<Q", binary, optional + 24, 0x140000000)
    struct.pack_into("<II", binary, optional + 32, 0x1000, 0x200)
    struct.pack_into("<II", binary, optional + 56, 0x3000, 0x200)
    struct.pack_into("<H", binary, optional + 68, 3)
    struct.pack_into("<I", binary, optional + 108, 16)
    section = optional + 240
    struct.pack_into("<8sIIIIIIHHI", binary, section, b".text\0\0\0", 0x200, 0x1000, 0x200, 0x200, 0, 0, 0, 0, 0x60000020)
    struct.pack_into("<8sIIIIIIHHI", binary, section + 40, b".rdata\0\0", 0x1000, 0x2000, 0x1000, 0x400, 0, 0, 0, 0, 0x40000040)
    pattern = "48 83 EC 28 4C 8D 0D 00 00 00 00 B9 02 00 00 00 4C 8D 05 00 00 00 00 48 8D 15 00 00 00 00 E8"
    binary[0x200:0x200 + 31] = bytes.fromhex(pattern)
    for displacement, target in [(7, 0x400), (19, 0x600), (26, 0x800)]:
        target_rva = 0x2000 + target - 0x400
        struct.pack_into("<i", binary, 0x200 + displacement, target_rva - (0x1000 + displacement + 4))
    struct.pack_into("<i", binary, 0x200 + 12, version)
    payload = b"Qt resource content\n"
    stored = struct.pack(">I", len(payload)) + zlib.compress(payload) if compressed else payload
    struct.pack_into(">I", binary, 0x400, len(stored))
    binary[0x404:0x404 + len(stored)] = stored
    position = 0x600
    names = []
    for name in ["", "prefix", "../escape.txt" if unsafe_name else "test.txt"]:
        names.append(position - 0x600)
        encoded = name.encode("utf-16-be")
        struct.pack_into(">HI", binary, position, len(name), 0)
        binary[position + 6:position + 6 + len(encoded)] = encoded
        position += 6 + len(encoded)
    stride = 22 if version >= 2 else 14
    struct.pack_into(">I H I I", binary, 0x800, names[0], 2, 1, 0 if cyclic else 1)
    struct.pack_into(">I H I I", binary, 0x800 + stride, names[1], 2, 1, 2)
    struct.pack_into(">I H I I", binary, 0x800 + stride * 2, names[2], 1 if compressed else 0, 0, 0)
    return binary, payload


@unittest.skipUnless(os.environ.get("RCT_CLI"), "Set RCT_CLI to the published tracker DLL for parser tests.")
class QtExtractorTests(unittest.TestCase):
    def extract(self, binary, successful=True):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            build = root / "build"
            stage = root / "stage"
            build.mkdir()
            for name in ["version.txt", "version-guid.txt", *client_tracker.API_DUMPS,
                         "rbxManifest.txt", "rbxPkgManifest.txt", "ReflectionMetadata.xml"]:
                (build / name).write_text("fixture")
            (build / "RobloxStudioBeta.exe").write_bytes(binary)
            process = subprocess.run(["dotnet", os.environ["RCT_CLI"], "build", "--build-dir", str(build),
                                      "--stage-dir", str(stage), "--miners", "ExtractQtResources"],
                                     capture_output=True, text=True)
            if successful:
                self.assertEqual(process.returncode, 0, process.stderr)
                return (stage / "QtResources/RobloxStyle/test.txt").read_bytes()
            self.assertNotEqual(process.returncode, 0)
            self.assertFalse((root / "escape.txt").exists())

    def test_plain_and_zlib_versions(self):
        for version in [1, 2, 3]:
            for compressed in [False, True]:
                with self.subTest(version=version, compressed=compressed):
                    binary, payload = qt_executable(compressed=compressed, version=version)
                    self.assertEqual(self.extract(binary), payload)

    def test_rejects_unsafe_names(self):
        binary, _ = qt_executable(unsafe_name=True)
        self.extract(binary, successful=False)

    def test_rejects_cycles(self):
        binary, _ = qt_executable(cyclic=True)
        self.extract(binary, successful=False)

    def test_rejects_out_of_bounds_pointer(self):
        binary, _ = qt_executable()
        struct.pack_into(">I", binary, 0x800, 0x7FFFFFFF)
        self.extract(binary, successful=False)

    def test_rejects_unknown_registration(self):
        binary, _ = qt_executable()
        binary[0x200] = 0
        self.extract(binary, successful=False)


if __name__ == "__main__":
    unittest.main()