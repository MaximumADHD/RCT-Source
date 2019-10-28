using System.Collections.Generic;

using RobloxFiles;
using Newtonsoft.Json;

namespace RobloxClientTracker
{
    public class LocalizationEntry
    {
        public string Key;
        public string Source;
        public string Context;
        public string Example;

        public Dictionary<string, string> Values;

        public static LocalizationEntry[] GetEntries(LocalizationTable table)
        {
            string contents = table.Contents;
            return JsonConvert.DeserializeObject<LocalizationEntry[]>(contents);
        }
    }
}
