using System;
using System.IO;

namespace RobloxClientTracker
{
    public class CopyContentFolders : RobloxFileMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Cyan;

        private string[] contentFolders = new string[]
        {
            "avatar",
            "scripts",
            "LuaPackages",
            "translations"
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
            string destFolder = resetDirectory(stageDir, folderName);

            print($"Copying {srcFolder} to {destFolder}");
            copyDirectory(srcFolder, destFolder);

            foreach (string file in Directory.GetFiles(destFolder, "*.*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".rbxm") || file.EndsWith(".rbxmx"))
                {
                    print($"\t\tUnpacking {localPath(file)}");
                    unpackFile(file, false);
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
                        writeFile(file, newSource);
                    }
                }
            }
        }
    }
}
