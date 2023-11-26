using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics.Contracts;

using RobloxFiles;
using System.Diagnostics;
using RobloxFiles.DataTypes;
using RobloxClientTracker.Luau;
#pragma warning disable IDE1006 // Naming Styles

namespace RobloxClientTracker
{
    /// <summary>
    /// An extension to the MultiTaskMiner class which provides
    /// utilities for unpacking RobloxFile objects and pulling
    /// useful data from Instances.
    /// </summary>
    public abstract class RobloxFileMiner : MultiTaskMiner
    {
        private static Dictionary<string, string> modelManifest => state.ModelManifest;
        private static Dictionary<string, Dictionary<Instance, Instance>> packages = new Dictionary<string, Dictionary<Instance, Instance>>();

        private static readonly FileLogConfig LogRbxm = new FileLogConfig()
        {
            Color = Program.DARK_CYAN,
            Stack = 3
        };

        // absolute last resort for stupid inconsistent package locations lol.

        private static readonly List<string> KnownPackages = new List<string>()
        {
            "Cryo",
            "Roact",
            "Rodux",
            "TestEZ",
            "UILibrary",
            "RoactRodux",
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
            bool canSanitize = true;
            Contract.Requires(inst != null);

            if (inst is LuaSourceContainer luaFile)
            {
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

                // why are these properties separate again?
                ProtectedString source = null;

                if (luaFile is Script script)
                    source = script.Source;
                else if (luaFile is ModuleScript moduleScript)
                    source = moduleScript.Source;
                
                if (source != null && source.IsCompiled)
                {
                    extension += "c";
                    canSanitize = false;
                }

                value = source;
            }
            else if (inst is StringValue str && inst.Name != "AvatarPartScaleType")
            {
                if (inst.Name.StartsWith("."))
                    extension = "";
                else
                    extension = ".txt";

                value = str.Value;
            }
            else if (inst is LocalizationTable table)
            {
                var csvTable = new CsvLocalizationTable(table);
                value = csvTable.WriteCsv();
                extension = ".csv";
            }

            if (canSanitize)
                value = sanitizeString(value);

            return (value.Length > 0);
        }

        private void recordPackage(Instance inst)
        {
            var parent = inst.Parent;
            inst.Parent = null;

            if (inst.Name.StartsWith("_"))
                return;

            if (!packages.TryGetValue(inst.Name, out var list))
            {
                list = new Dictionary<Instance, Instance>();
                packages.Add(inst.Name, list);
            }

            list.Add(inst, parent);
        }

        private void writeScript(string writePath, ProtectedString source)
        {
            byte[] buffer = source.RawBuffer;
            writeFile(writePath, buffer, LogRbxm);

            if (source.IsCompiled)
            {
                var disassembler = new LuauDisassembler(buffer);
                string disassembly = disassembler.BuildDisassembly();
                writeFile(writePath + ".s", disassembly, LogRbxm);
            }
        }

        private void unpackImpl(Instance inst, string parentDir, Instance expectParent)
        {
            if (inst.Parent != expectParent)
                return;

            string name = inst.Name;
            var packageBin = inst.Parent;

            var children = inst
                .GetChildren()
                .ToList();

            if (name == ".robloxrc")
            {
                // Definitely rotriever package.
                recordPackage(packageBin);
                return;
            }
            else if (KnownPackages.Contains(name) && children.Count > 0)
            {
                recordPackage(inst);
                return;
            }
            else if (name.StartsWith("_"))
            {
                // Probably a package managed by Rotriever.
                if (packageBin != null)
                {
                    var set = packageBin.GetChildren();

                    foreach (var child in set)
                    {
                        // Ignore _Index
                        if (child == inst)
                            continue;

                        // Ignore test bindings
                        if (child.Name == "Dev")
                            continue;

                        // Ignore package links.
                        if (!child.GetChildren().Any())
                            continue;

                        // This isn't a rotriever package, but it's probably reused.
                        children.Remove(child);
                        recordPackage(child);
                    }
                }

                foreach (var child in children)
                {
                    var packageName = child.Name
                        .Split('_')
                        .Last();
                    
                    LuaSourceContainer package = null;
                    var target = child;

                    if (name == "_IndexLegacy" || name == "_Legacy")
                        target = child.FindFirstChild("Packages");

                    if (target == null)
                        continue;
                    
                    foreach (var module in target.GetChildren())
                    {
                        if (!packageName.StartsWith(module.Name))
                            continue;

                        if (module is LuaSourceContainer luaModule)
                        {
                            package = luaModule;
                            break;
                        }
                    }

                    if (package == null)
                        continue;

                    recordPackage(child);
                }

                children.Clear();
            }

            string instDir = resetDirectory(parentDir, name);
            var indexHack = inst.FindFirstChild("_Index");

            string extension = "";
            string value = "";

            if (indexHack != null)
            {
                children.Remove(indexHack);
                children.Insert(0, indexHack);
            }

            if (inst.Parent != packageBin)
                return;
            else if (children.Count > 0)
                children.ForEach(child => unpackImpl(child, instDir, inst));
            else if (Directory.Exists(instDir))
                Directory.Delete(instDir);

            if (PullInstanceData(inst, ref value, ref extension) && inst is LuaSourceContainer lua)
            {
                ProtectedString bin = null;

                if (lua is Script script)
                    bin = script.Source;
                else if (lua is ModuleScript moduleScript)
                    bin = moduleScript.Source;

                string filePath;

                if (Directory.Exists(instDir))
                    filePath = Path.Combine(instDir, "init" + extension);
                else
                    filePath = instDir + extension;

                writeScript(filePath, bin);
            }
        }

        protected void unpackFile(string filePath, bool checkHash, bool delete = true)
        {
            var info = new FileInfo(filePath);

            if (info.Exists && (info.Extension == ".rbxm" || info.Extension == ".rbxmx"))
            {
                RobloxFile file = RobloxFile.Open(filePath);

                string projectName = info.Name.Replace(info.Extension, "");
                string regKey = info.Directory.Name + '/' + projectName;

                if (delete)
                    File.Delete(filePath);

                string newHash = "";
                modelManifest.TryGetValue(regKey, out string currentHash);

                if (checkHash)
                {
                    newHash = ModelHasher.GetFileHash(file);

                    if (currentHash == newHash && !Program.FORCE_PACKAGE_ANALYSIS)
                    {
                        print($"\t\t\tNo changes to {regKey}. Skipping!", ConsoleColor.Red);
                        return;
                    }
                }

                var children = file.GetChildren();

                if (children.Length == 1)
                {
                    Instance project = children[0];
                    project.Name = projectName;
                    project.Parent = null;

                    unpackImpl(project, info.DirectoryName, null);

                    if (newHash.Length == 0)
                        return;

                    modelManifest[regKey] = newHash;
                }
            }
        }
    }
}
