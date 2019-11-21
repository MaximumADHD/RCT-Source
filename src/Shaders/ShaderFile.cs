using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using Microsoft.Win32;

namespace RobloxClientTracker
{
    public enum ShaderType
    {
        Vertex = (byte)'v',
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

        public void WriteFile(string dir, RegistryKey container, FileLogConfig logConfig)
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

                string currentHash = container.GetString(RegistryKey);

                if (currentHash != Hash)
                {
                    container.SetValue(RegistryKey, Hash);
                    Program.WriteFile(shaderPath, contents, logConfig);
                }
            }
        }
    }
}