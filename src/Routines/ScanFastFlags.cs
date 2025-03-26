using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RobloxClientTracker.Properties;
using RobloxClientTracker.Exceptions;
using RobloxStudioModManager;
using RbxFFlagDumper.Lib;

namespace RobloxClientTracker
{
    public class ScanFastFlags : DataMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Yellow;

        private const string SHOW_EVENT = "StudioNoSplashScreen";
        private const string START_EVENT = "ClientTrackerFlagScan";

        private void ScanFlagsUsingExecutable(List<string> flags)
        {
            string localAppData = Environment.GetEnvironmentVariable("LocalAppData");

            string clientSettings = resetDirectory(localAppData, "Roblox", "ClientSettings");
            string settingsPath = Path.Combine(clientSettings, "StudioAppSettings.json");

            string studioSettings = createDirectory(studioDir, "ClientSettings");
            string studioSettingsPath = Path.Combine(studioSettings, "ClientAppSettings.json");

            File.WriteAllText(settingsPath, "");
            File.WriteAllBytes(studioSettingsPath, Resources.ClientAppSettings_json);

            using (var show = new SystemEvent(SHOW_EVENT))
            using (var start = new SystemEvent(START_EVENT))
            {
                print("Starting FFlag scan...");

                var startInfo = new ProcessStartInfo()
                {
                    FileName = studioPath,
                    Arguments = $"-startEvent {start.Name} -showEvent {show.Name}"
                };

                using (Process update = Process.Start(startInfo))
                {
                    print("\tWaiting for signal from studio...");
                    start.WaitOne();

                    int timeOut = 0;
                    const int numTries = 32;

                    print("\tWaiting for StudioAppSettings.json to be written...");
                    FileInfo info = new FileInfo(settingsPath);

                    while (timeOut < numTries)
                    {
                        info.Refresh();

                        if (info.Length > 0)
                        {
                            if (!update.HasExited)
                                update.Kill();

                            break;
                        }

                        print($"\t\t({++timeOut}/{numTries} tries until giving up...)");

                        var delay = Task.Delay(30);
                        delay.Wait();
                    }

                    if (info.Length == 0)
                    {
                        print("FAST FLAG EXTRACTION FAILED!", ConsoleColor.Red);

                        update.Close();
                        update.Kill();

                        return;
                    }

                    var file = File.ReadAllText(settingsPath);

                    using (var jsonText = new StringReader(file))
                    using (var reader = new JsonTextReader(jsonText))
                    {
                        var flagData = JObject.Load(reader);

                        foreach (var pair in flagData)
                            flags.Add(pair.Key);
                    }

                    print("Flag Scan completed!");
                    update.Close();
                }
            }
        }

        public override void ExecuteRoutine()
        {
            string extraContent = createDirectory(studioDir, "content");

            var flags = new List<string>();
            var timer = new Stopwatch();

            print("Starting FastVariable scan...");

            timer.Start();

            print("Scanning Lua flags...");
            var luaFlags = StudioFFlagDumper.DumpLuaFlags(studioDir);

            print("Scanning C++ flags...");
            var cppFlags = new List<string>();

            try
            {
                cppFlags = StudioFFlagDumper.DumpCppFlags(studioPath);
            }
            catch (Exception ex)
            {
                print($"{ex.GetType().FullName}: {ex.Message}", ConsoleColor.Yellow);
                print($"Falling back to StudioAppSettings dump", ConsoleColor.Yellow);
                ScanFlagsUsingExecutable(cppFlags);
            }
            
            timer.Stop();

            if (!cppFlags.Any())
                return;

            var commonFlags = cppFlags.Intersect(luaFlags);
            flags.AddRange(cppFlags.Where(x => !commonFlags.Contains(x)).Select(x => "[C++] " + x));
            flags.AddRange(luaFlags.Where(x => !commonFlags.Contains(x)).Select(x => "[Lua] " + x));
            flags.AddRange(commonFlags.Select(x => "[Com] " + x));
            flags = flags.OrderBy(x => x.Substring(6)).ToList();
            
            print($"FastVariable scan completed in {timer.Elapsed} with {flags.Count} variables");

            string flagsPath = Path.Combine(stageDir, "FVariables.txt");
            string result = string.Join("\r\n", flags);
            writeFile(flagsPath, result);
        }
    }
}
