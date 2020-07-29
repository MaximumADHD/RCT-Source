using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

using Microsoft.Win32;
using RobloxFiles.BinaryFormat.Chunks;

namespace RobloxClientTracker
{
    public enum ShaderType
    {
        Vertex = (byte)'v',
        Compute = (byte)'c',
        Fragment = (byte)'p',
    }

    public class ShaderFile : IComparable
    {
        private const string extendGL = "#extension GL_ARB_shading_language_include : require";

        public string Name;
        public string Hash;

        public int Offset;
        public int Size;

        public ShaderType ShaderType;
        public string Group;

        public byte[] Stub;
        public byte[] Buffer;

        public int Level = 0;

        public string Id
        {
            get
            {
                string level = "";

                if (Name.EndsWith('_'))
                    level = $"Level{Level}";

                return $"{Name}{level}";
            }
        }

        public override string ToString()
        {
            string Type = Enum.GetName(typeof(ShaderType), ShaderType);
            return $"[{Type}] {Id}";
        }

        public string RegistryKey
        {
            get { return $"{Group}/{Id}"; }
        }

        public int CompareTo(object obj)
        {
            if (obj is ShaderFile)
            {
                ShaderFile other = (ShaderFile)obj;

                if (Name == other.Name && Level != other.Level)
                    return Level - other.Level;
                
                return Id.CompareTo(other.Id);
            }

            return Id.CompareTo(obj);
        }

        public void WriteFile(UnpackShaders unpacker, string dir, RegistryKey container)
        {
            string contents = Program.UTF8.GetString(Buffer);

            if (contents.StartsWith("#version"))
            {
                var enumType = typeof(ShaderType);

                string extension = Enum.GetName(enumType, ShaderType)
                    .Substring(0, 4)
                    .ToLower();

                string shaderPath = Path.Combine(dir, Id + '.' + extension);
                var names = new List<int>();

                Regex variables = new Regex("_([0-9]+)");
                Regex structs = new Regex("struct ([A-z]+)\n{[^}]+};\n\n");
                
                foreach (var variable in variables.Matches(contents))
                {
                    string str = variable
                        .ToString()
                        .Substring(1);

                    int value = int.Parse(str);
                    string name = extension.Substring(0, 1);

                    if (!names.Contains(value))
                        names.Add(value);

                    name += names.IndexOf(value);
                    contents = contents.Replace("_" + str, name);
                }

                foreach (Match match in structs.Matches(contents))
                {
                    string fullStruct = match.ToString();
                    string structName = match.Groups[1].Value;

                    if (!unpacker.HasHeaderFile(structName))
                    {
                        string filePath = Path.Combine(unpacker.IncludeDir, $"{structName}.h");
                        unpacker.WriteShader(filePath, fullStruct.Trim());
                        unpacker.AddHeaderFile(structName);
                    }

                    string line = $"#include <{structName}.h>\n";

                    if (!contents.Contains(extendGL))
                        line = extendGL + "\n" + line;

                    contents = contents.Replace(fullStruct, line);
                }

                string currentHash = container.GetString(RegistryKey);

                if (currentHash != Hash)
                {
                    container.SetValue(RegistryKey, Hash);
                    unpacker.WriteShader(shaderPath, contents);
                }
            }
        }
    }
}