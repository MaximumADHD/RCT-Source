using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RobloxClientTracker
{
    public class StudioDeployLogs
    {
        private const string LogPattern = "New (Studio6?4?) (version-[a-f\\d]+) at \\d+/\\d+/\\d+ \\d+:\\d+:\\d+ [A,P]M, file version: (\\d+), (\\d+), (\\d+), (\\d+)...Done!";

        public string Branch { get; private set; }

        private string LastDeployHistory = "";
        private static Dictionary<string, StudioDeployLogs> LogCache = new Dictionary<string, StudioDeployLogs>();

        public HashSet<DeployLog> CurrentLogs_x86 { get; private set; } = new HashSet<DeployLog>();
        public HashSet<DeployLog> CurrentLogs_x64 { get; private set; } = new HashSet<DeployLog>();

        private static readonly CultureInfo invariant = CultureInfo.InvariantCulture;

        private StudioDeployLogs(string branch)
        {
            Branch = branch;
            LogCache[branch] = this;
        }

        private void UpdateLogs(string deployHistory)
        {
            MatchCollection matches = Regex.Matches(deployHistory, LogPattern);
            CurrentLogs_x86.Clear();
            CurrentLogs_x64.Clear();

            foreach (Match match in matches)
            {
                string[] data = match.Groups.Cast<Group>()
                    .Select(group => group.Value)
                    .Where(value => value.Length != 0)
                    .ToArray();

                DeployLog deployLog = new DeployLog()
                {
                    BuildType   = data[1],
                    VersionGuid = data[2],

                    MajorRev    = int.Parse(data[3], invariant),
                    Version     = int.Parse(data[4], invariant),
                    Patch       = int.Parse(data[5], invariant),
                    Changelist  = int.Parse(data[6], invariant)
                };

                HashSet<DeployLog> targetList;

                if (deployLog.Is64Bit)
                    targetList = CurrentLogs_x64;
                else
                    targetList = CurrentLogs_x86;

                targetList.Add(deployLog);
            }
        }

        public static async Task<StudioDeployLogs> Get(string branch, bool refresh = false)
        {
            StudioDeployLogs logs;
            bool init = !LogCache.ContainsKey(branch);

            if (init)
                logs = new StudioDeployLogs(branch);
            else
                logs = LogCache[branch];

            string deployHistory = await HistoryCache.GetDeployHistory(branch, init || refresh);
            
            if (init || refresh)
            {
                logs.LastDeployHistory = deployHistory;
                logs.UpdateLogs(deployHistory);
            }

            return logs;
        }
    }
}
