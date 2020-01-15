using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RobloxClientTracker
{
    public class UnpackShaders : DataMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Green;

        private static FileLogConfig LogShader = new FileLogConfig()
        {
            Color = ConsoleColor.DarkGreen,
            Stack = 2
        };

        public void WriteShader(string path, string contents)
        {
            writeFile(path, contents, LogShader);
        }
        
        public override void ExecuteRoutine()
        {
            string studioDir = studio.GetStudioDirectory();
            string shaderDir = Path.Combine(studioDir, "shaders");

            var names = new List<string>();
            var shaders = new Dictionary<string, string>();
            var shaderPacks = new Dictionary<string, HashSet<string>>();

            string newShaderDir = createDirectory(stageDir, "shaders");
            print("Unpacking shader packs...");

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
                    string shader = file.Name;

                    shaders[shader] = shaderType;
                    myShaders[shader] = shaderType;

                    if (!shaderPacks.ContainsKey(shader))
                        shaderPacks.Add(shader, new HashSet<string>());

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
                    .Select(line => line.ToString())
                    .ToArray();

                string myManifest = string.Join("\r\n", myManifestLines);
                myManifest = CsvBuilder.Convert(myManifest, "Name", "Shader Type");

                string newShaderPathCsv = Path.Combine(newShaderDir, pack.Name + ".csv");
                writeFile(newShaderPathCsv, myManifest, LogShader);
            }

            var headers = new List<string>() { "Name", "Shader Type" };
            headers.AddRange(names);

            var shaderNames = shaders.Keys.ToList();
            shaderNames.Sort();

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
