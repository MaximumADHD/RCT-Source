using Newtonsoft.Json.Linq;
using RobloxClientTracker.Luau;
using RobloxFiles;
using RobloxFiles.BinaryFormat.Chunks;
using RobloxFiles.DataTypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        public struct PackageRecord
        {
            public string Hash;
            public string Name;
            public string Content;
            public string Version;
        }

        private static Dictionary<string, string> modelManifest => state.ModelManifest;
        public static ConcurrentDictionary<string, ConcurrentDictionary<string, PackageRecord>> Packages = new ConcurrentDictionary<string, ConcurrentDictionary<string, PackageRecord>>();

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

        public static void ResetPackages()
        {
            resetDirectory(stageDir, "CompiledPackages");
            Packages.Clear();
        }

        private static string getHash(string content)
        {
            byte[] raw = Encoding.UTF8.GetBytes(content);

            using (HashAlgorithm md5 = MD5.Create())
            {
                string hash = "";
                var rawHash = md5.ComputeHash(raw);

                foreach (byte b in rawHash)
                    hash += b.ToString("x2");

                return hash;
            }
        }

        private void recordPackage(string name, Instance package, string version = null, string registry = null)
        {
            var contentMap = new Dictionary<string, string>();
            package.Parent = null;

            if (!Packages.TryGetValue(name, out var variants))
            {
                var newVariants = new ConcurrentDictionary<string, PackageRecord>();
                Packages.TryAdd(name, newVariants);

                variants = Packages[name];
            }

            // early out
            if (version != null && variants.ContainsKey(version))
                return;

            foreach (var item in package.GetDescendants())
            {
                if (item is LuaSourceContainer lua)
                {
                    var path = item.GetFullName();
                    ProtectedString source = null;

                    if (lua is Script script)
                    {
                        path += (lua is LocalScript ? ".client" : ".server");
                        source = script.Source;
                    }
                    else if (lua is ModuleScript module)
                    {
                        source = module.Source;
                    }

                    if (source != null)
                    {
                        string contents;
                        path += ".lua";

                        try
                        {
                            if (source.IsCompiled)
                            {
                                var data = source.RawBuffer;
                                var disassembler = new LuauDisassembly(data);

                                contents = disassembler.BuildDisassembly();
                                path += "c";
                            }
                            else
                            {
                                contents = source.ToString();
                            }

                            contentMap.Add(path, contents);
                        }
                        catch (Exception e)
                        {
                            // TODO
                            print(e.Message, ConsoleColor.Red);
                        }
                    }
                }
            }

            var keys = contentMap.Keys.ToArray();
            Array.Sort(keys, string.CompareOrdinal);

            var contentBlob = new StringBuilder();
            
            foreach (var key in keys)
            {
                var value = contentMap[key];
                value = getHash(value);

                contentBlob.AppendLine($"[{key}]");
                contentBlob.AppendLine(value);
                contentBlob.AppendLine();
            }

            string content = contentBlob.ToString();
            string hash = getHash(content);

            // Unlikely to collide, but maybe once in a blue moon.
            string id = version ?? hash.Substring(0, 8);

            if (variants.ContainsKey(id))
                return;

            var compiledPackages = createDirectory(stageDir, "CompiledPackages");
            var packageDir = createDirectory(compiledPackages, name);
            
            try
            {
                var record = new PackageRecord()
                {
                    Name = name,
                    Hash = hash,
                    Version = id,
                    Content = content,
                };

                if (!variants.TryAdd(hash, record))
                    return;

                var children = package.GetChildren();

                if (children.Length == 1)
                {
                    package = children[0];
                    package.Parent = null;
                }

                package.Name = id;
                print($"Unpacking CompiledPackage {record.Name} ({record.Hash}) (Version: {record.Version})", ConsoleColor.Magenta);
                unpackImpl(package, packageDir, null);
            }
            catch
            {
                print(hash, ConsoleColor.Red);
            }
        }

        private void writeScript(string writePath, ProtectedString source)
        {
            var info = new FileInfo(writePath);
            byte[] buffer = source.RawBuffer;
            writeFile(writePath, buffer, LogRbxm);

            if (source.IsCompiled)
            {
                var rawSource = writePath.Replace(".luac", ".lua");

                if (File.Exists(rawSource))
                    return;

                try
                {
                    var disassembler = new LuauDisassembly(buffer);
                    string disassembly = disassembler.BuildDisassembly();
                    writeFile(writePath + ".s", disassembly, LogRbxm);
                }
                catch (Exception e)
                {
                    print("\t\t!!FIXME: Error writing disassembly for " + writePath + ": " + e.Message, ConsoleColor.Red);
                }
            }
            else
            {
                string src = Encoding.UTF8.GetString(buffer);
                writeFile(writePath, buffer, LogRbxm);

                var compiled = writePath + "c";
                var disassembled = compiled + ".s";

                if (File.Exists(compiled))
                    File.Delete(compiled);

                if (!File.Exists(disassembled))
                    return;

                File.Delete(disassembled);
            }
        }

        private void unpackImpl(Instance inst, string parentDir, Instance expectParent)
        {
            if (inst.Parent != expectParent)
                return;

            string name = inst.Name;
            var parent = inst.Parent;

            var children = inst
                .GetChildren()
                .ToList();

            if (name.ToLowerInvariant() == "packages")
            {
                var index = inst.FindFirstChild("_Index");
                var dev = inst.FindFirstChild("Dev");

                //-----------------------------------------------------------------------
                // Each package folder is setup as such:
                // Packages [Folder]
                //   _Index [Folder]
                //     TheModule-MaybeHash-MaybeVersion [Folder]
                //       TheModule [ModuleScript] (REAL MODULE)
                //       Dependency1 [ModuleScript] (LINK TO _INDEX DEPENDENCY)
                //       Dependency2 [ModuleScript] (LINK TO _INDEX DEPENDENCY)
                //
                //   Dev [Folder]
                //     DevModule [ModuleScript] (LINK TO _INDEX DEPENDENCY)
                //
                //   TheModule [ModuleScript] (LINK TO "REAL MODULE" _INDEX DEPENDENCY)
                //-----------------------------------------------------------------------

                // What we want to do is:
                // * Grab all of the packages that have been indexed.
                // * Hash the contents of those packages in a deterministic way.
                // * Index all of these hashes into named buckets.

                if (index != null)
                {
                    foreach (var pkgIndex in index.GetChildren())
                    {
                        string packageId = pkgIndex.Name;

                        if (!packageId.Contains("@"))
                        {
                            // Rotriever package
                            var schema = packageId.Split('-');
                            var pkgName = schema[0];

                            var pkgVersion = schema.Length > 2 ? schema[2] : null;
                            var pkgRegistry = schema.Length > 2 ? schema[1] : null;

                            var pkgInst = pkgIndex.FindFirstChild(pkgName);
                            recordPackage(pkgName, pkgIndex, pkgVersion, pkgRegistry);
                        }
                        else
                        {
                            // Wally package
                            var underscore = packageId.IndexOf("_");
                            var atSign = packageId.IndexOf("@");

                            if (underscore >= 0 && atSign >= 0)
                            {
                                var pkgName = packageId.Substring(underscore + 1, atSign - 1);
                                var version = packageId.Substring(atSign + 1);
                                recordPackage(pkgName, pkgIndex, version);
                            }
                        }
                    }

                    index.Parent = null;
                }
            }

            string instDir = resetDirectory(parentDir, name);
            string extension = "";
            string value = "";

            if (children.Count > 0)
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
