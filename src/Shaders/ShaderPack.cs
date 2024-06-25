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
        public ushort NumNames { get; private set; }
        public ushort NumFFlags { get; private set; }
        public ushort NumShaders { get; private set; }
        public ushort NumBitNames { get; private set; }

        private readonly string[] GroupsImpl;
        private readonly ShaderFile[][] ShadersImpl;
        private readonly string[] BitNames;

        public IReadOnlyList<string> Groups => GroupsImpl;

        public IReadOnlyList<ShaderFile> Shaders => ShadersImpl
            .SelectMany(groups => groups)
            .ToList();

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
                Header = reader.ReadString(4);
                Version = reader.ReadUInt16();

                NumGroups = reader.ReadUInt16();
                NumFFlags = reader.ReadUInt16();
                NumBitNames = reader.ReadUInt16();

                NumNames = reader.ReadUInt16();
                NumShaders = reader.ReadUInt16();

                Name = info.Name.Replace(info.Extension, "");
                Hash = reader.ReadUInt32();

                GroupsImpl = new string[NumGroups];
                ShadersImpl = new ShaderFile[NumGroups][];

                // Read Groups
                for (int i = 0; i < NumGroups; i++)
                    GroupsImpl[i] = reader.ReadString(64);

                // Read Names
                var names = new ShaderDef[NumNames];
                var fflags = new string[NumFFlags];
                var bitNames = new string[NumBitNames];

                for (int i = 0; i < NumNames; i++)
                {
                    names[i] = new ShaderDef()
                    {
                        Name = reader.ReadString(64),
                        Mask = reader.ReadUInt32(),
                    };
                }

                for (int i = 0; i < NumFFlags; i++)
                    fflags[i] = reader.ReadString(64);

                for (int i = 0; i < NumBitNames; i++)
                {
                    var name = reader.ReadString(64);
                    var index = reader.ReadByte();
                    bitNames[index] = name;
                }

                for (int i = 0; i < NumGroups; i++)
                {
                    var group = new ShaderFile[NumNames];

                    for (int j = 0; j < NumNames; j++)
                    {
                        byte[] rawHash = reader.ReadBytes(16);
                        string hash = Convert.ToBase64String(rawHash);

                        var shader = new ShaderFile
                        {
                            Hash = hash,
                            Offset = reader.ReadInt32(),
                            Size = reader.ReadInt32(),
                        };

                        shader.Mask = reader.ReadInt32();
                        shader.ShaderType = (ShaderType)reader.ReadByte();
                        shader.Group = Groups[reader.ReadByte()];

                        ushort nameIndex = reader.ReadUInt16();
                        var nameInfo = names[nameIndex];

                        byte[] stub = reader.ReadBytes(32);
                        shader.Name = nameInfo.Name;

                        group[j] = shader;
                    }

                    ShadersImpl[i] = group;
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
