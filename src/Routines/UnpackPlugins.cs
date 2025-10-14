using System;
using System.IO;

namespace RobloxClientTracker
{
    public class UnpackPlugins : RobloxFileMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Cyan;

        private static readonly string[] pluginFolders = new string[]
        {
            "BuiltInPlugins",
            "BuiltInStandalonePlugins"
        };

        public UnpackPlugins()
        {
            foreach (string folderName in pluginFolders)
            {
                string studioDir = studio.GetLocalStudioDirectory();

                string srcFolder = Path.Combine(studioDir, folderName, "Optimized_Embedded_Signature");
                string destFolder = Path.Combine(stageDir, folderName);

                print($"\tCopying {srcFolder} to {destFolder}");
                copyDirectory(srcFolder, destFolder);

                foreach (string file in Directory.GetFiles(destFolder))
                {
                    addRoutine(() =>
                    {
                        if (file.EndsWith(".rbxm", Program.InvariantString) || file.EndsWith(".rbxmx", Program.InvariantString))
                        {
                            print($"\t\tUnpacking {localPath(file)}");
                            unpackFile(file, true);
                            return;
                        }

                        File.Delete(file);
                    });
                }
            }
        }
    }
}
