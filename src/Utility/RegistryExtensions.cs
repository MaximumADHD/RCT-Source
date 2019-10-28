using Microsoft.Win32;

public static class RegistryExtensions
{
    public static RegistryKey Open(this RegistryKey start, params string[] traversal)
    {
        RegistryKey current = start;

        foreach (string key in traversal)
            current = current.CreateSubKey(key, RegistryKeyPermissionCheck.ReadWriteSubTree);

        return current;
    }

    public static string GetString(this RegistryKey registry, string key)
    {
        return registry.GetValue(key, "") as string;
    }
}