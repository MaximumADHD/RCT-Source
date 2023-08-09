using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RobloxClientTracker.Properties;
using RobloxStudioModManager;

namespace RobloxClientTracker
{
    public class ScanFastFlags : DataMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Yellow;

        /// https://stackoverflow.com/a/39021296/11852173
        /// <summary>Looks for the next occurrence of a sequence in a byte array</summary>
        /// <param name="array">Array that will be scanned</param>
        /// <param name="start">Index in the array at which scanning will begin</param>
        /// <param name="sequence">Sequence the array will be scanned for</param>
        /// <returns>
        ///   The index of the next occurrence of the sequence of -1 if not found
        /// </returns>
        private static int findSequence(byte[] array, int start, byte[] sequence)
        {
            int end = array.Length - sequence.Length; // past here no match is possible
            byte firstByte = sequence[0]; // cached to tell compiler there's no aliasing

            while (start <= end)
            {
                // scan for first byte only. compiler-friendly.
                if (array[start] == firstByte)
                {
                    // scan for rest of sequence
                    for (int offset = 1; ; ++offset)
                    {
                        if (offset == sequence.Length)
                        {
                            // full sequence matched?
                            return start;
                        }
                        else if (array[start + offset] != sequence[offset])
                        {
                            break;
                        }
                    }
                }
                ++start;
            }

            // end of array reached without match
            return -1;
        }

        public override void ExecuteRoutine()
        {
            string extraContent = createDirectory(studioDir, "content");

            // HashSets ignores duplicate entries
            var flags = new HashSet<string>();
            var timer = new Stopwatch();

            print("Starting FastVariable scan...");

            // Scan flags defined in Lua scripts

            timer.Start();

            foreach (var file in Directory.GetFiles(extraContent, "*.lua", SearchOption.AllDirectories))
            {
                string contents = File.ReadAllText(file);
                var matches = Regex.Matches(contents, "game:(?:Get|Define)Fast(Flag|Int|String)\\(\\\"(\\w+)\\\"").Cast<Match>();

                foreach (var match in matches)
                {
                    string flag = String.Format("F{0}{1}", match.Groups[1], match.Groups[2]);
                    flags.Add($"[Lua] {flag}");
                }
            }

            // Scan flags defined in the binary

            var binary = File.ReadAllBytes(studioPath);
            int knownFlagAddress = findSequence(binary, 0, Encoding.UTF8.GetBytes("DebugDisplayFPS"));

            if (knownFlagAddress == -1)
            {
                print("FAST FLAG EXTRACTION FAILED! (Could not find address of known flag)", ConsoleColor.Red);
                return;
            }

            // https://files.pizzaboxer.xyz/i/x64dbg_PavTJp2sLp.png
            // Note that for instructions that handle addresses (e.g. lea and jmp), operand is
            // an offset to a memory address relative to the address of the instruction's end
            // mov r8d, <value>       | 41 B8 ?? ?? ?? ??    | Determines if it's a dynamic flag
            // lea rdx, ds:[<offset>] | 48 8D 15 ?? ?? ?? ?? | Ignore, we don't really care about this, might be for the value?
            // lea rcx, ds:[<offset>] | 48 8D 0D ?? ?? ?? ?? | Loads the string of the flag name into memory 
            // jmp <offset>           | E9 ?? ?? ?? ??       | Jumps to the subroutine that registers it as a flag

            int position = 0;
            int index = 0;

            int knownFlagLoadAddress = 0;
            int leaOffset = 0;

            while (knownFlagLoadAddress == 0)
            {
                // Look for the 'lea rcx' instruction
                int leaInstAddr = findSequence(binary, position, new byte[] { 0x48, 0x8D, 0x0D });

                if (index == -1)
                {
                    print("FAST FLAG EXTRACTION FAILED! (Could not find address of instruction that loads known flag)", ConsoleColor.Red);
                    return;
                }

                // Next instruction should be a 'jmp'
                if (binary[leaInstAddr + 7] != 0xE9)
                {
                    position = leaInstAddr + 3;
                    continue;
                }

                int leaInstOperand = BitConverter.ToInt32(binary, leaInstAddr + 3);
                int leaTargetAddr = leaInstAddr + 7 + leaInstOperand;

                // Weird oddity - the target address specified by the lea instruction may
                // itself be offset up to 0x0F00 bytes prior, with an alignment of 0x0100
                for (int i = 0; i > -0x0F00; i -= 0x0100)
                {
                    if (leaTargetAddr + i == knownFlagAddress)
                    {
                        leaOffset = i;
                        knownFlagLoadAddress = leaInstAddr;
                        break;
                    }
                }

                position = leaInstAddr + 3;
            }

            // After the lea instruction comes a jmp instruction
            // The target address of this instruction is a subroutine which all registered
            // flags go through
            // There are different subroutines for each type (FFlag, FInt, FString, etc)
            // which are all aligned by 0x20

            int jmpInstAddr = knownFlagLoadAddress + 7;
            int jmpInstOperand = BitConverter.ToInt32(binary, jmpInstAddr + 1);
            int jmpTargetAddr = jmpInstAddr + 5 + jmpInstOperand;

            var typeAddresses = new Dictionary<int, string>
            {
                { jmpTargetAddr + 0x20 * 0, "FFlag"   },
                { jmpTargetAddr + 0x20 * 1, "SFFlag"  },
                { jmpTargetAddr + 0x20 * 2, "FInt"    },
                { jmpTargetAddr + 0x20 * 3, "FLog"    },
                { jmpTargetAddr + 0x20 * 4, "FString" },
            };

            position = 0;
            while (true)
            {
                jmpInstAddr = findSequence(binary, position, new byte[] { 0xE9 });

                if (jmpInstAddr == -1)
                    break;

                jmpInstOperand = BitConverter.ToInt32(binary, jmpInstAddr + 1);
                jmpTargetAddr = jmpInstAddr + 5 + jmpInstOperand;

                if (!typeAddresses.TryGetValue(jmpTargetAddr, out string flagType))
                {
                    position = jmpInstAddr + 1;
                    continue;
                }

                int leaAddress = jmpInstAddr - 7;
                int targetLeaOffset = BitConverter.ToInt32(binary, leaAddress + 3);
                int targetLeaAddress = leaAddress + 7 + targetLeaOffset + leaOffset;

                string flagName = "";

                if (flagType != "SFFlag")
                {
                    // Check if it's a dynamic flag
                    // The operand of the 'mov' instruction will be 0x2 if it is

                    int movAddress = jmpInstAddr - 20;
                    int movValue = binary[movAddress + 2];

                    if (movValue == 0x2)
                        flagName += 'D';
                }

                flagName += flagType;

                int stringIndex = targetLeaAddress;

                for (int i = targetLeaAddress; binary[i] != 0; i++)
                    flagName += Convert.ToChar(binary[i]);

                flags.Add($"[C++] {flagName}");

                position = jmpInstAddr + 1;
            }

            timer.Stop();

            print($"FastVariable scan completed in {timer.Elapsed} with {flags.Count} variables");

            var sortedFlags = flags.OrderBy(x => x.Substring(6)).ToList();

            string flagsPath = Path.Combine(stageDir, "FVariables.txt");
            string result = string.Join("\r\n", sortedFlags);

            writeFile(flagsPath, result);
        }
    }
}
