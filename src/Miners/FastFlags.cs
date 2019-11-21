using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RobloxClientTracker
{
    public static class FastFlags
    {
        private static void print(string msg)
        {
            Program.print(msg, Program.YELLOW);
        }

        public static void Extract()
        {
            string studioPath = Program.StudioPath;

            string localAppData = Environment.GetEnvironmentVariable("LocalAppData");
            string clientSettings = Program.ResetDirectory(localAppData, "Roblox", "ClientSettings");

            string settingsPath = Path.Combine(clientSettings, "StudioAppSettings.json");
            File.WriteAllText(settingsPath, "");

            SystemEvent show = new SystemEvent("StudioNoSplashScreen");
            SystemEvent start = new SystemEvent("ClientTrackerFlagScan");

            print("Starting FFlag scan...");

            Process update = Process.Start(new ProcessStartInfo()
            {
                FileName = studioPath,
                Arguments = $"-startEvent {start.Name} -showEvent {show.Name}"
            });

            print("\tWaiting for signal from studio...");
            start.WaitOne();

            int timeOut = 0;
            const int numTries = 128;

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

            string file = File.ReadAllText(settingsPath);
            var flags = new List<string>();

            using (var jsonText = new StringReader(file))
            {
                JsonTextReader reader = new JsonTextReader(jsonText);
                JObject flagData = JObject.Load(reader);

                foreach (var pair in flagData)
                {
                    string flagName = pair.Key;
                    flags.Add(flagName);
                }
            }

            flags.Sort();
            print("Flag Scan completed!");

            string stageDir = Program.StageDir;
            string flagsPath = Path.Combine(stageDir, "FVariables.txt");

            string result = string.Join("\r\n", flags);
            Program.WriteFile(flagsPath, result);
        }
    }
}
