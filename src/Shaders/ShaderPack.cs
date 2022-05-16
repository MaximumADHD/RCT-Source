using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RobloxClientTracker
{
    public class ShaderPack
    {
        public string Name { get; private set; }
        public int Hash { get; private set; }

        public string Header { get; private set; }
        public int Version { get; private set; }

        public short NumGroups { get; private set; }
        public short NumShaders { get; private set; }

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
                Header = reader.ReadString(4);
                Version = reader.ReadInt32();

                NumGroups = reader.ReadInt16();
                NumShaders = reader.ReadInt16();

                Name = info.Name.Replace(info.Extension, "");
                Hash = reader.ReadInt32();

                GroupsImpl = new string[NumGroups];
                ShadersImpl = new ShaderFile[NumShaders];

                // Read Groups
                for (int i = 0; i < NumGroups; i++)
                    GroupsImpl[i] = reader.ReadString(64);

                // Read Shaders
                for (int i = 0; i < NumShaders; i++)
                {
                    string name = reader.ReadString(64);

                    byte[] rawHash = reader.ReadBytes(16);
                    string hash = Convert.ToBase64String(rawHash);

                    var shader = new ShaderFile
                    {
                        Name = name,
                        Hash = hash,

                        Offset = reader.ReadInt32(),
                        Size = reader.ReadInt32(),
                    };

                    if (Version > 6)
                        shader.Level = reader.ReadInt32();

                    shader.ShaderType = (ShaderType)reader.ReadByte();
                    shader.Group = Groups[reader.ReadByte()];

                    if (Version > 6)
                        shader.Stub = reader.ReadBytes(2);
                    else
                        shader.Stub = reader.ReadBytes(6);

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
