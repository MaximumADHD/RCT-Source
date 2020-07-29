using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RobloxClientTracker
{
    public class ShaderPack
    {
        public string Name;
        public int Hash;

        public string Header;
        public int Version;

        public short NumGroups;
        public short NumShaders;

        public string[] Groups;
        public ShaderFile[] Shaders;

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

                Groups = new string[NumGroups];
                Shaders = new ShaderFile[NumShaders];

                // Read Groups
                for (int i = 0; i < NumGroups; i++)
                    Groups[i] = reader.ReadString(64);

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

                    Shaders[i] = shader;
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
            var shaderManifest = Program.BranchRegistry.Open("ShaderManifest");
            string shaderKey = Name.Replace("shaders_", "");
            
            var shaderReg = shaderManifest.Open(shaderKey);
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

            string[] oldNames = shaderReg.GetValueNames()
                .Except(names)
                .ToArray();

            foreach (string oldName in oldNames)
            {
                string filePath = Path.Combine(unpackDir, oldName);

                if (File.Exists(filePath))
                    File.Delete(filePath);

                shaderReg.DeleteValue(oldName);
            }

            if (shaderReg.GetValueNames().Length == 0)
                shaderManifest.DeleteSubKey(shaderKey);

            return hashes;
        }
    }
}
