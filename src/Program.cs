using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

using RobloxClientTracker.Properties;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using RobloxDeployHistory;
using RobloxStudioModManager;

#pragma warning disable IDE1006 // Naming Styles

namespace RobloxClientTracker
{
    static class Program
    {
        public enum TrackMode
        {
            Client,
            FastFlags
        }

        public static readonly Encoding UTF8 = new UTF8Encoding(false);

        const string ARG_BRANCH = "-branch";
        const string ARG_PARENT = "-parent";
        const string ARG_TRACK_MODE = "-trackMode";

        const string ARG_FORCE_REBASE = "-forceRebase";
        const string ARG_FORCE_UPDATE = "-forceUpdate";
        const string ARG_FORCE_COMMIT = "-forceCommit";
        const string ARG_MANUAL_BUILD = "-manualBuild";

        const string ARG_VERBOSE_GIT_LOGS = "-verboseGitLogs";
        const string ARG_UPDATE_FREQUENCY = "-updateFrequency";

        const string ARG_FORCE_VERSION_ID = "-forceVersionId";
        const string ARG_FORCE_VERSION_GUID = "-forceVersionGuid";
        const string ARG_FORCE_PACKAGE_ANALYSIS = "-forcePackageAnalysis";

        public const ConsoleColor DARK_YELLOW = ConsoleColor.DarkYellow;
        public const ConsoleColor DARK_GREEN = ConsoleColor.DarkGreen;
        public const ConsoleColor DARK_CYAN = ConsoleColor.DarkCyan;

        public const ConsoleColor MAGENTA = ConsoleColor.Magenta;
        public const ConsoleColor YELLOW = ConsoleColor.Yellow;
        public const ConsoleColor WHITE = ConsoleColor.White;
        public const ConsoleColor GREEN = ConsoleColor.Green;
        public const ConsoleColor GRAY = ConsoleColor.Gray;
        public const ConsoleColor CYAN = ConsoleColor.Cyan;
        public const ConsoleColor BLUE = ConsoleColor.Blue;
        public const ConsoleColor RED = ConsoleColor.Red;

        static bool FORCE_REBASE = false;
        static bool FORCE_UPDATE = false;
        static bool FORCE_COMMIT = false;

        static int UPDATE_FREQUENCY = 5;
        static bool VERBOSE_GIT_LOGS = false;
        static bool MANUAL_BUILD = false;

        static TrackMode TRACK_MODE = TrackMode.Client;
        static readonly Type DataMiner = typeof(DataMiner);

        public static bool FORCE_PACKAGE_ANALYSIS = false;
        public static string FORCE_VERSION_GUID = "";
        public static string FORCE_VERSION_ID = "";

        public static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        public static NumberFormatInfo InvariantNumber = NumberFormatInfo.InvariantInfo;
        public const StringComparison InvariantString = StringComparison.InvariantCulture;
        
        static readonly string[] fflagPlatforms = new string[]
        {
            "PCDesktopClient",
            "MacDesktopClient",
            "PCStudioBootstrapper",
            "MacStudioBootstrapper",
            "PCClientBootstrapper",
            "MacClientBootstrapper",
            "XboxClient",
            "AndroidApp",
            "iOSApp",
            "PCStudioApp",
            "MacStudioApp",
            "UWPApp",
        };

        static readonly IReadOnlyList<string> boringFiles = new List<string>()
        {
            "rbxManifest.txt",
            "rbxPkgManifest.txt",

            "rbxManifest.csv",
            "rbxPkgManifest.csv",

            "version.txt",
            "version-guid.txt",

            "DeepStrings.txt",
        };

        static readonly IReadOnlyList<string> filesToCopy = new List<string>
        {
            "version.txt",
            "version-guid.txt",

            "API-Dump.json",

            "rbxManifest.txt",
            "rbxPkgManifest.txt",

            "ReflectionMetadata.xml",
            "RobloxStudioRibbon.xml"
        };

        static readonly IReadOnlyDictionary<string, ConsoleColor> changeTypeColors = new Dictionary<string, ConsoleColor>()
        {
            { "A", GREEN   },
            { "D", RED     },
            { "R", MAGENTA },
            { "C", CYAN    },
            { "M", YELLOW  },
            { "U", GRAY    },
        };

        static readonly ProcessStartInfo gitExecute = new ProcessStartInfo
        {
            FileName = "git",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        public static string branch = "roblox";
        public static string parent = "roblox";

        public static string trunk { get; private set; }
        public static string stageDir { get; private set; }
        public static string studioDir { get; private set; }
        public static string studioPath { get; private set; }
        public static ClientTrackerState state { get; private set; }
        public static StudioBootstrapper studio { get; private set; }

        static readonly Dictionary<string, string> argMap = new Dictionary<string, string>();
        
        public static void print(string message, ConsoleColor color = GRAY)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
        }

        public static void log(string content, ConsoleColor color = GRAY)
        {
            if (content.Length > 0)
            {
                Console.ForegroundColor = color;
                Console.Write(content);
            }
        }

        public static string createDirectory(params string[] traversal)
        {
            string dir = Path.Combine(traversal);

            if (!dir.StartsWith(@"\\?\"))
                dir = @"\\?\" + dir;

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            return dir;
        }

        public static IEnumerable<string> git(params string[] args)
        {
            Directory.SetCurrentDirectory(stageDir);

            string command = string.Join(" ", args);
            Process git;

            lock (gitExecute)
            {
                gitExecute.Arguments = command;
                git = Process.Start(gitExecute);
            }

            if (VERBOSE_GIT_LOGS)
                print($"> git {command}");

            List<string> outLines = new List<string>();

            var processOutput = new Action<string, bool>((message, isError) =>
            {
                if (message != null && message.Length > 0)
                {
                    lock (outLines)
                    {
                        if (VERBOSE_GIT_LOGS || isError)
                        {
                            log("[git] ", MAGENTA);
                            print(message, isError ? RED : WHITE);
                        }

                        outLines.Add(message);
                    }
                }
            });

            git.ErrorDataReceived += new DataReceivedEventHandler
                ((sender, evt) => processOutput(evt.Data, true));

            git.OutputDataReceived += new DataReceivedEventHandler
                ((sender, evt) => processOutput(evt.Data, false));
            
            git.BeginOutputReadLine();
            git.BeginErrorReadLine();

            git.WaitForExit();

            return outLines;
        }

        static List<string> getChangedFiles(string filter)
        {
            var query = git("status", "-s");
            var result = new List<string>();

            string pattern = filter.Replace("*", ".*");

            foreach (string line in query)
            {
                string type = line.Substring(0, 2).Trim();
                string file = line.Substring(3);

                if (!Regex.IsMatch(file, pattern))
                    continue;

                log("\t\t");

                if (changeTypeColors.ContainsKey(type))
                {
                    ConsoleColor color = changeTypeColors[type];
                    log(type, color);
                }
                else
                {
                    log("?", BLUE);
                }

                print($" {file}", WHITE);
                result.Add(file);
            }

            return result;
        }

        static bool isRemoteBehind(string branch, string parent)
        {
            git("fetch", "--all");
            var query = git("rev-list", "--left-right", "--count", $"{parent}...{branch}");

            int behind = query
                .First()
                .Split('\t')
                .Select(int.Parse)
                .First();

            return (behind > 0);
        }

        static bool stageCommit(string label, string filter = ".")
        {
            print($"\t[{label}] Checking in files...", YELLOW);
            git($"add {filter}");

            // Verify this update is worth committing.
            var files = getChangedFiles(filter);

            int updateCount = 0;
            int boringCount = 0;

            foreach (string file in files)
            {
                try
                {
                    string filePath = stageDir + '\\' + file;
                    FileInfo fileInfo = new FileInfo(filePath);

                    if (branch != "roblox")
                    {
                        if (boringFiles.Contains(fileInfo.Name) || fileInfo.Extension.Contains("rbx"))
                        {
                            boringCount++;
                            continue;
                        }
                    }

                    updateCount++;
                }
                catch
                {
                    // Illegal characters? No clue what to make of this.
                }
            }

            if (updateCount > 0 || FORCE_COMMIT)
            {
                print($"[{label}]\tCommitting...", CYAN);
                git($"commit -m \"{label}\"");

                return true;
            }
            else
            {
                string skipMsg;

                if (boringCount > 0)
                    skipMsg = "This update was deemed to be boring";
                else
                    skipMsg = "No changes were detected";

                print($"\t[{label}] {skipMsg}, skipping commit!", RED);
                return false;
            }
        }

        public static void cloneRepo(string repository)
        {
            var settings = Settings.Default;
            string owner = settings.RepoOwner;

            string userProfile = Environment.GetEnvironmentVariable("UserProfile");
            string privateKey = Path.Combine(userProfile, ".ssh", "RobloxClientTracker")
                .Replace('\\', '/');

            if (!File.Exists(privateKey))
            {
                print("FATAL: Missing SSH private key 'RobloxClientTracker' in ~\\.ssh!", RED);
                print("Please generate such a key above and make sure its connected to GitHub!\n", RED);

                print("For more information, visit:");
                print("https://help.github.com/en/github/authenticating-to-github/generating-a-new-ssh-key-and-adding-it-to-the-ssh-agent\n", CYAN);

                print("Press any key to continue...");
                Console.Read();

                Environment.Exit(1);
            }

            string sshCommand = $"ssh -i {privateKey}";
            string repoUrl = $"git@github.com:{owner}/{repository}.git";

            git($"clone -c core.sshCommand=\"{sshCommand}\" {repoUrl} {stageDir}");
        }

        public static bool initGitBinding(string repository)
        {
            var settings = Settings.Default;
            string gitBinding = Path.Combine(stageDir, ".git");

            if (!Directory.Exists(gitBinding))
            {
                print($"Assembling stage for {branch}...", MAGENTA);
                cloneRepo(repository);
                
                string name = settings.BotName;
                git("config", "--local", "user.name", $"\"{name}\"");

                string email = settings.BotEmail;
                git("config", "--local", "user.email", email);

                return true;
            }

            return false;
        }

        static async Task startRoutineLoop(Func<Task> routine)
        {
            print("Main thread starting!", MAGENTA);
            
            while (true)
            {
                bool timeout = false;
                print("Checking for updates...", CYAN);
                
                try
                {
                    await Task.Run(routine);
                    print($"Next update check in {UPDATE_FREQUENCY} minutes.", YELLOW);
                    await Task.Delay(UPDATE_FREQUENCY * 60000);
                }
                catch (AggregateException a)
                {
                    foreach (Exception e in a.InnerExceptions)
                        print($"Exception Thrown: {e.Message}\n{e.StackTrace}", RED);

                    timeout = true;
                }
                catch (Exception e)
                {
                    print($"Exception Thrown: {e.Message}\n{e.StackTrace}", RED);
                    timeout = true;
                }

                if (timeout)
                {
                    print($"Timing out for 1 minute.", RED);

                    if (Debugger.IsAttached)
                        Debugger.Break();

                    await Task.Delay(60000);
                }
            }
        }

        static Task TrackClientAsync()
        {
            // Initialize the git repository.
            bool init = initGitBinding(Settings.Default.ClientRepoName);

            if (init)
            {
                if (branch != "roblox")
                {
                    if (parent != "roblox")
                    {
                        git("checkout", parent);
                        git("pull");
                    }

                    git("checkout", branch);
                    git("pull");
                }
            }

            // Setup studio bootstrapper.
            studioDir = createDirectory(trunk, "builds", branch);
            state = ClientTrackerState.Load(studioDir);

            studio = new StudioBootstrapper(state)
            {
                Branch = branch,
                GenerateMetadata = true,
                RemapExtraContent = true,
                CanShutdownStudio = false,
                OverrideStudioDirectory = studioDir
            };

            studioPath = studio.GetLocalStudioPath();
            studio.EchoFeed += new MessageEventHandler((sender, e) => print(e.Message, YELLOW));
            studio.StatusChanged += new MessageEventHandler((sender, e) => print(e.Message, MAGENTA));

            var dataMiners = AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => !type.IsAbstract)
                .Where(type => type.IsSubclassOf(DataMiner))
                .Select(type => Activator.CreateInstance(type))
                .Cast<DataMiner>();

            // Report set arguments.
            if (FORCE_REBASE)
                print("\tCaution: FORCE_REBASE is set to true!", YELLOW);

            if (FORCE_UPDATE)
                print("\tCaution: FORCE_UPDATE is set to true!", YELLOW);

            if (FORCE_COMMIT)
                print("\tCaution: FORCE_COMMIT is set to true!", YELLOW);

            // Start the main thread.
            return startRoutineLoop(async () =>
            {
                // Check if the parent branch has been updated

                if (branch != parent)
                {
                    // Check if we are behind the upstream.
                    if (!MANUAL_BUILD && (FORCE_REBASE || isRemoteBehind($"origin/{branch}", $"origin/{parent}")))
                    {
                        // Discard any local changes that might still be lingering.
                        git("reset", "--hard", $"origin/{branch}");
                        git("clean -d -f");

                        // Merge with the parent upstream, keeping our own changes.
                        // The assumption right now is that child branches are
                        // ahead of the parent branches and will replace them.

                        string message = $"Merge {parent}->{branch}";
                        print($"Merging ({parent}->{branch})...", MAGENTA);

                        var mergeResults = git("merge", $"-m \"{message}\"", "-X ours", $"origin/{parent}");
                        bool hasConflicts = false;

                        // Check if some merge conflicts have shown up.
                        // This usually happens with LuaPackages >:(

                        foreach (string result in mergeResults)
                        {
                            if (result.StartsWith("CONFLICT", InvariantString))
                            {
                                int splitPos = result.IndexOf(':') + 1;

                                string prefix = result.Substring(0, splitPos);
                                string msg = result.Substring(splitPos);

                                log(prefix, RED);
                                print(msg, WHITE);

                                hasConflicts = true;
                            }
                        }

                        if (hasConflicts)
                        {
                            print("Unfortunately we have to do a hard reset :(", MAGENTA);
                            git("reset", "--hard", $"origin/{parent}");
                        }

                        git("push", "--force");
                        state.Version = "";
                    }
                }

                // Check for updates to the version
                ClientVersionInfo info = null;

                if (MANUAL_BUILD)
                {
                    print($"WARNING: Using manual build for branch {branch}!");

                    string buildDir = Path.Combine(trunk, "builds", branch);
                    string versionFile = Path.Combine(buildDir, "version.txt");

                    if (!File.Exists(versionFile))
                        throw new Exception($"MISSING FILE {versionFile}");

                    const string versionGuid = "version-$guid";
                    string version = File.ReadAllText(versionFile);

                    info = new ClientVersionInfo(version, versionGuid);
                }
                else
                {
                    info = await StudioBootstrapper.GetCurrentVersionInfo(branch, state.VersionData);
                }

                if (!string.IsNullOrEmpty(FORCE_VERSION_ID))
                    info = new ClientVersionInfo(FORCE_VERSION_ID, info.VersionGuid);

                if (!string.IsNullOrEmpty(FORCE_VERSION_GUID))
                    info = new ClientVersionInfo(info.Version, FORCE_VERSION_GUID);

                if (FORCE_UPDATE || MANUAL_BUILD || info.VersionGuid != state.Version)
                {
                    // Make sure Roblox Studio is up to date for this build.
                    print("Update detected!", YELLOW);
                    git("pull");
                    
                    if (!MANUAL_BUILD)
                    {
                        print("Syncing Roblox Studio...", GREEN);
                        await studio.Bootstrap(FORCE_VERSION_ID);
                    }

                    // Copy some metadata generated during the studio installation.

                    foreach (string fileName in filesToCopy)
                    {
                        string sourcePath = Path.Combine(studioDir, fileName);
                        string destination = Path.Combine(stageDir, fileName);

                        if (!File.Exists(sourcePath))
                        {
                            string errorMsg = $"Missing file to copy: {sourcePath}!!";

                            if (MANUAL_BUILD)
                            {
                                print(errorMsg, YELLOW);
                                continue;
                            }

                            throw new Exception(errorMsg);
                        }

                        if (File.Exists(destination))
                            File.Delete(destination);

                        File.Copy(sourcePath, destination);
                    }

                    // Run data mining routines in parallel
                    // so they don't block each other.

                    var routines = new List<Task>();

                    foreach (DataMiner miner in dataMiners)
                    {
                        Type type = miner.GetType();
                        print($"Executing data miner routine: {type.Name}", GREEN);

                        Task routine = Task.Run(() => miner.ExecuteRoutine());
                        routines.Add(routine);
                    }

                    await Task.WhenAll(routines);
                    var exceptions = new List<Exception>();

                    foreach (Task routine in routines)
                    {
                        if (routine.Status == TaskStatus.Faulted)
                        {
                            var e = routine.Exception;

                            if (e is AggregateException a)
                            {
                                exceptions.AddRange(a.InnerExceptions);
                                continue;
                            }

                            exceptions.Add(e);
                        }
                    }
                    
                    if (exceptions.Count > 0)
                        throw new AggregateException(exceptions);
                    
                    if (MANUAL_BUILD)
                    {
                        print($"Stage assembled! Please create a commit with -m \"{info.Version}\"!", GREEN);
                        print("Press any key to continue...");

                        Console.Read();
                        Environment.Exit(0);
                    }
                    else
                    {
                        // Create three commits:
                        // - One for packages.
                        // - One for lua files.
                        // - One for everything else.

                        string versionId = info.Version;
                        print("Creating commits...", YELLOW);

                        bool didStagePackages = stageCommit($"{versionId} (Packages)", "*/_Index/*");
                        bool didStageScripts = stageCommit($"{versionId} (Scripts)", "*.lua");
                        bool didStageCore = stageCommit(versionId);

                        if (didStagePackages || didStageScripts || didStageCore)
                        {
                            print("Pushing to GitHub...", CYAN);
                            git("push");

                            print("\tDone!", GREEN);
                        }

                        state.Version = info.VersionGuid;
                    }
                }
                else
                {
                    print("No updates right now!", GREEN);
                }

                state.Save(studioDir);
            });
        }

        static Task TrackFFlagsAsync()
        {
            // Initialize Repository
            const string fflagEndpoint = "https://clientsettingscdn.roblox.com/v1/settings/application?applicationName=";
            initGitBinding(Settings.Default.FFlagRepoName);

            // Start tracking...
            git("reset --hard origin/main");
            git("pull");

            return startRoutineLoop(async () =>
            {
                var taskPool = new List<Task>();
                var sets = new ConcurrentDictionary<string, Dictionary<string, string>>();

                foreach (string platform in fflagPlatforms)
                {
                    Task updatePlatform = Task.Run(async () =>
                    {
                        string json = "";

                        try
                        {
                            using (WebClient http = new WebClient())
                            {
                                http.Headers.Set("UserAgent", "RobloxClientTracker");
                                json = await http.DownloadStringTaskAsync(fflagEndpoint + platform);
                            }
                        }
                        catch (Exception e)
                        {
                            print($"\tError fetching FFlag platform: {platform}!", RED);
                            return;
                        }

                        using (var jsonText = new StringReader(json))
                        {
                            JsonTextReader reader = new JsonTextReader(jsonText);

                            JObject root = JObject.Load(reader);
                            JObject appSettings = root.Value<JObject>("applicationSettings");

                            var data = new Dictionary<string, string>();

                            foreach (var pair in appSettings)
                            {
                                string key = pair.Key;
                                string value = appSettings.Value<string>(key);
                                data.Add(key, value);
                            }

                            sets.TryAdd(platform, data);
                        }
                    });

                    taskPool.Add(updatePlatform);
                }

                await Task.WhenAll(taskPool);
                var rootSet = sets["PCDesktopClient"];

                foreach (var platform in fflagPlatforms)
                {
                    if (platform == "PCDesktopClient")
                        continue;

                    if (platform.EndsWith("App") || !platform.EndsWith("Bootstrapper"))
                    {
                        if (!sets.TryGetValue(platform, out var set))
                            continue;

                        foreach (string key in rootSet.Keys)
                            set.Remove(key);

                        sets[platform] = set;
                    }
                }

                foreach (var platform in fflagPlatforms)
                {
                    if (!sets.TryGetValue(platform, out var set))
                        continue;

                    var keys = set
                        .Select(pair => pair.Key)
                        .OrderBy(key => key)
                        .ToArray();

                    var result = new StringBuilder();
                    result.AppendLine("{");
                    
                    for (int i = 0; i < keys.Length; i++)
                    {
                        string key = keys[i];

                        string value = set[key];
                        string lower = value.ToLowerInvariant();

                        if (i != 0)
                            result.Append(",\r\n");

                        if (lower == "true" || lower == "false")
                            value = lower;
                        else if (!int.TryParse(value, out int testInt))
                            value = '"' + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + '"';

                        result.Append($"\t\"{key}\": {value}");
                    }

                    result.Append("\r\n}");

                    string filePath = Path.Combine(stageDir, platform + ".json");
                    string newFile = result.ToString();
                    string oldFile = "";

                    if (File.Exists(filePath))
                        oldFile = File.ReadAllText(filePath);

                    if (oldFile != newFile)
                    {
                        print($"\tUpdating {platform}.json ...", YELLOW);
                        File.WriteAllText(filePath, newFile);
                    }
                }
                
                string timeStamp = DateTime.Now.ToString(CultureInfo.InvariantCulture);

                if (stageCommit(timeStamp))
                {
                    print("Pushing to GitHub...", CYAN);
                    git("push");

                    print("\tDone!", GREEN);
                }
            });
        }

        static void Main(string[] args)
        {
            #region Process Launch Options
            string argKey = "";

            foreach (string arg in args)
            {
                if (arg.StartsWith("-", InvariantString))
                {
                    if (!string.IsNullOrEmpty(argKey))
                        argMap.Add(argKey, "");

                    argKey = arg;
                }
                else if (!string.IsNullOrEmpty(argKey))
                {
                    argMap.Add(argKey, arg);
                    argKey = "";
                }
            }

            if (!string.IsNullOrEmpty(argKey))
                argMap.Add(argKey, "");

            if (argMap.ContainsKey(ARG_BRANCH))
                branch = argMap[ARG_BRANCH];

            if (argMap.ContainsKey(ARG_PARENT))
                parent = argMap[ARG_PARENT];

            if (argMap.ContainsKey(ARG_FORCE_REBASE))
                FORCE_REBASE = true;

            if (argMap.ContainsKey(ARG_FORCE_UPDATE))
                FORCE_UPDATE = true;

            if (argMap.ContainsKey(ARG_FORCE_COMMIT))
                FORCE_COMMIT = true;

            if (argMap.ContainsKey(ARG_VERBOSE_GIT_LOGS))
                VERBOSE_GIT_LOGS = true;

            if (argMap.ContainsKey(ARG_MANUAL_BUILD))
                MANUAL_BUILD = true;

            if (argMap.ContainsKey(ARG_UPDATE_FREQUENCY))
                if (!int.TryParse(argMap[ARG_UPDATE_FREQUENCY], out UPDATE_FREQUENCY))
                    print($"Bad {ARG_UPDATE_FREQUENCY} provided.", RED);

            if (argMap.ContainsKey(ARG_FORCE_VERSION_ID))
                FORCE_VERSION_ID = argMap[ARG_FORCE_VERSION_ID];

            if (argMap.ContainsKey(ARG_FORCE_VERSION_GUID))
                FORCE_VERSION_GUID = argMap[ARG_FORCE_VERSION_GUID];

            if (argMap.ContainsKey(ARG_FORCE_PACKAGE_ANALYSIS))
                FORCE_PACKAGE_ANALYSIS = true;

            if (argMap.ContainsKey(ARG_TRACK_MODE))
                if (!Enum.TryParse(argMap[ARG_TRACK_MODE], out TRACK_MODE))
                    print($"Bad {ARG_TRACK_MODE} provided.", RED);

            if (TRACK_MODE == TrackMode.FastFlags)
            {
                if (!argMap.ContainsKey(ARG_UPDATE_FREQUENCY))
                    UPDATE_FREQUENCY = 2;

                branch = "fflags";
            }
            #endregion

            trunk = createDirectory(@"C:\Roblox-Client-Tracker");
            stageDir = createDirectory(trunk, "stage", branch);

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Task mainThread = null;

            if (TRACK_MODE == TrackMode.Client)
                mainThread = Task.Run(TrackClientAsync);
            else if (TRACK_MODE == TrackMode.FastFlags)
                mainThread = Task.Run(TrackFFlagsAsync);
            else
                return;

            mainThread.Wait();
        }
    }
}