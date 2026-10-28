using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RobloxClientTracker
{
    static class Program
    {
        public static readonly Encoding UTF8 = new UTF8Encoding(false);
        public static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        public const StringComparison InvariantString = StringComparison.InvariantCulture;
        public const ConsoleColor DARK_CYAN = ConsoleColor.DarkCyan;
        public static bool FORCE_PACKAGE_ANALYSIS = true;
        public static string[] Conditions = [];

        public static string stageDir { get; private set; }
        public static string studioDir { get; private set; }
        public static string studioPath => Path.Combine(studioDir, "RobloxStudioBeta.exe");
        
        public static string Undname { get; private set; } = "llvm-undname";
        public static ClientTrackerState state { get; private set; }

        public static void print(string message, ConsoleColor color = ConsoleColor.Gray)
        {
            if (!Console.IsOutputRedirected)
                Console.ForegroundColor = color;
            
            Console.WriteLine(message);
        }

        public static string createDirectory(params string[] traversal)
        {
            string directory = Path.GetFullPath(Path.Combine(traversal));
            Directory.CreateDirectory(directory);
            return directory;
        }

        static async Task<int> Main(string[] args)
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                CultureInfo.DefaultThreadCurrentUICulture = Invariant;
                CultureInfo.DefaultThreadCurrentCulture = Invariant;

                if (args.Length == 0 || args.Contains("--help"))
                {
                    Console.WriteLine("RobloxClientTracker build --build-dir <Studio files> --stage-dir <output>");
                    Console.WriteLine("  [--miners <comma-separated names>] [--conditions <comma-separated names>]");
                    Console.WriteLine("  [--undname <llvm-undname executable>]");
                    Console.WriteLine("RobloxClientTracker --list-miners");
                    return 0;
                }

                var minerTypes = typeof(DataMiner).Assembly.GetTypes()
                    .Where(type => !type.IsAbstract && type.IsSubclassOf(typeof(DataMiner)))
                    .OrderBy(type => type.Name, StringComparer.Ordinal).ToArray();

                if (args.Length == 1 && args[0] == "--list-miners")
                {
                    foreach (var type in minerTypes)
                        Console.WriteLine(type.Name);
                    
                    return 0;
                }

                if (args[0] != "build")
                    throw new ArgumentException("Expected the 'build' command. Use --help for usage.");

                var options = new Dictionary<string, string>(StringComparer.Ordinal);
                var allowed = new[] { "--build-dir", "--stage-dir", "--miners", "--conditions", "--undname" };

                for (int index = 1; index < args.Length; index += 2)
                {
                    if (!allowed.Contains(args[index]) || index + 1 >= args.Length || args[index + 1].StartsWith("--"))
                        throw new ArgumentException("Unknown option or missing value: " + args[index]);
                    
                    options.Add(args[index], args[index + 1]);
                }

                if (!options.ContainsKey("--build-dir") || !options.ContainsKey("--stage-dir"))
                    throw new ArgumentException("--build-dir and --stage-dir are required.");

                studioDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options["--build-dir"]));
                stageDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options["--stage-dir"]));

                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

                if (studioDir.Equals(stageDir, comparison)
                    || studioDir.StartsWith(stageDir + Path.DirectorySeparatorChar, comparison)
                    || stageDir.StartsWith(studioDir + Path.DirectorySeparatorChar, comparison))
                    throw new ArgumentException("Build and stage directories must not overlap.");

                if (options.TryGetValue("--conditions", out string conditions))
                    Conditions = conditions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                
                if (options.TryGetValue("--undname", out string undname))
                    Undname = undname;
                
                if (options.TryGetValue("--miners", out string filter))
                {
                    var names = filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    if (names.Length == 0 || names.Any(name => !minerTypes.Any(type => type.Name.Equals(name, StringComparison.OrdinalIgnoreCase))))
                        throw new ArgumentException("Unknown miner. Use --list-miners for valid names.");
                    
                    minerTypes = minerTypes.Where(type => names.Contains(type.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
                }

                string[] metadata =
                {
                    "version.txt",
                    "version-guid.txt",
                    "API-Dump.json",
                    "Full-API-Dump.json",
                    "API-Dump-2.json", 
                    "rbxManifest.txt",
                    "rbxPkgManifest.txt",
                    "ReflectionMetadata.xml"
                };
                
                foreach (string name in metadata)
                    if (!File.Exists(Path.Combine(studioDir, name)))
                        throw new FileNotFoundException("Missing build input: " + name);
                
                createDirectory(stageDir);

                foreach (string name in metadata)
                {
                    var sourcePath = Path.Combine(studioDir, name);
                    var destinationPath = Path.Combine(stageDir, name);
                    File.Copy(sourcePath, destinationPath, true);
                }

                var routines = new List<Task>();
                state = new ClientTrackerState();
                RobloxFileMiner.ResetPackages();

                foreach (var type in minerTypes)
                {
                    var miner = (DataMiner)Activator.CreateInstance(type);
                    print("Executing " + type.Name, ConsoleColor.Green);
                    routines.Add(Task.Run(miner.ExecuteRoutine));
                }

                await Task.WhenAll(routines);
                print("Stage assembled: " + stageDir, ConsoleColor.Green);
                
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
    }
}