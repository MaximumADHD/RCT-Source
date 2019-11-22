using System.IO;
using System.Linq;

using RobloxFiles;
using Microsoft.Win32;

namespace RobloxClientTracker
{
    public static class ModelUnpacker
    {
        private static RegistryKey root => Program.BranchRegistry?.Open("ModelManifest");

        private static readonly FileLogConfig LogRbxm = new FileLogConfig()
        {
            Color = Program.DARK_CYAN,
            Stack = 3
        };

        public static bool GetInstanceInfo(Instance inst, ref string value, ref string extension)
        {
            string name = inst.Name;

            if (inst.IsA<Folder>() && name == "Packages")
                return false;

            if (inst.IsA<LuaSourceContainer>())
            {
                var luaFile = inst.Cast<LuaSourceContainer>();

                switch (luaFile.ClassName)
                {
                    case "Script":
                        extension = ".server.lua";
                        break;
                    case "LocalScript":
                        extension = ".client.lua";
                        break;
                    default:
                        extension = ".lua";
                        break;
                    //
                }

                value = luaFile.Source;
            }
            else if (inst.IsA<StringValue>() && inst.Name != "AvatarPartScaleType")
            {
                var str = inst.Cast<StringValue>();
                value = str.Value;
                extension = ".txt";
            }
            else if (inst.IsA<LocalizationTable>())
            {
                var table = inst.Cast<LocalizationTable>();
                var csvTable = new CsvLocalizationTable(table);

                extension = ".csv";
                value = csvTable.WriteCsv();
            }

            value = Program.SanitizeString(value);
            return (value.Length > 0);
        }

        private static void UnpackImpl(Instance inst, string parentDir)
        {
            string name = inst.Name;

            string extension = "";
            string value = "";

            if (inst.IsA<Folder>() && inst.Name == "Packages")
                return;

            if (GetInstanceInfo(inst, ref value, ref extension))
            {
                string filePath = Path.Combine(parentDir, name + extension);
                Program.WriteFile(filePath, value, LogRbxm);
            }

            var children = inst
                .GetChildren()
                .ToList();

            if (children.Count > 0)
            {
                string instDir = Program.CreateDirectory(parentDir, name);
                children.ForEach(child => UnpackImpl(child, instDir));
            }
        }

        public static void UnpackFile(string filePath, bool checkHash, bool delete = true)
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
                    string currentHash = root.GetString(regKey);
                    newHash = ModelHasher.GetFileHash(file);

                    if (currentHash == newHash)
                    {
                        Program.print($"\t\t\tNo changes to {regKey}. Skipping!", Program.RED);
                        return;
                    }
                }

                string projectDir = Program.ResetDirectory(info.DirectoryName, projectName);
                Instance[] children = file.GetChildren();

                if (children.Length == 1)
                {
                    Instance project = children[0];
                    project.Name = projectName;
                    project.Parent = null;

                    UnpackImpl(project, info.DirectoryName);

                    if (newHash.Length > 0)
                    {
                        root.SetValue(regKey, newHash);
                    }
                }
            }
        }
    }
}
