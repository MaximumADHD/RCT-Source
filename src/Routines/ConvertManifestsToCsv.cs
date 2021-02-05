using System;
using System.IO;
using System.Linq;

namespace RobloxClientTracker
{
    public class ConvertManifestsToCsv : MultiTaskMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Cyan;

        public ConvertManifestsToCsv()
        {
            addRoutine(buildCsvFileManifest);
            addRoutine(buildCsvPackageManifest);
        }

        private static string flipColumns(string line)
        {
            string[] pair = line.Split(',');
            return pair[1] + ',' + pair[0];
        }

        private static void permutate(ref string file, Func<string, string> permutation)
        {
            string[] lines = file.Split('\r', '\n')
                .Where(line => line.Length > 0)
                .Select(line => permutation(line))
                .ToArray();

            file = string.Join("\n", lines);
        }

        // Converts rbxManifest.txt -> rbxManifest.csv
        private void buildCsvFileManifest()
        {
            string filePath = Path.Combine(stageDir, "rbxManifest.txt");
            string fileCsvPath = Path.Combine(stageDir, "rbxManifest.csv");

            var manifestHeaders = new string[2]
            {
                "File Name",
                "MD5 Signature"
            };

            CsvBuilder.Convert(filePath, manifestHeaders, fileCsv =>
            {
                permutate(ref fileCsv, flipColumns);
                writeFile(fileCsvPath, fileCsv);
            });
        }

        // Converts rbxPkgManifest.txt -> rbxPkgManifest.csv
        private void buildCsvPackageManifest()
        {
            string pkgPath = Path.Combine(stageDir, "rbxPkgManifest.txt");
            string pkgCsvPath = Path.Combine(stageDir, "rbxPkgManifest.csv");

            var pkgHeaders = new string[4]
            {
                "File Name",
                "MD5 Signature",
                "Compressed Size (bytes)",
                "Size (bytes)"
            };

            CsvBuilder.Convert(pkgPath, pkgHeaders, pkgCsv => writeFile(pkgCsvPath, pkgCsv));
        }
    }
}
