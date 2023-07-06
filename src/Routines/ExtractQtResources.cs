using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using RobloxClientTracker.Properties;
#pragma warning disable IDE1006 // Naming Styles

namespace RobloxClientTracker
{
    public class ExtractQtResources : DataMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Magenta;

        private static string shortenPath(params string[] traversal)
        {
            string path = Path.Combine(traversal);

            if (path.StartsWith(@"\\?\"))
                path = path.Substring(4);

            return path;
        }

        public override void ExecuteRoutine()
        {
            string qtExtract = createDirectory(trunk, "qtextract");
            string gitBinding = Path.Combine(qtExtract, ".git");

            if (!Directory.Exists(gitBinding))
                cmd(trunk, "git", "clone https://github.com/axstin/qtextract.git");

            // Check if rust needs to update.
            print("Updating rust...");
            cmd(qtExtract, "rustup", "update -q");

            // Build qtextract.
            print("Building qtextract...");
            cmd(qtExtract, "cargo", "build -q");

            // Get arguments ready.
            string extractDir = resetDirectory(stageDir, "QtResources");
            string outputDir = resetDirectory(studioDir, "qtextract");

            string rawStudioPath = shortenPath(studioPath);
            extractDir = shortenPath(extractDir);

            // Run it!
            print("Extracting Qt Resources...");
            cmd(qtExtract, "cargo", $"-q run {rawStudioPath} --chunk 0 --output {outputDir}");

            foreach (string folder in Directory.GetDirectories(outputDir))
            {
                // First layer is an index.
                var info = new DirectoryInfo(folder);

                if (int.TryParse(info.Name, out int index))
                {
                    // Second layer is the name of this chunk.
                    var rootDir = Directory
                        .GetDirectories(folder)
                        .First();

                    foreach (var file in Directory.GetFiles(rootDir, "*.*", SearchOption.AllDirectories))
                    {
                        string localPath = file.Replace(rootDir, "");
                        var split = localPath.Split('\\');

                        if (split.Length == 2)
                            // TEMPORARY HACK, probably should just be generalized?
                            localPath = "\\RobloxStyle" + localPath;

                        string reroute = extractDir + localPath;
                        var fileInfo = new FileInfo(reroute);

                        string dir = fileInfo.DirectoryName;
                        createDirectory(dir);

                        if (File.Exists(reroute))
                            File.Delete(reroute);

                        File.Move(file, reroute);
                    }
                }
            }
        }
    }
}
