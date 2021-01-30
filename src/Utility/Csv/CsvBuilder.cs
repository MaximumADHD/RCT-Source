using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;

namespace RobloxClientTracker
{
    public static class CsvBuilder
    {
        public static string Convert(string file, IEnumerable<string> headers)
        {
            Contract.Requires(file != null);

            int column = 0;
            int numColumns = headers.Count();

            string[] lines = file.Split('\r', '\n')
                .Where(line => line.Length > 0)
                .ToArray();

            string csv = string.Join(",", headers);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (i == 0 && line == "v0")
                    continue;

                if (column++ % numColumns == 0)
                    csv += '\n';
                else
                    csv += ',';

                csv += line;
            }

            return csv;
        }

        public static string Convert(string file, params string[] headers)
        {
            return Convert(file, headers.AsEnumerable());
        }

        public static void Convert(string path, string[] headers, Action<string> callback)
        {
            Contract.Requires(callback != null);

            if (!File.Exists(path))
            {
                Program.print("Missing CSV file: " + path, ConsoleColor.Red);
                return;
            }

            string file = File.ReadAllText(path);
            string csv = Convert(file, headers);

            callback(csv);
        }

        public static string Convert(IEnumerable<string> lines, params string[] headers)
        {
            string file = string.Join("\r\n", lines);
            return Convert(file, headers);
        }
    }
}
