using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

using RobloxClientTracker.Properties;
#pragma warning disable IDE1006 // Naming Styles

namespace RobloxClientTracker
{
    public class ExtractQtResources : DataMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Magenta;

        private static readonly IReadOnlyDictionary<string, byte[]> luaFiles = new Dictionary<string, byte[]>
        {
            { "BinaryReader.lua",  Resources.BinaryReader_lua },
            { "QtExtract.lua",     Resources.QtExtract_lua    },
            { "PEParser.lua",      Resources.PEParser_lua     },
            { "Deflate.lua",       Resources.Deflate_lua      },
            { "Bit.lua",           Resources.Bit_lua          },
        };

        private static string shortenPath(params string[] traversal)
        {
            string path = Path.Combine(traversal);

            if (path.StartsWith(@"\\?\"))
                path = path.Substring(4);

            return path;
        }

        private void deployLuaJit(string dir)
        {
            resetDirectory(dir);

            using (var stream = new MemoryStream(Resources.LuaJIT_zip))
            using (var luaJit = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in luaJit.Entries)
                {
                    string fullPath = Path.Combine(dir, entry.FullName);

                    if (fullPath.EndsWith("/", Program.InvariantString))
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

        public override void ExecuteRoutine()
        {
            string extractDir = resetDirectory(stageDir, "QtResources");
            string luaDir = shortenPath(trunk, "lua");

            string luaJit = Path.Combine(luaDir, "luajit.cmd");
            string qtExtract = Path.Combine(luaDir, "QtExtract.lua");

            if (!File.Exists(luaJit) || !File.Exists(qtExtract))
            {
                print("Deploying LuaJIT...");
                deployLuaJit(luaDir);
            }

            string rawStudioPath = shortenPath(studioPath);
            extractDir = shortenPath(extractDir);
            
            var extract = new ProcessStartInfo()
            {
                FileName = luaJit,
                Arguments = $"{qtExtract} {rawStudioPath} --chunk 1 --output {extractDir}",

                CreateNoWindow = true,
                UseShellExecute = false
            };

            print("Extracting Qt Resources...");

            using (Process process = Process.Start(extract))
            {
                process.WaitForExit();
                process.Close();
            }

            foreach (string file in Directory.GetFiles(extractDir, "*.xml", SearchOption.AllDirectories))
            {
                FileInfo info = new FileInfo(file);
                string newPath = Path.Combine(stageDir, info.Name);

                if (File.Exists(newPath))
                    File.Delete(newPath);

                File.Move(file, newPath);
            }
        }
    }
}
