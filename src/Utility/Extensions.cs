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

    public static string ReadString(this BinaryReader reader, int bufferSize)
    {
        byte[] sequence = reader?.ReadBytes(bufferSize);
        string value = Encoding.UTF8.GetString(sequence);

        int length = value.IndexOf('\0');
        value = (length > 0 ? value.Substring(0, length) : value);

        return value;
    }
}