using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RobloxClientTracker
{
    public enum ShaderType
    {
        Vertex   = (byte)'v',
        Fragment = (byte)'p',
    }

    public class ShaderFile : IComparable
    {
        public string Name;
        public string Hash;

        public int Offset;
        public int Size;

        public ShaderType ShaderType;
        public string Group;

        public byte[] Buffer;
        public byte[] Stub;

        public override string ToString()
        {
            string Type = Enum.GetName(typeof(ShaderType), ShaderType);
            return $"[{Type}] {Name}";
        }

        public int CompareTo(object obj)
        {
            int result;
            string value = ToString();

            if (obj is ShaderFile)
            {
                ShaderFile other = (ShaderFile)obj;
                result = Name.CompareTo(other.Name);
            }
            else
            {
                result = Name.CompareTo(obj);
            }

            return result;
        }

        public void WriteFile(string dir)
        {
            string contents = Program.UTF8.GetString(Buffer);

            if (contents.StartsWith("#version"))
            {
                var enumType = typeof(ShaderType);

                string extension = Enum.GetName(enumType, ShaderType)
                    .Substring(0, 4)
                    .ToLower();

                string shaderPath = Path.Combine(dir, Name + '.' + extension);
                
                var names = new List<int>();
                Regex variables = new Regex("_([0-9]+)");

                foreach (var match in variables.Matches(contents))
                {
                    string sValue = match
                        .ToString()
                        .Substring(1);

                    int value = int.Parse(sValue);
                    string name = extension.Substring(0, 1);

                    if (!names.Contains(value))
                        names.Add(value);

                    name += names.IndexOf(value);
                    contents = contents.Replace("_" + sValue, name);
                }

                Program.WriteFile(shaderPath, contents);
            }
        }
    }

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

        private static string readString(BinaryReader reader, int bufferSize)
        {
            byte[] sequence = reader.ReadBytes(bufferSize);
            string value = Encoding.ASCII.GetString(sequence);

            int length = value.IndexOf('\0');
            value = (length > 0 ? value.Substring(0, length) : value);
            
            return value;
        }

        public void UnpackShader(string exportDir)
        {
            var hashes = new HashSet<string>();
            string root = Groups[0];

            string unpackDir = Path.Combine(exportDir, Name);
            Directory.CreateDirectory(unpackDir);

            var rootShaders = Shaders.Where((shader) => shader.Group == root);
            var otherShaders = Shaders.Except(rootShaders);

            foreach (ShaderFile rootShader in rootShaders)
            {
                hashes.Add(rootShader.Hash);
                rootShader.WriteFile(unpackDir);
            }

            foreach (ShaderFile otherShader in otherShaders)
            {
                string GroupDir = Path.Combine(unpackDir, otherShader.Group);
                Directory.CreateDirectory(GroupDir);

                if (!hashes.Contains(otherShader.Hash))
                {
                    hashes.Add(otherShader.Hash);
                    otherShader.WriteFile(GroupDir);
                }
            }
        }

        public ShaderPack(string filePath)
        {
            FileInfo info = new FileInfo(filePath);

            using (FileStream file = File.OpenRead(filePath))
            using (BinaryReader reader = new BinaryReader(file))
            {
                Header = readString(reader, 4);
                Version = reader.ReadInt32();

                NumGroups = reader.ReadInt16();
                NumShaders = reader.ReadInt16();

                Name = info.Name.Replace(info.Extension, "");
                Hash = reader.ReadInt32();

                Groups = new string[NumGroups];
                Shaders = new ShaderFile[NumShaders];

                // Read Groups
                for (int i = 0; i < NumGroups; i++)
                    Groups[i] = readString(reader, 64);

                // Read Shaders
                for (int i = 0; i < NumShaders; i++)
                {
                    string name = readString(reader, 64);

                    byte[] rawHash = reader.ReadBytes(16);
                    string hash = Convert.ToBase64String(rawHash);

                    Shaders[i] = new ShaderFile
                    {
                        Name = name,
                        Hash = hash,

                        Offset = reader.ReadInt32(),
                        Size = reader.ReadInt32(),

                        ShaderType = (ShaderType)reader.ReadByte(),
                        Group = Groups[reader.ReadByte()],

                        Stub = reader.ReadBytes(6),
                    };
                }

                // Unpack the shader files
                foreach (ShaderFile shader in Shaders)
                {
                    file.Position = shader.Offset;
                    shader.Buffer = reader.ReadBytes(shader.Size);
                }
            }
        }
    }
}
