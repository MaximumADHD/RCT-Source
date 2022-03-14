using System;
using System.IO;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RobloxClientTracker
{
    public class CopyContentFolders : RobloxFileMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Cyan;

        private readonly string[] contentFolders = new string[]
        {
            "avatar",
            "configs",
            "scripts",
            "api_docs",
            "LuaPackages",
            "translations",
            "textures"
        };

        public CopyContentFolders()
        {
            foreach (string folder in contentFolders)
            {
                var copy = new Action(() => copyContentFolder(folder));
                addRoutine(copy);
            }
        }

        private void copyContentFolder(string folderName)
        {
            string srcFolder = Path.Combine(studioDir, "content", folderName);
            string destFolder = resetDirectory(stageDir, folderName.Replace('_', '-'));

            print($"Copying {srcFolder} to {destFolder}");
            copyDirectory(srcFolder, destFolder);

            foreach (string file in Directory.GetFiles(destFolder, "*.*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".rbxm", Program.InvariantString) || file.EndsWith(".rbxmx", Program.InvariantString))
                {
                    print($"\t\tUnpacking {localPath(file)}");
                    unpackFile(file, false);
                }
                else if (file.EndsWith(".sig", Program.InvariantString) || file.EndsWith(".mesh", Program.InvariantString))
                {
                    File.Delete(file);
                }
                else if (file.EndsWith(".lua", Program.InvariantString))
                {
                    string source = File.ReadAllText(file);
                    string newSource = sanitizeString(source);

                    if (source == newSource)
                        continue;

                    writeFile(file, newSource);
                }
                else if (file.EndsWith(".json", Program.InvariantString))
                {
                    string rawJson = File.ReadAllText(file);

                    using (var reader = new StringReader(rawJson))
                    using (var jsonReader = new JsonTextReader(reader))
                    {
                        var json = JObject.Load(jsonReader);

                        string minified = json.ToString(Formatting.None);
                        string indented = json.ToString(Formatting.Indented);

                        var info = new FileInfo(file);
                        string dir = info.DirectoryName;

                        string minDir = createDirectory(dir, "mini");
                        string minFile = Path.Combine(minDir, info.Name);

                        writeFile(file, indented);
                        writeFile(minFile, minified);
                    }
                }
            }
        }
    }
}
