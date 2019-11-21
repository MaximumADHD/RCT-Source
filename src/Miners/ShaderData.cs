using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RobloxClientTracker
{
    public static class ShaderData
    {
        public static FileLogConfig LogShader = new FileLogConfig()
        {
            Color = Program.DARK_GREEN,
            Stack = 2
        };

        private static void print(string msg)
        {
            Program.print(msg, Program.GREEN);
        }

        public static void Extract()
        {
            string stageDir = Program.StageDir;
            var studio = Program.Studio;

            string studioDir = studio.GetStudioDirectory();
            string shaderDir = Path.Combine(studioDir, "shaders");

            var packNames = new List<string>();

            var shaders = new Dictionary<string, string>();
            var shaderPacks = new Dictionary<string, HashSet<string>>();

            string newShaderDir = Program.CreateDirectory(stageDir, "shaders");
            print("Unpacking shader packs...");

            foreach (string shaderPath in Directory.GetFiles(shaderDir))
            {
                ShaderPack pack = new ShaderPack(shaderPath);
                var myShaders = new Dictionary<string, string>();

                string packName = pack.Name.Replace("shaders_", "");
                packNames.Add(packName);

                List<ShaderFile> shaderFiles = pack.Shaders.ToList();
                shaderFiles.Sort();

                print($"\tUnpacking shader file {packName}...");
                HashSet<string> hashes = pack.UnpackShader(newShaderDir, LogShader);

                foreach (ShaderFile file in shaderFiles)
                {
                    string shaderType = Enum.GetName(typeof(ShaderType), file.ShaderType);
                    string shader = file.Name;

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
                myManifest = CsvBuilder.Convert(myManifest, "Name", "Shader Type");

                string newShaderPathCsv = Path.Combine(newShaderDir, pack.Name + ".csv");
                Program.WriteFile(newShaderPathCsv, myManifest, LogShader);
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
            manifest = CsvBuilder.Convert(manifest, headers.ToArray());

            string manifestPath = Path.Combine(stageDir, "RobloxShaderData.csv");
            Program.WriteFile(manifestPath, manifest, LogShader);

            print("Shaders unpacked!");
        }
    }
}
