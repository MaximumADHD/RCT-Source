using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using RobloxStudioModManager;

namespace RobloxClientTracker
{
    public class ScanFastFlags : DataMiner
    {
        private const string SHOW_EVENT = "StudioNoSplashScreen";
        private const string START_EVENT = "ClientTrackerFlagScan";

        public override ConsoleColor LogColor => ConsoleColor.Yellow;

        public override void ExecuteRoutine()
        {
            string localAppData = Environment.GetEnvironmentVariable("LocalAppData");
            string clientSettings = resetDirectory(localAppData, "Roblox", "ClientSettings");

            string settingsPath = Path.Combine(clientSettings, "StudioAppSettings.json");
            File.WriteAllText(settingsPath, "");

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
                    var flags = new List<string>();

                    using (var jsonText = new StringReader(file))
                    using (var reader = new JsonTextReader(jsonText))
                    {
                        var flagData = JObject.Load(reader);

                        foreach (var pair in flagData)
                        {
                            string flagName = pair.Key;
                            flags.Add(flagName);
                        }
                    }

                    flags.Sort();
                    print("Flag Scan completed!");

                    string flagsPath = Path.Combine(stageDir, "FVariables.txt");
                    string result = string.Join("\r\n", flags);

                    writeFile(flagsPath, result);
                    update.Close();
                }
            }
        }
    }
}
