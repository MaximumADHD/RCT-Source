using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace RobloxClientTracker
{
    public class ExtractStudioStrings : MultiTaskMiner
    {
        [Flags]
        enum UnDecorateFlags
        {
            UNDNAME_COMPLETE = (0x0000),  // Enable full undecoration
            UNDNAME_NO_LEADING_UNDERSCORES = (0x0001),  // Remove leading underscores from MS extended keywords
            UNDNAME_NO_MS_KEYWORDS = (0x0002),  // Disable expansion of MS extended keywords
            UNDNAME_NO_FUNCTION_RETURNS = (0x0004),  // Disable expansion of return type for primary declaration
            UNDNAME_NO_ALLOCATION_MODEL = (0x0008),  // Disable expansion of the declaration model
            UNDNAME_NO_ALLOCATION_LANGUAGE = (0x0010),  // Disable expansion of the declaration language specifier
            UNDNAME_NO_MS_THISTYPE = (0x0020),  // NYI Disable expansion of MS keywords on the 'this' type for primary declaration
            UNDNAME_NO_CV_THISTYPE = (0x0040),  // NYI Disable expansion of CV modifiers on the 'this' type for primary declaration
            UNDNAME_NO_THISTYPE = (0x0060),  // Disable all modifiers on the 'this' type
            UNDNAME_NO_ACCESS_SPECIFIERS = (0x0080),  // Disable expansion of access specifiers for members
            UNDNAME_NO_THROW_SIGNATURES = (0x0100),  // Disable expansion of 'throw-signatures' for functions and pointers to functions
            UNDNAME_NO_MEMBER_TYPE = (0x0200),  // Disable expansion of 'static' or 'virtual'ness of members
            UNDNAME_NO_RETURN_UDT_MODEL = (0x0400),  // Disable expansion of MS model for UDT returns
            UNDNAME_32_BIT_DECODE = (0x0800),  // Undecorate 32-bit decorated names
            UNDNAME_NAME_ONLY = (0x1000),  // Crack only the name for primary declaration;
                                           // return just [scope::]name.  Does expand template params
            UNDNAME_NO_ARGUMENTS = (0x2000),  // Don't undecorate arguments to function
            UNDNAME_NO_SPECIAL_SYMS = (0x4000),  // Don't undecorate special names (v-table, vcall, vector xxx, metatype, etc)
        }

        [DllImport("dbghelp.dll", SetLastError = true, PreserveSig = true)]
        static extern int UnDecorateSymbolName
        (
            [In]  [MarshalAs(UnmanagedType.LPStr)] string DecoratedName,
            [Out]  StringBuilder UnDecoratedName,
            [In]  [MarshalAs(UnmanagedType.U4)] int UndecoratedLength,
            [In]  [MarshalAs(UnmanagedType.U4)] UnDecorateFlags Flags
        );

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

            var classes = hackOutPattern(studioExe, "(AV|AW4)[A-z0-9_@\\?\\$]+");
            var lines = new List<string>();

            foreach (string linkerChunk in classes)
            {
                if (linkerChunk.Length > 8 && linkerChunk.ToLower().EndsWith("rbx@@"))
                {
                    StringBuilder output = new StringBuilder(8192);
                    UnDecorateSymbolName("?" + linkerChunk, output, 8192, UnDecorateFlags.UNDNAME_NO_ARGUMENTS);

                    string result = output.ToString();

                    if (result == "?" + linkerChunk)
                        continue;

                    lines.Add(result.Substring(6));
                }
            }

            string cppTree = string.Join("\r\n", lines
                .OrderBy(line => line)
                .Distinct());
            
            string cppTreePath = Path.Combine(stageDir, "CppTree.txt");
            writeFile(cppTreePath, cppTree);
        }
    }
}
