using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RobloxClientTracker
{
    public static class StudioStrings
    {
        private static void print(string msg)
        {
            Program.print(msg, Program.MAGENTA);
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

        private static void extractDeepStrings(string file)
        {
            print("Extracting Deep Strings...");

            string stageDir = Program.StageDir;
            MatchCollection matches = Regex.Matches(file, "([A-Z][A-z][A-z_0-9.]{8,256})+[A-z0-9]?");

            var lines = matches.Cast<Match>()
                .Select(match => match.Value)
                .OrderBy(str => str)
                .Distinct();

            string deepStrings = string.Join("\n", lines);
            string deepStringsPath = Path.Combine(stageDir, "DeepStrings.txt");

            Program.WriteFile(deepStringsPath, deepStrings);
        }

        private static void extractCppTypes(string file)
        {
            print("Extracting CPP types...");

            string stageDir = Program.StageDir;
            List<string> classes = hackOutPattern(file, "AV[A-z][A-z0-9_@?]+");
            
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

            Program.WriteFile(cppTreePath, cppTree);
        }

        public static void Extract()
        {
            string studioPath = Program.StudioPath;
            string file = File.ReadAllText(studioPath);

            Task cppTypes = Task.Run(() => extractCppTypes(file));
            Task deepStrings = Task.Run(() => extractDeepStrings(file));

            Task extraction = Task.WhenAll(cppTypes, deepStrings);
            extraction.Wait();
        }
    }
}
