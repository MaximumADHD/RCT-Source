using System.IO;
using Roblox.Reflection;

namespace RobloxClientTracker
{
    public static class ApiDump
    {
        public static void Extract()
        {
            string stageDir = Program.StageDir;

            string jsonFile = Path.Combine(stageDir, "API-Dump.json");
            string json = File.ReadAllText(jsonFile);

            var api = new ReflectionDatabase(jsonFile);
            var dumper = new ReflectionDumper(api);

            string dump = dumper.DumpApi(ReflectionDumper.DumpUsingTxt);
            string exportPath = Path.Combine(stageDir, "API-Dump.txt");

            Program.WriteFile(exportPath, dump);
        }
    }
}
