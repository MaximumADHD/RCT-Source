using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

using Microsoft.Win32;

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

        public byte[] Buffer;
        public byte[] Stub;

        public override string ToString()
        {
            string Type = Enum.GetName(typeof(ShaderType), ShaderType);
            return $"[{Type}] {Name}";
        }

        public string RegistryKey
        {
            get { return $"{Group}/{Name}"; }
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

        public void WriteFile(UnpackShaders unpacker, string dir, RegistryKey container)
        {
            string contents = Program.UTF8.GetString(Buffer);

            if (contents.StartsWith("#version"))
            {
                var enumType = typeof(ShaderType);

                string extension = Enum.GetName(enumType, ShaderType)
                    .Substring(0, 4)
                    .ToLower();

                string shaderPath = Path.Combine(dir, Name + '.' + extension);
                var writtenHeaders = new HashSet<string>();
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