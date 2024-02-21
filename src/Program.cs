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
using RobloxClientTracker.Utility;

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
        const string ARG_CHANNEL = "-channel";
        const string ARG_TRACK_MODE = "-trackMode";

        const string ARG_FORCE_REBASE = "-forceRebase";
        const string ARG_FORCE_UPDATE = "-forceUpdate";
        const string ARG_FORCE_COMMIT = "-forceCommit";
        const string ARG_MANUAL_BUILD = "-manualBuild";

        const string ARG_VERBOSE_LOGS = "-verboseLogs";
        const string ARG_UPDATE_FREQUENCY = "-updateFrequency";

        const string ARG_FORCE_VERSION_ID = "-forceVersionId";
        const string ARG_FORCE_VERSION_GUID = "-forceVersionGuid";
        const string ARG_UPDATE_GITHUB_PAGE = "-updateGitHubPage";
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
        static bool VERBOSE_LOGS = false;
        static bool MANUAL_BUILD = false;

        static TrackMode TRACK_MODE = TrackMode.Client;
        static readonly Type DataMiner = typeof(DataMiner);

        public static bool UPDATE_GITHUB_PAGE = false;
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

            "CppTree.txt",
            "DeepStrings.txt",
        };

        static readonly IReadOnlyList<string> filesToCopy = new List<string>
        {
            "version.txt",
            "version-guid.txt",

            "API-Dump.json",
            "Full-API-Dump.json",

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

        static readonly ProcessStartInfo cmdExecute = new ProcessStartInfo
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        public static string branch = "roblox";
        public static string parent = "roblox";
        public static Channel channel = "LIVE";

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
            dir = dir.Replace("/", "\\");

            if (!dir.StartsWith(@"\\?\"))
                dir = @"\\?\" + dir;

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            return dir;
        }

        public static IEnumerable<string> cmd(string workDir, string name, params string[] args)
        {
            Directory.SetCurrentDirectory(workDir);

            string command = string.Join(" ", args);
            Process cmd;

            lock (cmdExecute)
            {
                cmdExecute.FileName = name;
                cmdExecute.Arguments = command;
                cmd = Process.Start(cmdExecute);
            }

            if (VERBOSE_LOGS)
                print($"> {name} {command}");

            var output = new List<string>();

            var processOutput = new Action<string, bool>((message, isError) =>
            {
                if (message != null && message.Length > 0)
                {
                    lock (output)
                    {
                        if (VERBOSE_LOGS || isError)
                        {
                            log($"[{name}] ", MAGENTA);
                            print(message, isError ? RED : WHITE);
                        }

                        output.Add(message);
                    }
                }
            });

            if (name != "cargo")
                cmd.ErrorDataReceived += new DataReceivedEventHandler
                    ((sender, evt) => processOutput(evt.Data, true));

            cmd.OutputDataReceived += new DataReceivedEventHandler
                ((sender, evt) => processOutput(evt.Data, false));

            cmd.BeginOutputReadLine();
            cmd.BeginErrorReadLine();
            cmd.WaitForExit();

            return output;
        }

        public static IEnumerable<string> git(params string[] args)
        {
            return cmd(stageDir, "git", args);
        }

        static bool reportChangedFiles(string filter = "*")
        {
            var query = git("status", "-s");
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
            }

            return query.Any();
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

        static bool stageCommit(string label, params string[] filters)
        {
            print($"\t[{label}] Checking in files...", YELLOW);

            foreach (string filter in filters)
                git($"add {filter}");

            if (reportChangedFiles())
            {
                print($"[{label}]\tCommitting...", CYAN);
                git($"commit -m \"{label}\"");
            }

            return true;
        }

        public static void cloneRepo(string repository, string stageDir)
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

        public static bool initGitBinding(string repository, bool soloBranched = false)
        {
            string localStageDir = soloBranched
                ? createDirectory(trunk, "stage", repository) 
                : stageDir;

            string gitBinding = Path.Combine(localStageDir, ".git");
            var settings = Settings.Default;
            var init = false;

            if (!Directory.Exists(gitBinding))
            {
                print($"Assembling {repository} stage @ {localStageDir}...", MAGENTA);
                cloneRepo(repository, localStageDir);
                init = true;
            }

            string name = settings.BotName;
            git("config", "--local", "user.name", $"\"{name}\"");

            string email = settings.BotEmail;
            git("config", "--local", "user.email", email);

            return init;
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
            if (UPDATE_GITHUB_PAGE)
                initGitBinding(Settings.Default.ApiSite, true);

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
                Channel = channel,
                GenerateMetadata = true,
                RemapExtraContent = true,
                CanShutdownStudio = false,
                OverrideStudioDirectory = studioDir
            };

            studioPath = studio.GetLocalStudioPath();
            studio.EchoFeed += new MessageFeed((msg) => print(msg, YELLOW));
            studio.StatusFeed += new MessageFeed((msg) => print(msg, MAGENTA));

            var dataMiners = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => !type.IsAbstract && type.IsSubclassOf(DataMiner))
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
                ClientVersionInfo info = await StudioBootstrapper.GetCurrentVersionInfo(channel, state.VersionData);
                
                if (!string.IsNullOrEmpty(FORCE_VERSION_ID))
                    info = new ClientVersionInfo(channel, FORCE_VERSION_ID, info.VersionGuid);

                if (!string.IsNullOrEmpty(FORCE_VERSION_GUID))
                    info = new ClientVersionInfo(channel, info.Version, FORCE_VERSION_GUID);

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
                            throw new Exception($"Missing file to copy: {sourcePath}!!");
                        
                        if (File.Exists(destination))
                            File.Delete(destination);

                        File.Copy(sourcePath, destination);
                    }

                    // Run data mining routines in parallel
                    // so they don't block each other.

                    var routines = new List<Task>();
                    state.Save(studioDir);

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
                    
                    if (UPDATE_GITHUB_PAGE)
                    {
                        string pageDir = Path.Combine(trunk, "stage", Settings.Default.ApiSite);
                        print("Updating API page...");

                        string versionId = info.Version
                            .Split('.')
                            .Skip(1)
                            .First();

                        await RobloxApiDumpTool.ArgProcessor.Run(new Dictionary<string, string>()
                        {
                            { "-updatePages", pageDir },
                            { "-version", versionId },
                            { "-full", "" },
                        });

                        Directory.SetCurrentDirectory(stageDir);
                    }

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
                        bool didStageScripts = stageCommit($"{versionId} (Scripts)", "*.lua", "*.luac", "*.luac.s");
                        bool didStageCore = stageCommit(versionId, "*.*");

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

            var settings = new JsonSerializerSettings()
            {
                ContractResolver = new OrderedContractResolver()
            };

            // Start tracking...
            git("reset --hard origin/main");
            git("pull");

            return startRoutineLoop(async () =>
            {
                var taskPool = new List<Task>();
                var sets = new ConcurrentDictionary<string, Dictionary<string, object>>();

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
                        catch
                        {
                            print($"\tError fetching FFlag platform: {platform}!", RED);
                            return;
                        }

                        using (var jsonText = new StringReader(json))
                        {
                            JsonTextReader reader = new JsonTextReader(jsonText);

                            JObject root = JObject.Load(reader);
                            JObject appSettings = root.Value<JObject>("applicationSettings");

                            var data = new Dictionary<string, object>();

                            foreach (var pair in appSettings)
                            {
                                string key = pair.Key;
                                string value = appSettings.Value<string>(key);
                                object insert;

                                if (value == "True")
                                    insert = true;
                                else if (value == "False")
                                    insert = false;
                                else if (long.TryParse(value, out long l))
                                    insert = l;
                                else if (value.StartsWith("True") || value.StartsWith("False"))
                                    insert = value.Substring(0, 1).ToLowerInvariant() + value.Substring(1);
                                else
                                    insert = value;

                                data.Add(key, insert);
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

                    var sorted = set
                        .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                        .ToDictionary(kv => kv.Key, kv => kv.Value);

                    string result = JsonConvert.SerializeObject(sorted, Formatting.Indented, settings);
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

                if (stageCommit(timeStamp, "*.*"))
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

            if (argMap.ContainsKey(ARG_CHANNEL))
                channel = argMap[ARG_CHANNEL];

            if (argMap.ContainsKey(ARG_PARENT))
                parent = argMap[ARG_PARENT];

            if (argMap.ContainsKey(ARG_FORCE_REBASE))
                FORCE_REBASE = true;

            if (argMap.ContainsKey(ARG_FORCE_UPDATE))
                FORCE_UPDATE = true;

            if (argMap.ContainsKey(ARG_FORCE_COMMIT))
                FORCE_COMMIT = true;

            if (argMap.ContainsKey(ARG_VERBOSE_LOGS))
                VERBOSE_LOGS = true;

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

            if (argMap.ContainsKey(ARG_UPDATE_GITHUB_PAGE))
                UPDATE_GITHUB_PAGE = true;

            if (TRACK_MODE == TrackMode.FastFlags)
            {
                if (!argMap.ContainsKey(ARG_UPDATE_FREQUENCY))
                    UPDATE_FREQUENCY = 2;

                branch = "fflags";
            }
            #endregion

            if (TRACK_MODE == TrackMode.Client && !argMap.ContainsKey(ARG_BRANCH))
            {
                switch (channel.Name)
                {
                    case "live":
                    {
                        // Nothing to change.
                        break;
                    }
                    case "zcanary":
                    {
                        branch = "zCanary";
                        break;
                    }

                    case "zintegration":
                    {
                        branch = "zIntegration";
                        parent = "zCanary";
                        break;
                    }

                    default:
                    {
                        branch = channel.Name;
                        parent = "zIntegration";
                        break;
                    }
                }
            }

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
