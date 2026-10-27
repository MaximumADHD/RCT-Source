using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RobloxClientTracker.Properties;
using RobloxStudioModManager;

namespace RobloxClientTracker
{
    public class ScanFastFlags : DataMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Yellow;

        public override void ExecuteRoutine()
        {
            string extraContent = createDirectory(studioDir, "content");

            var flags = new List<string>();
            var timer = new Stopwatch();

            print("Starting FastVariable scan...");
            timer.Start();

            var luaFlags = new List<string>();
            var cppFlags = new List<string>();

            if (!Program.Conditions.Contains("noluaflags"))
            {
                print("Scanning Lua flags...");
                luaFlags = StudioFFlagDumper.DumpLuaFlags(studioDir);
            }

            if (!Program.Conditions.Contains("nocppflags"))
            {
                print("Scanning C++ flags...");
                cppFlags = StudioFFlagDumper.DumpCppFlags(studioPath);
                
                if (cppFlags.Count == 0 || cppFlags.Any(flag => flag.StartsWith("ERROR") || flag.StartsWith("DERROR")))
                {
                    throw new InvalidDataException("Static C++ FastVariable extraction failed.");
                }
            }
            
            timer.Stop();

            var commonFlags = cppFlags.Intersect(luaFlags);
            flags.AddRange(cppFlags.Where(x => !commonFlags.Contains(x)).Select(x => "[C++] " + x));
            flags.AddRange(luaFlags.Where(x => !commonFlags.Contains(x)).Select(x => "[Lua] " + x));
            flags.AddRange(commonFlags.Select(x => "[Com] " + x));
            flags = flags.OrderBy(x => x.Substring(6)).ToList();
            
            print($"FastVariable scan completed in {timer.Elapsed} with {flags.Count} variables");

            string flagsPath = Path.Combine(stageDir, "FVariables.txt");
            string result = string.Join("\r\n", flags);
            writeFile(flagsPath, result);
        }
    }
}
