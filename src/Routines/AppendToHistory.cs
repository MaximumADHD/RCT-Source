using RobloxDeployHistory;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RobloxClientTracker
{
    public class AppendToHistory : DataMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Magenta;

        public override void ExecuteRoutine()
        {
            var historyPath = Path.Combine(stageDir, "version-history.json");
            var jsonHistory = File.ReadAllText(historyPath);

            var versionPath = Path.Combine(stageDir, "version.txt");
            var version = File.ReadAllText(versionPath);

            var guidPath = Path.Combine(stageDir, "version-guid.txt");
            var guid = File.ReadAllText(guidPath);

            print($"Updating version history... ({version} = {guid})");
            var append = StudioDeployLogs.AppendToHistoryLedger(jsonHistory, version, guid);

            append.Wait();
            jsonHistory = append.Result;

            File.WriteAllText(historyPath, jsonHistory);
        }
    }
}
