using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using RobloxFiles;
#pragma warning disable IDE1006 // Naming Styles

namespace RobloxClientTracker
{
    public static class ModelHasher
    {
        private static string hashBase64(string data)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(data);

            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(buffer);
                return Convert.ToBase64String(hash);
            }
        }

        public static string GetFileHash(RobloxFile file)
        {
            Contract.Requires(file != null);

            Instance root = file
                .GetChildren()
                .FirstOrDefault();

            string prefix = Path.Combine(file.Name, root.Name) + '\\';
            var manifest = new Dictionary<string, string>();
            
            foreach (var inst in root.GetDescendants())
            {
                string value = "";
                string extension = "";

                if (RobloxFileMiner.PullInstanceData(inst, ref value, ref extension))
                {
                    string path = inst.GetFullName("\\");

                    if (path.StartsWith(prefix, Program.InvariantString))
                        path = path.Substring(prefix.Length);

                    if (path.Contains("Packages\\"))
                        continue;

                    string hash = hashBase64(path + "\r\n" + value);
                    manifest.Add(path + extension, hash);
                }
            }

            StringBuilder builder = new StringBuilder();

            string[] keys = manifest.Keys
                .OrderBy(key => key)
                .ToArray();

            foreach (string key in keys)
            {
                string hash = manifest[key];
                builder.AppendLine($"[{hash}] {key}");
            }

            string signature = builder.ToString();
            return hashBase64(signature);
        }
    }
}
