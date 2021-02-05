using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RobloxClientTracker
{
    public class UnpackShaders : DataMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Green;
        private readonly HashSet<string> writtenHeaders = new HashSet<string>();

        public string IncludeDir { get; private set; }

        private static FileLogConfig LogShader = new FileLogConfig()
        {
            Color = ConsoleColor.DarkGreen,
            Stack = 2
        };

        public static string ResetDirectory(params string[] traversal) => resetDirectory(traversal);
        public void WriteShader(string path, string contents) => writeFile(path, contents, LogShader);

        public bool HasHeaderFile(string header) => writtenHeaders.Contains(header);
        public bool AddHeaderFile(string header) => writtenHeaders.Add(header);

        public override void ExecuteRoutine()
        {
            string studioDir = studio.GetLocalStudioDirectory();
            string shaderDir = Path.Combine(studioDir, "shaders");

            var names = new List<string>();
            var shaders = new Dictionary<string, string>();

            var shaderPacks = new Dictionary<string, HashSet<string>>();
            var fileLookup = new Dictionary<string, ShaderFile>();

            string newShaderDir = createDirectory(stageDir, "shaders");
            IncludeDir = createDirectory(newShaderDir, "include");

            print("Unpacking shader packs...");
            writtenHeaders.Clear();

            foreach (string shaderPath in Directory.GetFiles(shaderDir))
            {
                FileInfo info = new FileInfo(shaderPath);

                if (info.Extension != ".pack")
                    continue;

                ShaderPack pack = new ShaderPack(shaderPath);
                var myShaders = new Dictionary<string, string>();

                string name = pack.Name.Replace("shaders_", "");
                names.Add(name);

                List<ShaderFile> shaderFiles = pack.Shaders.ToList();
                shaderFiles.Sort();

                print($"\tUnpacking shader file {name}...");
                HashSet<string> hashes = pack.UnpackShader(this, newShaderDir);

                foreach (ShaderFile file in shaderFiles)
                {
                    string shaderType = Enum.GetName(typeof(ShaderType), file.ShaderType);
                    string shader = file.Id;

                    if (shaderType == null)
                    {
                        shaderType = "Unknown";
                        print($"Shader '{shader}' has an unknown shader type! (Id: {(char)file.ShaderType})", ConsoleColor.Red);
                    }
                    
                    shaders[shader] = shaderType;
                    myShaders[shader] = shaderType;

                    if (!shaderPacks.ContainsKey(shader))
                        shaderPacks.Add(shader, new HashSet<string>());

                    if (!fileLookup.ContainsKey(shader))
                        fileLookup.Add(shader, file);

                    shaderPacks[shader].Add(name);
                }

                var myLines = new List<string>();

                foreach (string shader in myShaders.Keys)
                {
                    string type = myShaders[shader];
                    myLines.Add(shader);
                    myLines.Add(type);
                }

                string[] myManifestLines = myLines
                    .Select(line => line.ToString(CultureInfo.InvariantCulture))
                    .ToArray();

                string myManifest = string.Join("\r\n", myManifestLines);
                myManifest = CsvBuilder.Convert(myManifest, "Name", "Shader Type");

                string newShaderPathCsv = Path.Combine(newShaderDir, pack.Name + ".csv");
                writeFile(newShaderPathCsv, myManifest, LogShader);
            }

            var headers = new List<string>() { "Name", "Shader Type" };
            headers.AddRange(names);

            var shaderSort = new Comparison<string>((a, b) =>
            {
                try
                {
                    ShaderFile fileA = fileLookup[a];
                    ShaderFile fileB = fileLookup[b];

                    return fileA.CompareTo(fileB);
                }
                catch
                {
                    return string.Compare(a, b, Program.InvariantString);
                }
            });

            var shaderNames = shaders.Keys.ToList();
            shaderNames.Sort(shaderSort);

            var lines = new List<string>();

            foreach (string shader in shaderNames)
            {
                string type = shaders[shader];
                var packs = shaderPacks[shader];

                lines.Add(shader);
                lines.Add(type);

                foreach (string name in names)
                {
                    string check = packs.Contains(name) ? "✔" : "❌";
                    lines.Add(check);
                }
            }

            string manifest = string.Join("\r\n", lines);
            manifest = CsvBuilder.Convert(manifest, headers);

            string manifestPath = Path.Combine(stageDir, "RobloxShaderData.csv");
            writeFile(manifestPath, manifest, LogShader);

            print("Shaders unpacked!");
        }
    }
}
