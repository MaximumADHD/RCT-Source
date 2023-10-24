using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RobloxClientTracker
{
    public class ShaderDef
    {
        public string Name;
        public uint Mask;

        public override string ToString()
        {
            string bits = Convert
                .ToString(Mask, 16)
                .PadLeft(8, '0');

            return $"{Name}_{bits}";
        }
    }

    public struct ShaderFFlagMask
    {
        public int WhenDisabled;
        public int WhenEnabled;

        public override string ToString()
        {
            return $"[❌{WhenDisabled}][✔️{WhenEnabled}]";
        }
    }

    public class ShaderPack
    {
        public string Name { get; private set; }
        public uint Hash { get; private set; }

        public string Header { get; private set; }
        public ushort Version { get; private set; }

        public ushort NumGroups { get; private set; }
        public ushort NumShaders { get; private set; }
        public ushort NumUnknown { get; private set; }

        private readonly string[] GroupsImpl;
        private readonly ShaderFile[] ShadersImpl;

        public IReadOnlyList<string> Groups => GroupsImpl;
        public IReadOnlyList<ShaderFile> Shaders => ShadersImpl;

        public override string ToString()
        {
            return Name;
        }

        public ShaderPack(string filePath)
        {
            FileInfo info = new FileInfo(filePath);

            using (FileStream file = File.OpenRead(filePath))
            using (BinaryReader reader = new BinaryReader(file))
            {
                uint numNames = 0;
                uint numFFlags = 0;

                Header = reader.ReadString(4);
                Version = reader.ReadUInt16();

                if (Version >= 10)
                {
                    NumGroups = reader.ReadByte();
                    numFFlags = reader.ReadByte();
                }
                else
                {
                    NumGroups = reader.ReadUInt16();
                }

                NumShaders = reader.ReadUInt16();

                if (Version >= 9)
                {
                    numNames = NumShaders;
                    NumShaders = reader.ReadUInt16();
                }

                Name = info.Name.Replace(info.Extension, "");
                Hash = reader.ReadUInt32();

                GroupsImpl = new string[NumGroups];
                ShadersImpl = new ShaderFile[NumShaders];

                // Read Groups
                for (int i = 0; i < NumGroups; i++)
                    GroupsImpl[i] = reader.ReadString(64);

                // Read Names
                var names = new ShaderDef[numNames];
                var fflags = new string[numFFlags];

                for (int i = 0; i < numNames; i++)
                {
                    names[i] = new ShaderDef()
                    {
                        Name = reader.ReadString(64),
                        Mask = reader.ReadUInt32(),
                    };
                }

                for (int i = 0; i < numFFlags; i++)
                    fflags[i] = reader.ReadString(64);

                for (int i = 0; i < NumShaders; i++)
                {
                    string name = "";

                    if (Version < 9)
                        name = reader.ReadString(64);

                    byte[] rawHash = reader.ReadBytes(16);
                    string hash = Convert.ToBase64String(rawHash);

                    var shader = new ShaderFile
                    {
                        Hash = hash,
                        Offset = reader.ReadInt32(),
                        Size = reader.ReadInt32(),
                    };

                    if (Version > 6)
                        shader.Mask = reader.ReadInt32();

                    shader.ShaderType = (ShaderType)reader.ReadByte();
                    shader.Group = Groups[reader.ReadByte()];

                    if (Version >= 9)
                    {
                        ushort nameIndex = reader.ReadUInt16();
                        var nameInfo = names[nameIndex];
                        name = nameInfo.Name;
                    }
                    else
                    {
                        int skip = Version > 6 ? 2 : 6;
                        shader.Stub = reader.ReadBytes(skip);
                    }

                    var flagMasks = new Dictionary<string, ShaderFFlagMask>();
                    shader.Name = name;

                    if (Version > 9)
                    {
                        for (int j = 0; j < numFFlags; j++)
                        {
                            string flagName = fflags[j];

                            // best guess: this enables certain bit-flags on the
                            // shader when the specified FFlag is enabled/disabled?
                            // without knowing what the bit-flags represent, it's hard to tell.

                            flagMasks[flagName] = new ShaderFFlagMask
                            {
                                WhenEnabled = reader.ReadInt32(),
                                WhenDisabled = reader.ReadInt32(),
                            };
                        }
                    }

                    shader.FFlagMasks = flagMasks;
                    ShadersImpl[i] = shader;
                }

                // Unpack the shader files
                foreach (ShaderFile shader in Shaders)
                {
                    file.Position = shader.Offset;
                    shader.Buffer = reader.ReadBytes(shader.Size);
                }
            }
        }

        public HashSet<string> UnpackShader(UnpackShaders unpacker, string exportDir)
        {
            var shaderManifest = Program.state.ShaderManifest;
            string shaderKey = Name.Replace("shaders_", "");
            
            if (!shaderManifest.TryGetValue(shaderKey, out var shaderReg))
            {
                shaderReg = new Dictionary<string, string>();
                shaderManifest[shaderKey] = shaderReg;
            }

            string root = Groups[0];

            var hashes = new HashSet<string>();
            var names = new HashSet<string>();

            string unpackDir = Path.Combine(exportDir, Name);
            Directory.CreateDirectory(unpackDir);

            var rootShaders = Shaders.Where((shader) => shader.Group == root);
            var otherShaders = Shaders.Except(rootShaders);

            foreach (string dir in Directory.GetDirectories(unpackDir))
            {
                var info = new DirectoryInfo(dir);

                if (Groups.Contains(info.Name) && info.Name != root)
                    continue;

                Directory.Delete(dir, true);
            }

            foreach (ShaderFile rootShader in rootShaders)
            {
                hashes.Add(rootShader.Hash);
                names.Add(rootShader.RegistryKey);
                rootShader.WriteFile(unpacker, unpackDir, shaderReg);
            }

            foreach (ShaderFile otherShader in otherShaders)
            {
                string groupDir = Path.Combine(unpackDir, otherShader.Group);
                Directory.CreateDirectory(groupDir);

                if (!hashes.Contains(otherShader.Hash))
                {
                    hashes.Add(otherShader.Hash);
                    names.Add(otherShader.RegistryKey);
                    otherShader.WriteFile(unpacker, groupDir, shaderReg);
                }
            }

            string[] oldNames = shaderReg.Keys
                .Except(names)
                .ToArray();

            foreach (string oldName in oldNames)
            {
                string filePath = Path.Combine(unpackDir, oldName);

                if (File.Exists(filePath))
                    File.Delete(filePath);

                shaderReg.Remove(oldName);
            }

            if (!shaderReg.Any())
                shaderManifest.Remove(shaderKey);

            return hashes;
        }
    }
}
