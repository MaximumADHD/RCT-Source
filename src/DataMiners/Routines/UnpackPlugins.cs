using System;
using System.IO;

namespace RobloxClientTracker
{
    public class UnpackPlugins : RobloxFileMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Cyan;

        private static string[] pluginFolders = new string[]
        {
            "BuiltInPlugins",
            "BuiltInStandalonePlugins"
        };

        public UnpackPlugins()
        {
            foreach (string folder in pluginFolders)
            {
                var unpack = new Action(() => unpackPlugin(folder));
                addRoutine(unpack);
            }
        }

        private void unpackPlugin(string folderName)
        {
            string studioDir = studio.GetStudioDirectory();

            string srcFolder = Path.Combine(studioDir, folderName);
            string destFolder = Path.Combine(stageDir, folderName);

            print($"\tCopying {srcFolder} to {destFolder}");
            copyDirectory(srcFolder, destFolder);

            foreach (string file in Directory.GetFiles(destFolder))
            {
                if (file.EndsWith(".rbxm") || file.EndsWith(".rbxmx"))
                {
                    print($"\t\tUnpacking {localPath(file)}");
                    unpackFile(file, true);

                    continue;
                }

                File.Delete(file);
            }

            foreach (string folder in Directory.GetDirectories(destFolder))
            {
                var info = new DirectoryInfo(folder);
                string file = Path.Combine(srcFolder, info.Name + ".rbxm");

                if (File.Exists(file))
                    continue;

                Directory.Delete(folder, true);
            }
        }
    }
}
