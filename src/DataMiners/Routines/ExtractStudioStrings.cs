using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RobloxClientTracker
{
    public class ExtractStudioStrings : MultiTaskMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Magenta;
        private string studioExe;

        public override void ExecuteRoutine()
        {
            // Both of the routines datamine 
            // Roblox Studio, so preload it first.

            print("Reading Roblox Studio...");
            studioExe = File.ReadAllText(studioPath);

            // Now execute the routines.
            addRoutine(extractCppTypes);
            addRoutine(extractDeepStrings);
            
            base.ExecuteRoutine();
        }

        private static List<string> hackOutPattern(string source, string pattern)
        {
            MatchCollection matches = Regex.Matches(source, pattern);
            var lines = new List<string>();

            foreach (Match match in matches)
            {
                string matchStr = match.Groups[0].ToString();

                if (matchStr.Length <= 4)
                    continue;

                if (lines.Contains(matchStr))
                    continue;

                string firstChar = matchStr.Substring(0, 1);
                Match sanitize = Regex.Match(firstChar, "^[A-z_%*'-]");

                if (sanitize.Length == 0)
                    continue;

                lines.Add(matchStr);
            }

            lines.Sort();
            return lines;
        }

        private void extractDeepStrings()
        {
            print("Extracting Deep Strings...");

            var matches = Regex.Matches(studioExe, "([A-Z][A-z][A-z_0-9.]{8,256})+[A-z0-9]?");

            var lines = matches.Cast<Match>()
                .Select(match => match.Value)
                .OrderBy(str => str)
                .Distinct();

            string deepStrings = string.Join("\n", lines);
            string deepStringsPath = Path.Combine(stageDir, "DeepStrings.txt");

            writeFile(deepStringsPath, deepStrings);
        }

        private void extractCppTypes()
        {
            print("Extracting CPP types...");

            var classes = hackOutPattern(studioExe, "AV[A-z][A-z0-9_@?]+");
            var RBX = new NameTree("RBX");

            foreach (string linkerChunk in classes)
            {
                if (linkerChunk.Length > 8)
                {
                    string data = linkerChunk.Substring(2);
                    string lineEnd = data.Substring(data.Length - 6);

                    if (lineEnd == "@RBX@@")
                    {
                        string[] chunks = data.Split('@');
                        NameTree currentTree = RBX;

                        for (int i = chunks.Length - 1; i >= 0; i--)
                        {
                            string chunk = chunks[i];

                            if (chunk.Length > 0 && chunk != "RBX")
                            {
                                currentTree = currentTree.Add(chunk);
                            }
                        }
                    }
                }
            }

            string cppTree = RBX.WriteTree();
            string cppTreePath = Path.Combine(stageDir, "CppTree.txt");

            writeFile(cppTreePath, cppTree);
        }
    }
}
