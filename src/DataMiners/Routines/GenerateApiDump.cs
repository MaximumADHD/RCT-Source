using System;
using System.IO;

using Roblox.Reflection;

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
        }
    }
}
