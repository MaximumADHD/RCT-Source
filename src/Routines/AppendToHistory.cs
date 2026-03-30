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


            print("Updating version history...");
            var append = StudioDeployLogs.AppendToHistoryLedger(jsonHistory);

            append.Wait();
            jsonHistory = append.Result;

            File.WriteAllText(historyPath, jsonHistory);
        }
    }
}
