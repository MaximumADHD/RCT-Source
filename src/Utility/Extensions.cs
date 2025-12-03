using System;
using System.IO;
using System.Text;
using System.Globalization;

using Microsoft.Win32;

public static class Extensions
{
    public static RegistryKey Open(this RegistryKey start, params string[] traversal)
    {
        RegistryKey current = start;

        foreach (string key in traversal)
            current = current?.CreateSubKey(key, RegistryKeyPermissionCheck.ReadWriteSubTree);

        return current;
    }

    public static string GetString(this RegistryKey registry, string key)
    {
        return registry?.GetValue(key, "") as string;
    }

    public static string ReadString(this BinaryReader reader, int? bufferSize)
    {
        if (bufferSize.HasValue)
        {
            byte[] sequence = reader?.ReadBytes(bufferSize.Value);
            string value = Encoding.UTF8.GetString(sequence);

            int length = value.IndexOf('\0');
            value = (length > 0 ? value.Substring(0, length) : value);

            return value;
        }
        else
        {
            var stream = reader.BaseStream;
            var sequence = new byte[0];

            var startIndex = stream.Position;
            var endIndex = startIndex;

            while (endIndex < stream.Length)
            {
                if (reader.ReadByte() == 0)
                    break;

                endIndex++;
            }
                

            stream.Position = startIndex;
            sequence = reader.ReadBytes((int)(endIndex - startIndex));

            return Encoding.UTF8.GetString(sequence);
        }
    }
}