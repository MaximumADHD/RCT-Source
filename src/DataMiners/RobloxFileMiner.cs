using System;
using System.IO;
using System.Linq;
using System.Diagnostics.Contracts;

using RobloxFiles;
using Microsoft.Win32;

namespace RobloxClientTracker
{
    /// <summary>
    /// An extension to the MultiTaskMiner class which provides
    /// utilities for unpacking RobloxFile objects and pulling
    /// useful data from Instances.
    /// </summary>
    public abstract class RobloxFileMiner : MultiTaskMiner
    {
        private static RegistryKey modelManifest => Program.BranchRegistry?.Open("ModelManifest");

        private static readonly FileLogConfig LogRbxm = new FileLogConfig()
        {
            Color = Program.DARK_CYAN,
            Stack = 3
        };

        protected static void copyDirectory(string source, string target)
        {
            string dest = createDirectory(target);
            var src = new DirectoryInfo(source);

            foreach (var file in src.GetFiles())
            {
                string destPath = Path.Combine(dest, file.Name);
                file.CopyTo(destPath, true);
            }

            foreach (var subDir in src.GetDirectories())
            {
                string subSrc = subDir.FullName;
                string subTarget = Path.Combine(dest, subDir.Name);
                copyDirectory(subSrc, subTarget);
            }
        }

        public static bool PullInstanceData(Instance inst, ref string value, ref string extension)
        {
            Contract.Requires(inst != null);

            if (inst.IsA<LuaSourceContainer>())
            {
                var luaFile = inst.Cast<LuaSourceContainer>();

                switch (luaFile.ClassName)
                {
                    case "Script":
                    {
                        extension = ".server.lua";
                        break;
                    }
                    case "LocalScript":
                    {
                        extension = ".client.lua";
                        break;
                    }
                    default:
                    {
                        extension = ".lua";
                        break;
                    }
                }

                value = luaFile.Source;
            }
            else if (inst.IsA<StringValue>() && inst.Name != "AvatarPartScaleType")
            {
                var str = inst.Cast<StringValue>();
                extension = ".txt";
                value = str.Value;
            }
            else if (inst.IsA<LocalizationTable>())
            {
                var table = inst.Cast<LocalizationTable>();
                var csvTable = new CsvLocalizationTable(table);

                extension = ".csv";
                value = csvTable.WriteCsv();
            }

            value = sanitizeString(value);
            return (value.Length > 0);
        }

        private void unpackImpl(Instance inst, string parentDir)
        {
            string name = inst.Name;

            string extension = "";
            string value = "";

            if (PullInstanceData(inst, ref value, ref extension))
            {
                string filePath = Path.Combine(parentDir, name + extension);
                writeFile(filePath, value, LogRbxm);
            }

            var children = inst
                .GetChildren()
                .ToList();

            if (children.Count > 0)
            {
                string instDir = createDirectory(parentDir, name);
                children.ForEach(child => unpackImpl(child, instDir));
            }
        }

        protected void unpackFile(string filePath, bool checkHash, bool delete = true)
        {
            FileInfo info = new FileInfo(filePath);

            if (info.Exists && (info.Extension == ".rbxm" || info.Extension == ".rbxmx"))
            {
                RobloxFile file = RobloxFile.Open(filePath);

                string projectName = info.Name.Replace(info.Extension, "");
                string regKey = info.Directory.Name + '/' + projectName;

                if (delete)
                    File.Delete(filePath);

                string newHash = "";
                
                if (checkHash)
                {
                    string currentHash = modelManifest.GetString(regKey);
                    newHash = ModelHasher.GetFileHash(file);

                    if (currentHash == newHash)
                    {
                        print($"\t\t\tNo changes to {regKey}. Skipping!", ConsoleColor.Red);
                        return;
                    }
                }

                Instance[] children = file.GetChildren();

                if (children.Length == 1)
                {
                    Instance project = children[0];
                    project.Name = projectName;
                    project.Parent = null;

                    unpackImpl(project, info.DirectoryName);

                    if (newHash.Length == 0)
                        return;

                    modelManifest.SetValue(regKey, newHash);
                }
            }
        }
    }
}
