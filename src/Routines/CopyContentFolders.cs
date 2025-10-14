using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#pragma warning disable IDE1006 // Naming Styles

namespace RobloxClientTracker
{
    public class CopyContentFolders : RobloxFileMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Cyan;

        private readonly string[] contentFolders = new string[]
        {
            @"content\avatar",
            @"content\configs",
            @"content\scripts",
            @"content\api_docs",
            @"content\textures",
            @"content\studio_svg_textures",

            @"ExtraContent\LuaPackages",

            // !! TODO: Add this when I figure out what to do with DataModelPatch & ServerCoreScripts
            // "ExtraContent/models",
            
            @"ExtraContent\scripts",
            @"ExtraContent\textures",
            @"ExtraContent\translations",

            @"StudioContent\textures",
        };

        public CopyContentFolders()
        {
            foreach (string folder in contentFolders)
            {
                var path = folder.Split('\\');
                var dest = path.Last();

                var destFolder = Path.Combine(stageDir, dest.Replace('_', '-'));
                resetDirectory(destFolder);
            }

            foreach (string folder in contentFolders)
            {
                var copy = new Action(() => copyContentFolder(folder));
                addRoutine(copy);
            }
        }

        private void copyContentFolder(string folderName)
        {
            var path = folderName.Split('\\');
            var dest = path.Last();

            string srcFolder = Path.Combine(studioDir, folderName);
            string destFolder = createDirectory(stageDir, dest.Replace('_', '-'));

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
