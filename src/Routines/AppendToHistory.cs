using RobloxDeployHistory;

using System;
using System.IO;
using System.Threading.Tasks;

namespace RobloxClientTracker
{
    public class AppendToHistory : DataMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Magenta;

        public override void ExecuteRoutine()
        {
            for (int retry = 1; retry < 10; retry++)
            {
                var historyPath = Path.Combine(stageDir, "version-history.json");
                var jsonHistory = File.Exists(historyPath) ? File.ReadAllText(historyPath) : "{}";

                var versionPath = Path.Combine(stageDir, "version.txt");
                var version = File.Exists(versionPath) ? File.ReadAllText(versionPath) : null;

                var guidPath = Path.Combine(stageDir, "version-guid.txt");
                var guid = File.Exists(guidPath) ? File.ReadAllText(guidPath) : null;

                if (version == null || guid == null)
                {
                    var timeout = Task.Delay(1000);
                    print($"Version or GUID is null, retrying... ({retry}/10)", ConsoleColor.Red);

                    timeout.Wait();
                    continue;
                }

                print($"Updating version history... ({version} = {guid})");
                var append = StudioDeployLogs.AppendToHistoryLedger(jsonHistory, version, guid);

                append.Wait();
                jsonHistory = append.Result;

                File.WriteAllText(historyPath, jsonHistory);
                break;
            }
        }
    }
}
