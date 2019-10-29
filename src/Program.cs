using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

using RobloxFiles;
using Roblox.Reflection;

using Microsoft.Win32;
using RobloxClientTracker.Properties;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RobloxClientTracker
{
    class Program
    {
        public static RegistryKey RootRegistry;
        public static RegistryKey BranchRegistry;

        const string ARG_BRANCH = "-branch";
        const string ARG_PARENT = "-parent";
        const string ARG_FORCE_REBASE = "-forceRebase";
        const string ARG_FORCE_UPDATE = "-forceUpdate";
        const string ARG_FORCE_COMMIT = "-forceCommit";
        const string ARG_VERBOSE_GIT_LOGS = "-verboseGitLogs";
        const string ARG_UPDATE_FREQUENCY = "-updateFrequency";
        const string ARG_FORCE_VERSION_GUID = "-forceVersionGuid";

        const ConsoleColor DARK_YELLOW = ConsoleColor.DarkYellow;
        const ConsoleColor DARK_CYAN = ConsoleColor.DarkCyan;

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

        static Dictionary<string, byte[]> luaFiles = new Dictionary<string, byte[]>
        {
            { "QtExtract.lua",     Resources.QtExtract_lua    },
            { "PEParser.lua",      Resources.PEParser_lua     },
            { "BinaryReader.lua",  Resources.BinaryReader_lua },
            { "Deflate.lua",       Resources.Deflate_lua      },
            { "Bit.lua",           Resources.Bit_lua          },
        };

        static string branch = "roblox";
        static string parent = "roblox";

        static string trunk;
        static string stageDir;

        static string studioPath;
        static StudioBootstrapper studio;

        public static Encoding UTF8 = new UTF8Encoding(false);
        public static string Trunk => trunk;

        static Dictionary<string, string> argMap = new Dictionary<string, string>();
        
        static ProcessStartInfo gitExecute = new ProcessStartInfo
        {
            FileName = "git",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
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
            return globalPath.Substring(stageDir.Length + 1);
        }

        static string sanitizeString(string str)
        {
            string sanitized = str
                .Replace("\r\r", "\r")
                .Replace("\n", "\r\n")
                .Replace("\r\r", "\r");

            return sanitized;
        }

        public static void WriteFile(string path, string contents)
        {
            string sanitized = sanitizeString(contents);
            File.WriteAllText(path, sanitized, UTF8);
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

        static string getBranchHash(string branchName)
        {
            var query = git("rev-parse", branchName);
            return query.First();
        }

        static List<string> getChangedFiles(string branch)
        {
            var query = git("diff", branch, "--name-status");
            var result = new List<string>();

            foreach (string line in query)
            {
                string[] data = line.Split('\t');

                string flag = data[0];
                string name = data[1];
                
                result.Add(name);
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

        static string createDirectory(params string[] traversal)
        {
            string dir = Path.Combine(traversal);

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            return dir;
        }

        static string resetDirectory(params string[] traversal)
        {
            string dir = Path.Combine(traversal);

            if (Directory.Exists(dir))
                Directory.Delete(dir, true);

            return createDirectory(dir);
        }

        static void copyDirectory(string source, string target)
        {
            string dest = resetDirectory(target);
            DirectoryInfo src = new DirectoryInfo(source);

            // HACK
            if (src.Name == "Packages")
                return;

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

        static void deployLuaJit(string dir)
        {
            resetDirectory(dir);

            using (var stream = new MemoryStream(Resources.LuaJIT_zip))
            {
                ZipArchive luaJit = new ZipArchive(stream, ZipArchiveMode.Read);

                foreach (ZipArchiveEntry entry in luaJit.Entries)
                {
                    string fullPath = Path.Combine(dir, entry.FullName);

                    if (fullPath.EndsWith("/"))
                    {
                        Directory.CreateDirectory(fullPath);
                        continue;
                    }

                    entry.ExtractToFile(fullPath);
                }
            }

            foreach (string fileName in luaFiles.Keys)
            {
                byte[] luaFile = luaFiles[fileName];
                string filePath = Path.Combine(dir, fileName);

                File.WriteAllBytes(filePath, luaFile);
            }
        }

        static void extractQtResources()
        {
            string extractDir = resetDirectory(stageDir, "QtResources");
            string luaDir = Path.Combine(trunk, "lua");

            string luaJit = Path.Combine(luaDir, "luajit.cmd");
            string qtExtract = Path.Combine(luaDir, "QtExtract.lua");

            if (!File.Exists(luaJit) || !File.Exists(qtExtract))
            {
                print("Deploying LuaJIT...", MAGENTA);
                deployLuaJit(luaDir);
            }

            ProcessStartInfo extract = new ProcessStartInfo()
            {
                FileName = luaJit,
                Arguments = $"{qtExtract} {studioPath} --chunk 1 --output {extractDir}",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            print("Extracting Qt Resources...", MAGENTA);

            Process process = Process.Start(extract);
            process.WaitForExit();

            foreach (string file in Directory.GetFiles(extractDir, "*.xml", SearchOption.AllDirectories))
            {
                FileInfo info = new FileInfo(file);
                string newPath = Path.Combine(stageDir, info.Name);

                if (File.Exists(newPath))
                    File.Delete(newPath);

                File.Move(file, newPath);
            }
        }

        static void extractShaderData()
        {
            string studioDir = studio.GetStudioDirectory();
            string shaderDir = Path.Combine(studioDir, "shaders");

            var packNames = new List<string>();

            var shaders = new Dictionary<string, string>();
            var shaderPacks = new Dictionary<string, HashSet<string>>();

            string newShaderDir = resetDirectory(stageDir, "shaders");
            print("Unpacking shader packs...", GREEN);

            foreach (string shaderPath in Directory.GetFiles(shaderDir))
            {
                ShaderPack pack = new ShaderPack(shaderPath);
                var myShaders = new Dictionary<string, string>();

                string packName = pack.Name.Replace("shaders_", "");
                packNames.Add(packName);

                List<ShaderFile> shaderFiles = pack.Shaders.ToList();
                shaderFiles.Sort();

                print($"\tUnpacking shader file {packName}...", GREEN);
                pack.UnpackShader(newShaderDir);

                foreach (ShaderFile file in shaderFiles)
                {
                    string shaderType = Enum.GetName(typeof(ShaderType), file.ShaderType);
                    string shader = file.Name;

                    if (shaders.ContainsKey(shader) && shaderType != shaders[shader])
                        Debugger.Break();

                    shaders[shader] = shaderType;
                    myShaders[shader] = shaderType;

                    if (!shaderPacks.ContainsKey(shader))
                        shaderPacks.Add(shader, new HashSet<string>());

                    shaderPacks[shader].Add(packName);
                }

                var myLines = new List<string>();

                foreach (string shader in myShaders.Keys)
                {
                    string type = myShaders[shader];
                    myLines.Add(shader);
                    myLines.Add(type);
                }

                string[] myManifestLines = myLines
                    .Select(line => line.ToString())
                    .ToArray();

                string myManifest = string.Join("\r\n", myManifestLines);
                myManifest = convertToCSV(myManifest, "Name", "Shader Type");

                string newShaderPathCsv = Path.Combine(newShaderDir, pack.Name + ".csv");
                WriteFile(newShaderPathCsv, myManifest);
            }

            var headers = new List<string>() { "Name", "Shader Type" };
            headers.AddRange(packNames);

            var shaderNames = shaders.Keys.ToList();
            shaderNames.Sort();

            var lines = new List<string>();

            foreach (string shader in shaderNames)
            {
                string type = shaders[shader];
                var packs = shaderPacks[shader];

                lines.Add(shader);
                lines.Add(type);

                foreach (string packName in packNames)
                {
                    string check = packs.Contains(packName) ? "✔" : "❌";
                    lines.Add(check);
                }
            }

            string manifest = string.Join("\r\n", lines);
            manifest = convertToCSV(manifest, headers.ToArray());

            string manifestPath = Path.Combine(stageDir, "RobloxShaderData.csv");
            WriteFile(manifestPath, manifest);

            print("Shaders unpacked!", GREEN);
        }

        static void scanFastFlags()
        {
            string localAppData = Environment.GetEnvironmentVariable("LocalAppData");
            string clientSettings = resetDirectory(localAppData, "Roblox", "ClientSettings");

            string settingsPath = Path.Combine(clientSettings, "StudioAppSettings.json");
            File.WriteAllText(settingsPath, "");

            SystemEvent show = new SystemEvent("StudioNoSplashScreen");
            SystemEvent start = new SystemEvent("ClientTrackerFlagScan");

            print("Starting FFlag scan...", YELLOW);

            Process update = Process.Start(new ProcessStartInfo()
            {
                FileName = studioPath,
                Arguments = $"-startEvent {start.Name} -showEvent {show.Name}"
            });

            print("\tWaiting for signal from studio...", YELLOW);
            start.WaitOne();

            int timeOut = 0;
            const int numTries = 128;

            print("\tWaiting for StudioAppSettings.json to be written...", YELLOW);
            FileInfo info = new FileInfo(settingsPath);

            while (timeOut < numTries)
            {
                info.Refresh();

                if (info.Length > 0)
                {
                    update.Kill();
                    break;
                }

                print($"\t\t({++timeOut}/{numTries} tries until giving up...)", YELLOW);

                var delay = Task.Delay(30);
                delay.Wait();
            }

            string file = File.ReadAllText(settingsPath);
            var flags = new List<string>();

            using (var jsonText = new StringReader(file))
            {
                JsonTextReader reader = new JsonTextReader(jsonText);
                JObject flagData = JObject.Load(reader);

                foreach (var pair in flagData)
                {
                    string flagName = pair.Key;
                    flags.Add(flagName);
                }
            }

            flags.Sort();
            print("Flag Scan completed!", YELLOW);

            string flagsPath = Path.Combine(stageDir, "FVariables.txt");
            string result = string.Join("\r\n", flags);

            WriteFile(flagsPath, result);
        }

        static void updateApiDump()
        {
            string jsonFile = Path.Combine(stageDir, "API-Dump.json");
            string json = File.ReadAllText(jsonFile);

            var api = new ReflectionDatabase(jsonFile);
            var dumper = new ReflectionDumper(api);

            string dump = dumper.DumpApi(ReflectionDumper.DumpUsingTxt);
            string exportPath = Path.Combine(stageDir, "API-Dump.txt");

            WriteFile(exportPath, dump);
        }

        static List<string> hackOutPattern(string source, string pattern)
        {
            MatchCollection matches = Regex.Matches(source, pattern);
            List<string> lines = new List<string>();

            foreach (Match match in matches)
            {
                string matchStr = match.Groups[0].ToString();

                if (matchStr.Length <= 4)
                    continue;

                if (lines.Contains(matchStr))
                    continue;

                string firstChar = matchStr.Substring(0, 1);
                Match sanitize = Regex.Match(firstChar, "^[A-z_%*'-]");

                if (sanitize.Length == 0)
                    continue;

                lines.Add(matchStr);
            }

            lines.Sort();
            return lines;
        }

        static void extractDeepStrings(string file)
        {
            print("Extracting Deep Strings...", MAGENTA);
            MatchCollection matches = Regex.Matches(file, "([A-Z][A-z][A-z_0-9.]{8,256})+[A-z0-9]?");

            var lines = matches.Cast<Match>()
                .Select(match => match.Value)
                .OrderBy(str => str)
                .Distinct();

            string deepStrings = string.Join("\n", lines);
            string deepStringsPath = Path.Combine(stageDir, "DeepStrings.txt");

            WriteFile(deepStringsPath, deepStrings);
        }

        static string extractCppTypes(string file)
        {
            print("Extracting CPP types...", MAGENTA);

            List<string> classes = hackOutPattern(file, "AV[A-z][A-z0-9_@?]+");
            List<string> enums = hackOutPattern(file, "AW4[A-z0-9_@]+");

            StringBuilder builder = new StringBuilder();
            NameDemangler RBX = new NameDemangler("RBX");

            foreach (string linkerChunk in classes)
            {
                if (linkerChunk.Length > 8)
                {
                    string data = linkerChunk.Substring(2);
                    string lineEnd = data.Substring(data.Length - 6);

                    if (lineEnd == "@RBX@@")
                    {
                        string[] chunks = data.Split('@');
                        NameDemangler currentTree = RBX;

                        for (int i = chunks.Length - 1; i >= 0; i--)
                        {
                            string chunk = chunks[i];

                            if (chunk.Length > 0 && chunk != "RBX")
                            {
                                currentTree = currentTree.Add(chunk);
                            }
                        }
                    }
                }
            }

            RBX.WriteTree(builder);
            return builder.ToString();
        }

        static void extractStudioStrings()
        {
            string file = File.ReadAllText(studioPath);

            Task cppTypes = Task.Run(() => extractCppTypes(file));
            Task deepStrings = Task.Run(() => extractDeepStrings(file));

            Task extraction = Task.WhenAll(cppTypes, deepStrings);
            extraction.Wait();
        }

        static string localeTableToCSV(LocalizationTable table)
        {
            Type entryType = typeof(LocalizationEntry);
            var entries = LocalizationEntry.GetEntries(table);

            // Select all distinct language definitions from the table entries.
            string[] languages = entries
                .SelectMany(entry => entry.Values
                    .Where(pair => pair.Value.Length > 0)
                    .Select(pair => pair.Key))
                .Distinct()
                .OrderBy(key => key)
                .ToArray();

            // Select headers whose columns actually have content.
            string[] headers = new string[3] { "Key", "Source", "Context" }
                .Select(header => entryType.GetField(header))
                .Where(field => entries
                    .Any(entry => field.GetValue(entry) as string != ""))
                .Select(field => field.Name)
                .Concat(languages)
                .ToArray();

            var lines = new List<string>();
            var fields = new Dictionary<string, FieldInfo>();

            foreach (LocalizationEntry entry in entries)
            {
                foreach (string header in headers)
                {
                    string value = "";

                    if (entry.Values.ContainsKey(header))
                    {
                        value = entry.Values[header];
                    }
                    else
                    {
                        try
                        {
                            if (!fields.ContainsKey(header))
                                fields.Add(header, entryType.GetField(header));

                            value = fields[header].GetValue(entry) as string;
                        }
                        catch
                        {
                            value = " ";
                        }
                    }

                    if (value == null)
                        value = " ";

                    if (value.Contains(","))
                    {
                        if (value.Contains("\""))
                        {
                            value = value.Replace("\"", "\\\"");
                            value = value.Replace("\\\\\"", "\\\"");
                        }

                        value = '"' + value + '"';
                    }

                    lines.Add(value);
                }
            }

            string manifest = string.Join("\r\n", lines);
            return convertToCSV(manifest, headers);
        }

        static void expandBinaryRbxmFile(Instance at, string directory)
        {
            string name = at.Name;

            if (at.IsA<Folder>() && name == "Packages")
                return;

            if (at.IsA<LuaSourceContainer>())
            {
                string extension = "";
                string source = "";

                if (at.IsA<ModuleScript>())
                {
                    var module = at.Cast<ModuleScript>();
                    source = module.Source;
                    extension = ".lua";
                }
                else if (at.IsA<Script>())
                {
                    var script = at.Cast<Script>();

                    if (script.IsA<LocalScript>())
                        extension = ".client.lua";
                    else
                        extension = ".server.lua";

                    source = script.Source;
                }

                source = sanitizeString(source);

                if (source.Length > 0)
                {
                    string filePath = Path.Combine(directory, name + extension);
                    WriteFile(filePath, source);
                }
            }
            else if (at.IsA<LocalizationTable>())
            {
                var table = at.Cast<LocalizationTable>();
                string csv = localeTableToCSV(table);

                if (csv.Length > 0)
                {
                    string filePath = Path.Combine(directory, name + ".csv");
                    WriteFile(filePath, csv);
                }
            }
            else if (at.IsA<StringValue>() && name != "AvatarPartScaleType")
            {
                var vString = at.Cast<StringValue>();
                string value = vString.Value;

                if (value.Length > 0)
                {
                    string filePath = Path.Combine(directory, name + ".txt");
                    WriteFile(filePath, value);
                }
            }

            var children = at
                .GetChildren()
                .ToList();

            if (children.Count > 0)
            {
                string childDir = createDirectory(directory, name);
                children.ForEach(child => expandBinaryRbxmFile(child, childDir));
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
                else
                {
                    Debugger.Break();
                }
            }
        }

        static string convertToCSV(string file, params string[] headers)
        {
            int column = 0;

            string[] lines = file.Split('\r', '\n')
                .Where(line => line.Length > 0)
                .ToArray();

            string csv = string.Join(",", headers);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (i == 0 && line == "v0")
                    continue;

                if (column++ % headers.Length == 0)
                    csv += '\n';
                else
                    csv += ',';

                csv += line;
            }

            return csv;
        }

        static void convertToCSV(string path, string[] headers, Action<string> andThen)
        {
            string file = File.ReadAllText(path);
            string csv = convertToCSV(file, headers);
            andThen(csv);
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

        static async Task updateStage(ClientVersionInfo info)
        {
            // Make sure Roblox Studio is up to date for this build.
            print("Syncing Roblox Studio...", ConsoleColor.Green);
            await studio.UpdateStudio();

            // Copy some metadata generated during the studio installation.
            string studioDir = studio.GetStudioDirectory();

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
                string destination = Path.Combine(stageDir, fileName);

                if (!File.Exists(sourcePath))
                    throw new Exception($"Missing file to copy: {sourcePath}!!");

                if (File.Exists(destination))
                    File.Delete(destination);

                File.Copy(sourcePath, destination);
            }

            // Convert rbxPkgManifest.txt -> rbxPkgManifest.csv
            Task rbxPkgManifestCsv = Task.Run(() =>
            {
                string pkgPath = Path.Combine(stageDir, "rbxPkgManifest.txt");
                string pkgCsvPath = Path.Combine(stageDir, "rbxPkgManifest.csv");

                var pkgHeaders = new string[4]
                {
                    "File Name",
                    "MD5 Signature",
                    "Compressed Size (bytes)",
                    "Size (bytes)"
                };

                convertToCSV(pkgPath, pkgHeaders, csv => WriteFile(pkgCsvPath, csv));
            });

            // Convert rbxManifest.txt -> rbxManifest.csv
            Task rbxManifestCsv = Task.Run(() =>
            {
                string manifestPath = Path.Combine(stageDir, "rbxManifest.txt");
                string manifestCsvPath = Path.Combine(stageDir, "rbxManifest.csv");
                var manifestHeaders = new string[2] { "File Name", "MD5 Signature" };

                convertToCSV(manifestPath, manifestHeaders, (csv) =>
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
                scanFastFlags,
                updateApiDump,
                extractShaderData,
                extractQtResources,
                extractStudioStrings
            };

            foreach (Action minerAction in minerActions)
            {
                Task minerTask = Task.Run(minerAction);
                taskPool.Add(minerTask);
            }

            await Task.WhenAll(taskPool);

            foreach (Task task in taskPool)
                if (task.Status == TaskStatus.Faulted)
                    throw task.Exception;
            
            taskPool.Clear();

            // Unpack and transfer specific content data from the studio build.
            print("Unpacking content data...", CYAN);
            
            var contentFolders = new Dictionary<string, string>
            {
                { "BuiltInPlugins", "BuiltInPlugins" },
                { "BuiltInStandalonePlugins", "BuiltInStandalonePlugins" },

                { "avatar", @"content\avatar" },
                { "scripts", @"content\scripts" },

                { "LuaPackages", @"content\LuaPackages" },
                { "translations", @"content\translations" }
            };

            foreach (string destPath in contentFolders.Keys)
            {
                string srcPath = contentFolders[destPath];

                Task unpack = Task.Run(() =>
                {
                    string srcFolder = Path.Combine(studioDir, srcPath);
                    string destFolder = resetDirectory(stageDir, destPath);

                    print($"\tCopying {srcFolder} to {destFolder}", CYAN);
                    copyDirectory(srcFolder, destFolder);

                    foreach (string file in Directory.GetFiles(destFolder, "*.*", SearchOption.AllDirectories))
                    {
                        if (file.EndsWith(".rbxm") || file.EndsWith(".rbxmx"))
                        {
                            print($"\t\tUnpacking {localPath(file)}", DARK_CYAN);
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
            print("Preparing commit...", CYAN);
        }

        static async Task MainAsync()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12 | SecurityProtocolType.Ssl3;

            string currentVersion = BranchRegistry.GetString("Version");
            string gitBinding = Path.Combine(stageDir, ".git");
            
            if (!Directory.Exists(gitBinding))
            {
                print($"Assembling stage for {branch}...", MAGENTA);
                var settings = Settings.Default;
                
                string repository = settings.RepoName;
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

                string name = settings.BotName;
                git("config", "--local", "user.name", '"' + name + '"');

                string email = settings.BotEmail;
                git("config", "--local", "user.email", email);

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

                        // Check in the files.
                        print("\tChecking in files...", YELLOW);
                        git("add .");

                        // Verify this update is worth committing.
                        var files = getChangedFiles(branch);
                        int updateCount = 0;
                        int boringCount = 0;

                        foreach (string file in files)
                        {
                            try
                            {
                                string filePath = stageDir + '\\' + file;
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
                            print("\tCommitting...", CYAN);
                            git($"commit -m {info.Version}");

                            print("\tPushing...", CYAN);
                            git($"push");

                            print("\tDone!", GREEN);
                        }
                        else
                        {
                            string skipMsg;

                            if (boringCount > 0)
                                skipMsg = "This update was deemed to be boring";
                            else
                                skipMsg = "No changes were detected";

                            print($"\t{skipMsg}, skipping commit!", RED);
                        }

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

            #endregion

            RootRegistry = Registry.CurrentUser.Open("Software", "RobloxClientTracker");
            BranchRegistry = RootRegistry.Open(branch);

            branch = branch.ToLower();
            trunk = createDirectory(@"C:\Roblox-Client-Tracker");
            stageDir = createDirectory(trunk, "stage", branch);

            studio = new StudioBootstrapper(branch);
            studioPath = studio.GetStudioPath();

            Task mainThread = Task.Run(() => MainAsync());
            mainThread.Wait();
        }
    }
}