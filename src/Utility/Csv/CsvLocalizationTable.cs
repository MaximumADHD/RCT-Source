using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using RobloxFiles;
using Newtonsoft.Json;
using System.Diagnostics.Contracts;

namespace RobloxClientTracker
{
    public class CsvLocalizationTable : List<CsvLocalizationEntry>
    {
        public CsvLocalizationTable(LocalizationTable table)
        {
            Contract.Requires(table != null);
            string contents = table.Contents;
            JsonConvert.PopulateObject(contents, this);
        }

        public string WriteCsv()
        {
            Type entryType = typeof(CsvLocalizationEntry);

            // Select all distinct language definitions from the table entries.
            string[] languages = this
                .SelectMany(entry => entry.Values
                    .Where(pair => pair.Value.Length > 0)
                    .Select(pair => pair.Key))
                .Distinct()
                .OrderBy(key => key)
                .ToArray();

            // Select headers whose columns actually have content.
            string[] headers = new string[3] { "Key", "Source", "Context" }
                .Select(header => entryType.GetField(header))
                .Where(field => this
                    .Any(entry => !string.IsNullOrEmpty(field.GetValue(entry) as string)))
                .Select(field => field.Name)
                .Concat(languages)
                .ToArray();

            var lines = new List<string>();
            var fields = new Dictionary<string, FieldInfo>();

            foreach (var entry in this)
            {
                foreach (string header in headers)
                {
                    string value = "";

                    if (entry.Values.ContainsKey(header))
                    {
                        value = entry.Values[header];
                    }
                    else
                    {
                        try
                        {
                            if (!fields.ContainsKey(header))
                                fields.Add(header, entryType.GetField(header));

                            value = (fields[header]?.GetValue(entry) ?? " ") as string;
                        }
                        catch
                        {
                            value = " ";
                        }
                    }

                    if (value == null)
                        value = " ";

                    if (value.Contains(",", Program.InvariantString))
                    {
                        if (value.Contains("\"", Program.InvariantString))
                        {
                            value = value.Replace("\"", "\\\"", Program.InvariantString);
                            value = value.Replace("\\\\\"", "\\\"", Program.InvariantString);
                        }

                        value = '"' + value + '"';
                    }

                    lines.Add(value);
                }
            }

            return CsvBuilder.Convert(lines, headers);
        }
    }
}
