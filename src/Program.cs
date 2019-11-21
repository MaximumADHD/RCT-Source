using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

using RobloxFiles;

using Microsoft.Win32;
using RobloxClientTracker.Properties;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RobloxClientTracker
{
    public class Program
    {
        public enum TrackMode
        {
            Client,
            FastFlags
        }

        public static RegistryKey RootRegistry;
        public static RegistryKey BranchRegistry;

        public static Encoding UTF8 = new UTF8Encoding(false);

        const string ARG_BRANCH = "-branch";
        const string ARG_PARENT = "-parent";
        const string ARG_TRACK_MODE = "-trackMode";
        const string ARG_FORCE_REBASE = "-forceRebase";
        const string ARG_FORCE_UPDATE = "-forceUpdate";
        const string ARG_FORCE_COMMIT = "-forceCommit";
        const string ARG_VERBOSE_GIT_LOGS = "-verboseGitLogs";
        const string ARG_UPDATE_FREQUENCY = "-updateFrequency";
        const string ARG_FORCE_VERSION_GUID = "-forceVersionGuid";

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
        static TrackMode TRACK_MODE = TrackMode.Client;

        public static string FORCE_VERSION_GUID = "";
        public static string FORCE_VERSION_ID = "";

        static List<string> boringFiles = new List<string>()
        {
            "rbxManifest.txt",
            "rbxPkgManifest.txt",
            "rbxManifest.csv",
            "rbxPkgManifest.csv",
            "version.txt",
            "version-guid.txt",
            "FVariables.txt",
            "DeepStrings.txt",
        };

        static string branch = "roblox";
        static string parent = "roblox";

        public static string Trunk;
        public static string StageDir;

        public static string StudioPath;
        public static StudioBootstrapper Studio;

        static Dictionary<string, string> argMap = new Dictionary<string, string>();
        
        static readonly ProcessStartInfo gitExecute = new ProcessStartInfo
        {
            FileName = "git",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        public static readonly FileLogConfig LogRbxm = new FileLogConfig()
        {
            Color = DARK_CYAN,
            Stack = 3
        };

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

        public static string localPath(string globalPath)
        {
            return globalPath.Substring(StageDir.Length + 1);
        }

        static string sanitizeString(string str)
        {
            string sanitized = str
                .Replace("\r\r", "\r")
                .Replace("\n", "\r\n")
                .Replace("\r\r", "\r");

            return sanitized;
        }

        public static void WriteFile(string path, string contents, FileLogConfig? maybeConfig = null)
        {
            string sanitized = sanitizeString(contents);
            
            if (maybeConfig.HasValue)
            {
                FileLogConfig config = maybeConfig.Value;

                for (int i = 0; i < config.Stack; i++)
                    Console.Write('\t');

                print($"Writing file: {localPath(path)}", config.Color);
            }
            
            File.WriteAllText(path, sanitized, UTF8);
        }

        public static IEnumerable<string> git(params string[] args)
        {
            Directory.SetCurrentDirectory(StageDir);

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

        static string getBranchHash(string branchName)
        {
            var query = git("rev-parse", branchName);
            return query.First();
        }

        static List<string> getChangedFiles(string branch, string filter)
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

                switch (type)
                {
                    case "A":
                        log(type, GREEN);
                        break;
                    case "D":
                        log(type, RED);
                        break;
                    case "R":
                        log(type, MAGENTA);
                        break;
                    case "C":
                        log(type, CYAN);
                        break;
                    case "M":
                        log(type, YELLOW);
                        break;
                    case "U":
                        log(type, GRAY);
                        break;
                    default:
                        log("?", BLUE);
                        break;
                    //
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

        public static string CreateDirectory(params string[] traversal)
        {
            string dir = Path.Combine(traversal);

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            return dir;
        }

        public static string ResetDirectory(params string[] traversal)
        {
            string dir = Path.Combine(traversal);

            if (Directory.Exists(dir))
                Directory.Delete(dir, true);

            return CreateDirectory(dir);
        }

        static void copyDirectory(string source, string target)
        {
            string dest = ResetDirectory(target);
            DirectoryInfo src = new DirectoryInfo(source);

            foreach (var file in src.GetFiles())
            {
                string destPath = Path.Combine(dest, file.Name);
                file.CopyTo(destPath);
            }

            foreach (var subDir in src.GetDirectories())
            {
                string subSrc = subDir.FullName;
                string subTarget = Path.Combine(dest, subDir.Name);
                copyDirectory(subSrc, subTarget);
            }
        }
        
        static void expandBinaryRbxmFile(Instance inst, string parentDir)
        {
            string name = inst.Name;

            if (inst.IsA<Folder>() && name == "Packages")
                return;

            if (inst.IsA<LuaSourceContainer>())
            {
                string extension = "";
                string source = "";

                if (inst.IsA<ModuleScript>())
                {
                    var module = inst.Cast<ModuleScript>();
                    source = module.Source;
                    extension = ".lua";
                }
                else if (inst.IsA<Script>())
                {
                    var script = inst.Cast<Script>();

                    if (script.IsA<LocalScript>())
                        extension = ".client.lua";
                    else
                        extension = ".server.lua";

                    source = script.Source;
                }

                source = sanitizeString(source);

                if (source.Length > 0)
                {
                    string filePath = Path.Combine(parentDir, name + extension);
                    WriteFile(filePath, source, LogRbxm);
                }
            }
            else if (inst.IsA<LocalizationTable>())
            {
                var table = inst.Cast<LocalizationTable>();
                var csvTable = new CsvLocalizationTable(table);

                string csv = csvTable.WriteCsv();
                string filePath = Path.Combine(parentDir, name + ".csv");

                WriteFile(filePath, csv, LogRbxm);
            }
            else if (inst.IsA<StringValue>() && name != "AvatarPartScaleType")
            {
                string value = inst
                    .Cast<StringValue>()
                    .Value;

                if (value.Length > 0)
                {
                    string filePath = Path.Combine(parentDir, name + ".txt");
                    WriteFile(filePath, value, LogRbxm);
                }
            }

            var children = inst
                .GetChildren()
                .ToList();

            if (children.Count > 0)
            {
                string instanceDir = CreateDirectory(parentDir, name);
                children.ForEach(child => expandBinaryRbxmFile(child, instanceDir));
            }
        }

        static void expandBinaryRbxmFile(string filePath)
        {
            FileInfo info = new FileInfo(filePath);

            if (info.Exists && (info.Extension == ".rbxm" || info.Extension == ".rbxmx"))
            {
                RobloxFile file = RobloxFile.Open(filePath);
                Instance[] children = file.GetChildren();

                File.Delete(filePath);

                if (children.Length == 1)
                {
                    Instance project = children[0];
                    project.Name = info.Name.Replace(info.Extension, "");
                    expandBinaryRbxmFile(project, info.DirectoryName);
                }
            }
        }

        static string permutate(string file, Func<string, string> permutation)
        {
            string[] lines = file.Split('\r', '\n')
                .Where(line => line.Length > 0)
                .Select(line => permutation(line))
                .ToArray();

            return string.Join("\n", lines);
        }

        static string flipColumns(string line)
        {
            string[] pair = line.Split(',');
            return pair[1] + ',' + pair[0];
        }

        static async Task<bool> updateStage(ClientVersionInfo info)
        {
            // Make sure Roblox Studio is up to date for this build.
            print("Syncing Roblox Studio...", ConsoleColor.Green);
            await Studio.UpdateStudio();

            // Copy some metadata generated during the studio installation.
            string studioDir = Studio.GetStudioDirectory();

            var filesToCopy = new List<string>
            {
                "version.txt",
                "version-guid.txt",

                "API-Dump.json",

                "rbxManifest.txt",
                "rbxPkgManifest.txt",

                "ReflectionMetadata.xml",
                "RobloxStudioRibbon.xml"
            };

            foreach (string fileName in filesToCopy)
            {
                string sourcePath = Path.Combine(studioDir, fileName);
                string destination = Path.Combine(StageDir, fileName);

                if (!File.Exists(sourcePath))
                    throw new Exception($"Missing file to copy: {sourcePath}!!");

                if (File.Exists(destination))
                    File.Delete(destination);

                File.Copy(sourcePath, destination);
            }

            // Convert rbxPkgManifest.txt -> rbxPkgManifest.csv
            Task rbxPkgManifestCsv = Task.Run(() =>
            {
                string pkgPath = Path.Combine(StageDir, "rbxPkgManifest.txt");
                string pkgCsvPath = Path.Combine(StageDir, "rbxPkgManifest.csv");

                var pkgHeaders = new string[4]
                {
                    "File Name",
                    "MD5 Signature",
                    "Compressed Size (bytes)",
                    "Size (bytes)"
                };

                CsvBuilder.Convert(pkgPath, pkgHeaders, csv => WriteFile(pkgCsvPath, csv));
            });

            // Convert rbxManifest.txt -> rbxManifest.csv
            Task rbxManifestCsv = Task.Run(() =>
            {
                string manifestPath = Path.Combine(StageDir, "rbxManifest.txt");
                string manifestCsvPath = Path.Combine(StageDir, "rbxManifest.csv");

                var manifestHeaders = new string[2] 
                {
                    "File Name",
                    "MD5 Signature"
                };

                CsvBuilder.Convert(manifestPath, manifestHeaders, (csv) =>
                {
                    string manifestCsv = permutate(csv, flipColumns);
                    WriteFile(manifestCsvPath, manifestCsv);
                });
            });

            // Run some other data mining tasks in parallel so they don't block each other.
            var taskPool = new List<Task>()
            {
                rbxPkgManifestCsv,
                rbxManifestCsv
            };

            var minerActions = new List<Action>
            {
                ApiDump.Extract,
                FastFlags.Extract,
                ShaderData.Extract,
                QtResources.Extract,
                StudioStrings.Extract
            };

            foreach (Action minerAction in minerActions)
            {
                Task minerTask = Task.Run(minerAction);
                taskPool.Add(minerTask);
            }
            
            // Unpack and transfer specific content data from the studio build.
            print("Unpacking content data...", CYAN);
            
            var contentFolders = new Dictionary<string, string>
            {
                { "BuiltInPlugins", "" },
                { "BuiltInStandalonePlugins", "" },

                { "avatar", @"content\avatar" },
                { "scripts", @"content\scripts" },

                { "LuaPackages", @"content\LuaPackages" },
                { "translations", @"content\translations" }
            };

            foreach (string destPath in contentFolders.Keys)
            {
                string srcPath = contentFolders[destPath];

                if (srcPath == "")
                    srcPath = destPath;
                
                Task unpack = Task.Run(() =>
                {
                    string srcFolder = Path.Combine(studioDir, srcPath);
                    string destFolder = ResetDirectory(StageDir, destPath);

                    print($"\tCopying {srcFolder} to {destFolder}", CYAN);
                    copyDirectory(srcFolder, destFolder);

                    foreach (string file in Directory.GetFiles(destFolder, "*.*", SearchOption.AllDirectories))
                    {
                        if (file.EndsWith(".rbxm") || file.EndsWith(".rbxmx"))
                        {
                            print($"\t\tUnpacking {localPath(file)}", CYAN);
                            expandBinaryRbxmFile(file);
                        }
                        else if (file.EndsWith(".sig") || file.EndsWith(".mesh"))
                        {
                            File.Delete(file);
                        }
                        else if (file.EndsWith(".lua"))
                        {
                            string source = File.ReadAllText(file);
                            string newSource = sanitizeString(source);

                            if (source != newSource)
                            {
                                WriteFile(file, newSource);
                            }
                        }
                    }
                });

                taskPool.Add(unpack);
            }

            await Task.WhenAll(taskPool);

            foreach (Task task in taskPool)
                if (task.Status == TaskStatus.Faulted)
                    throw task.Exception;

            return true;
        }

        static bool pushCommit(string label, string filter = ".")
        {
            print($"\t[{label}] Checking in files...", YELLOW);
            git($"add {filter}");

            // Verify this update is worth committing.
            var files = getChangedFiles(branch, filter);

            int updateCount = 0;
            int boringCount = 0;

            foreach (string file in files)
            {
                try
                {
                    string filePath = StageDir + '\\' + file;
                    FileInfo fileInfo = new FileInfo(filePath);

                    if (boringFiles.Contains(fileInfo.Name) || fileInfo.Extension.Contains("rbx"))
                    {
                        boringCount++;
                        continue;
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

                print($"[{label}]\tPushing commit...", CYAN);
                git($"push");

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

        static bool initGitBinding(string repository)
        {
            string gitBinding = Path.Combine(StageDir, ".git");

            if (!Directory.Exists(gitBinding))
            {
                print($"Assembling stage for {branch}...", MAGENTA);

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

                git($"clone -c core.sshCommand=\"{sshCommand}\" {repoUrl} {StageDir}");

                string name = settings.BotName;
                git("config", "--local", "user.name", '"' + name + '"');

                string email = settings.BotEmail;
                git("config", "--local", "user.email", email);

                return true;
            }

            return false;
        }

        static async Task TrackFFlagsAsync()
        {
            // Initialize Repository
            initGitBinding(Settings.Default.FFlagRepoName);

            // Start tracking...
            const string flagEndpoint = "https://clientsettingscdn.roblox.com/v1/settings/application?applicationName=";

            var platforms = new List<string>()
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
                "StudioApp"
            };

            print("Main thread starting!", MAGENTA);
            git("reset --hard origin/master");

            while (true)
            {
                print("Updating flags...", CYAN);

                var taskPool = new List<Task>();

                foreach (string platform in platforms)
                {
                    Task updatePlatform = Task.Run(async () =>
                    {
                        string json = "";

                        using (WebClient http = new WebClient())
                        {
                            http.Headers.Set("UserAgent", "Roblox/WinInet");
                            json = await http.DownloadStringTaskAsync(flagEndpoint + platform);
                        }

                        using (var jsonText = new StringReader(json))
                        {
                            JsonTextReader reader = new JsonTextReader(jsonText);

                            JObject root = JObject.Load(reader);
                            JObject appSettings = root.Value<JObject>("applicationSettings");

                            var keys = new List<string>();

                            foreach (var pair in appSettings)
                                keys.Add(pair.Key);

                            var result = new StringBuilder();
                            int testInt = 0;

                            result.AppendLine("{");
                            keys.Sort();

                            for (int i = 0; i < keys.Count; i++)
                            {
                                string key = keys[i];
                                string value = appSettings.Value<string>(key);

                                if (i != 0)
                                    result.Append(",\r\n");

                                if (value == "True" || value == "False")
                                    value = value.ToLower();
                                else if (!int.TryParse(value, out testInt))
                                    value = '"' + value + '"';
                                 
                                result.Append($"\t\"{key}\": {value}");
                            }

                            result.Append("\r\n}");

                            string filePath = Path.Combine(StageDir, platform + ".json");
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
                    });

                    taskPool.Add(updatePlatform);
                }

                await Task.WhenAll(taskPool);

                string timeStamp = DateTime.Now.ToString();
                pushCommit(timeStamp);

                print($"Next update check in {UPDATE_FREQUENCY} minutes.", YELLOW);
                await Task.Delay(UPDATE_FREQUENCY * 60000);
            }
        }

        static async Task TrackClientAsync()
        {
            string currentVersion = BranchRegistry.GetString("Version");
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

            Studio = new StudioBootstrapper(branch);
            StudioPath = Studio.GetStudioPath();

            print("Main thread starting!", MAGENTA);

            if (FORCE_REBASE)
                print("\tCaution: FORCE_REBASE is set to true!", YELLOW);

            if (FORCE_UPDATE)
                print("\tCaution: FORCE_UPDATE is set to true!", YELLOW);

            if (FORCE_COMMIT)
                print("\tCaution: FORCE_COMMIT is set to true!", YELLOW);

            while (true)
            {
                print("Checking for updates...", CYAN);

                try
                {
                    // Check if the parent branch has been updated
                    if (branch != parent)
                    {
                        // Check if we are behind the upstream.
                        if (FORCE_REBASE || isRemoteBehind($"origin/{branch}", $"origin/{parent}"))
                        {
                            // Discard any local changes that might still be lingering.
                            string branchHash = getBranchHash($"origin/{branch}");
                            git("reset", "--hard", branchHash);
                            git("clean -d -f");

                            // Merge with the parent upstream, keeping our own changes.
                            // The assumption right now is that child branches are
                            // ahead of the parent branches and will replace them.
                            string message = $"Merge {parent}->{branch}";
                            print($"Merging ({parent}->{branch})...", MAGENTA);

                            git("merge", $"-m \"{message}\"", "-X ours", $"origin/{parent}");
                            git("push");

                            currentVersion = "";
                        }
                    }

                    // Check for updates to the version
                    var info = await StudioBootstrapper.GetCurrentVersionInfo(branch);

                    if (FORCE_UPDATE || info.Guid != currentVersion)
                    {
                        print("Update detected!", YELLOW);
                        await updateStage(info);

                        // Create two commits:
                        // - One for package files.
                        // - One for everything else.

                        string versionId = info.Version;
                        print("Creating commits...", YELLOW);

                        bool didSubmitPackages = pushCommit($"{versionId} (Packages)", "*/Packages/*");
                        bool didSubmitCore = pushCommit(versionId);

                        if (didSubmitPackages || didSubmitCore)
                            print("Done!", GREEN);

                        currentVersion = info.Guid;
                        BranchRegistry.SetValue("Version", info.Guid);
                    }
                    else
                    {
                        print("No updates right now!", GREEN);
                    }

                    print($"Next update check in {UPDATE_FREQUENCY} minutes.", YELLOW);
                    await Task.Delay(UPDATE_FREQUENCY * 60000);
                }
                catch (Exception e)
                {
                    print($"Exception Thrown: {e.Message}\n{e.StackTrace}\nTiming out for 1 minute.", RED);
                    await Task.Delay(60000);
                }
            }
        }

        static void Main(string[] args)
        {
            #region Process Launch Options
            string argKey = "";

            foreach (string arg in args)
            {
                if (arg.StartsWith("-"))
                {
                    if (argKey != "")
                        argMap.Add(argKey, "");

                    argKey = arg;
                }
                else if (argKey != "")
                {
                    argMap.Add(argKey, arg);
                    argKey = "";
                }
            }

            if (argKey != "")
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

            if (argMap.ContainsKey(ARG_UPDATE_FREQUENCY))
                int.TryParse(argMap[ARG_UPDATE_FREQUENCY], out UPDATE_FREQUENCY);

            if (argMap.ContainsKey(ARG_FORCE_VERSION_GUID))
                FORCE_VERSION_GUID = argMap[ARG_FORCE_VERSION_GUID];

            if (argMap.ContainsKey(ARG_TRACK_MODE))
                Enum.TryParse(argMap[ARG_TRACK_MODE], out TRACK_MODE);

            if (TRACK_MODE == TrackMode.FastFlags)
            {
                if (!argMap.ContainsKey(ARG_UPDATE_FREQUENCY))
                    UPDATE_FREQUENCY = 2;

                branch = "fflags";
            }
            #endregion

            RootRegistry = Registry.CurrentUser.Open("Software", "RobloxClientTracker");
            BranchRegistry = RootRegistry.Open(branch);

            Trunk = CreateDirectory(@"C:\Roblox-Client-Tracker");
            StageDir = CreateDirectory(Trunk, "stage", branch);

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12 | SecurityProtocolType.Ssl3;
            Task mainThread = null;

            if (TRACK_MODE == TrackMode.Client)
                mainThread = Task.Run(() => TrackClientAsync());
            else if (TRACK_MODE == TrackMode.FastFlags)
                mainThread = Task.Run(() => TrackFFlagsAsync());

            mainThread?.Wait();
        }
    }
}