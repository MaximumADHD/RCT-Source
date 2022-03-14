using System;
using System.Collections.Generic;
using System.IO;

using RobloxStudioModManager;
using Newtonsoft.Json;

namespace RobloxClientTracker
{
    public class BootstrapperState : IBootstrapperState
    {
        public bool DeprecateMD5 { get; set; } = true;
        public string BuildBranch { get; set; } = "roblox";

        public VersionManifest VersionData { get; set; } = new VersionManifest();
        public Dictionary<string, string> FileManifest { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, PackageState> PackageManifest { get; set; } = new Dictionary<string, PackageState>();

        public static BootstrapperState Load(string studioDir)
        {
            string path = Path.Combine(studioDir, "state.json");
            BootstrapperState state;

            try
            {
                string content = File.ReadAllText(path);
                state = JsonConvert.DeserializeObject<BootstrapperState>(content);
            }
            catch
            {
                Program.log("Couldn't find/load bootstrapper state, creating new one.", ConsoleColor.Red);
                state = new BootstrapperState();
            }

            return state;
        }

        public void Save(string studioDir)
        {
            string path = Path.Combine(studioDir, "state.json");
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(path, json);
        }
    }
}
