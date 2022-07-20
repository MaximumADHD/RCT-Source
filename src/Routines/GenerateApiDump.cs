using System;
using System.IO;

using Newtonsoft.Json;
using RobloxApiDumpTool;
using RobloxClientTracker.Properties;

namespace RobloxClientTracker
{
    public class GenerateApiDump : DataMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Cyan;

        public override void ExecuteRoutine()
        {
            print("Generating API Dump...");

            string jsonFile = Path.Combine(stageDir, "API-Dump.json");
            string json = File.ReadAllText(jsonFile);

            var api = new ReflectionDatabase(jsonFile);
            var dumper = new ReflectionDumper(api);

            string dump = dumper.DumpApi(ReflectionDumper.DumpUsingTxt);
            string exportPath = Path.Combine(stageDir, "API-Dump.txt");

            writeFile(exportPath, dump);
            print("Minifying API Dump...");

            var source = api.Source;
            var minified = source.ToString(Formatting.None);

            string minJsonFile = Path.Combine(stageDir, "Mini-API-Dump.json");
            writeFile(minJsonFile, minified);

            if (branch == "roblox")
            {
                var apiSite = Settings.Default.ApiSite;
                var webHook = createDirectory(studioDir, apiSite);
                var gitBind = Path.Combine(webHook, ".git");

                if (!Directory.Exists(gitBind))
                {
                    print($"Cloning {apiSite}...");
                    Program.cloneRepo(apiSite);
                }

                print("Updating API pages...");
            }
        }
    }
}
