using System;
using System.Collections.Generic;

namespace RobloxClientTracker.Luau
{
    public class LuauProto
    {
        public byte NumUpvalues;
        public byte NumParams;
        public bool IsVarArg;
        public byte MaxStackSize;
        public LuauProtoFlags Flags;

        public string[] Upvalues;
        public byte[] TypeInfo;
        public uint[] Code;

        public int LineDefined;
        public string DebugName;

        public byte LineGapLog2;
        public byte[] LineInfo;

        public LuauDisassembly Parent;
        public LuauLocVar[] LocVars;
        public LuauProto[] Children;
        public LuauConst[] Consts;

        private static string JUMP(int result)
        {
            if (result > 0)
                return $"[+{result}]";

            return $"[{result}]";
        }

        private static string JUMP(Func<int> value)
        {
            return JUMP(value());
        }

        private static string JUMP(Func<uint> value)
        {
            return JUMP((int)value());
        }

        public string CONST(uint id)
        {
            return $"K{id} [{Consts[id]}]";
        }

        public string CONST(int id)
        {
            return CONST((uint)id);
        }

        public string CONST(Func<uint> value)
        {
            uint id = value();
            return CONST(id);
        }

        public string CONST(Func<int> value)
        {
            uint id = (uint)value();
            return CONST(id);
        }

        public IEnumerable<string> Disassembly
        {
            get
            {
                var lines = new List<string>();
                var len = Code.Length;

                uint insn = 0;
                uint AUX = 0;

                Func<uint> A = () => LuauInsn.A(insn);
                Func<uint> B = () => LuauInsn.B(insn);
                Func<uint> C = () => LuauInsn.C(insn);

                Func<int> D = () => LuauInsn.D(insn);
                Func<int> E = () => LuauInsn.E(insn);

                for (int code = 0; code < len; code++)
                {
                    insn = Code[code];
                    AUX = 0;

                    if (code + 1 < len)
                        AUX = Code[code + 1];

                    var op = LuauInsn.OP(insn);
                    string line = $"{op} ";

                    switch (op)
                    {
                        case LuauOpcode.NOP:
                        case LuauOpcode.BREAK:
                        case LuauOpcode.COVERAGE:
                        {
                            // already good.
                            break;
                        }
                        case LuauOpcode.LOADNIL:
                        {
                            line += $"R{A()}";
                            break;
                        }
                        case LuauOpcode.LOADB:
                        {
                            var offset = C();
                            line += $"R{A()} {B()}";

                            if (offset != 0)
                                line += $" +{offset}";

                            break;
                        }
                        case LuauOpcode.LOADN:
                        {
                            line += $"R{A()} {B()}";
                            break;
                        }
                        case LuauOpcode.LOADK:
                        {
                            line += $"R{A()} {CONST(B)}";
                            break;
                        }
                        case LuauOpcode.NOT:
                        case LuauOpcode.MOVE:
                        case LuauOpcode.MINUS:
                        case LuauOpcode.LENGTH:
                        {
                            line += $"R{A()} R{B()}";
                            break;
                        }
                        case LuauOpcode.GETGLOBAL:
                        case LuauOpcode.SETGLOBAL:
                        {
                            line += $"R{A()} {CONST(AUX)}";
                            code++; // AUX

                            break;
                        }
                        case LuauOpcode.GETUPVAL:
                        case LuauOpcode.SETUPVAL:
                        {
                            line += $"R{A()} {B()}";
                            break;
                        }
                        case LuauOpcode.CLOSEUPVALS:
                        {
                            line += $"R{A()}";
                            break;
                        }
                        case LuauOpcode.GETIMPORT:
                        {
                            line += $"R{A()} {CONST(D)}";
                            code++; // AUX
                            break;
                        }
                        case LuauOpcode.OR:
                        case LuauOpcode.AND:
                        case LuauOpcode.ADD:
                        case LuauOpcode.SUB:
                        case LuauOpcode.MUL:
                        case LuauOpcode.DIV:
                        case LuauOpcode.POW:
                        case LuauOpcode.MOD:
                        case LuauOpcode.IDIV:
                        case LuauOpcode.CONCAT:
                        case LuauOpcode.GETTABLE:
                        case LuauOpcode.SETTABLE:
                        {
                            line += $"R{A()} R{B()} R{C()}";
                            break;
                        }
                        case LuauOpcode.ORK:
                        case LuauOpcode.ANDK:
                        case LuauOpcode.ADDK:
                        case LuauOpcode.SUBK:
                        case LuauOpcode.MULK:
                        case LuauOpcode.DIVK:
                        case LuauOpcode.POWK:
                        case LuauOpcode.MODK:
                        case LuauOpcode.DIVRK:
                        case LuauOpcode.IDIVK:
                        case LuauOpcode.SUBRK:
                        {
                            line += $"R{A()} R{B()} {CONST(C)}";
                            break;
                        }
                        case LuauOpcode.NAMECALL:
                        case LuauOpcode.GETTABLEKS:
                        case LuauOpcode.SETTABLEKS:
                        {
                            line += $"R{A()} R{B()} {CONST(AUX)}";
                            code++; // AUX

                            break;
                        }
                        case LuauOpcode.GETTABLEN:
                        case LuauOpcode.SETTABLEN:
                        {
                            line += $"R{A()} R{B()} {C() + 1}";
                            break;
                        }
                        case LuauOpcode.NEWCLOSURE:
                        {
                            line += $"R{A()} P{D()}";
                            break;
                        }
                        case LuauOpcode.CALL:
                        {
                            line += $"R{A()} {(int)B() - 1} {(int)C() - 1}";
                            break;
                        }
                        case LuauOpcode.RETURN:
                        {
                            line += $"R{A()} {(int)B() - 1}";
                            break;
                        }
                        case LuauOpcode.JUMP:
                        case LuauOpcode.JUMPBACK:
                        {
                            line += $"{JUMP(D)}";
                            break;
                        }
                        case LuauOpcode.JUMPIF:
                        case LuauOpcode.JUMPIFNOT:
                        {
                            line += $"R{A()} {JUMP(D)}";
                            break;
                        }
                        case LuauOpcode.JUMPIFEQ:
                        case LuauOpcode.JUMPIFLE:
                        case LuauOpcode.JUMPIFLT:
                        case LuauOpcode.JUMPIFNOTEQ:
                        case LuauOpcode.JUMPIFNOTLE:
                        case LuauOpcode.JUMPIFNOTLT:
                        {
                            line += $"R{A()} R{AUX} {JUMP(D)}";
                            code++; // AUX
                            break;
                        }
                        case LuauOpcode.NEWTABLE:
                        {
                            line += $"R{A()} {(B() == 0 ? 0 : 1 << ((int)B() - 1))} {AUX}";
                            code++; // AUX
                            break;
                        }
                        case LuauOpcode.SETLIST:
                        {
                            line += $"R{A()} R{B()} {(int)C() - 1} [{AUX}]";
                            code++; // AUX
                            break;
                        }
                        case LuauOpcode.FORGLOOP:
                        {
                            var varCount = (byte)AUX;
                            string style = (int)AUX < 0 ? " [inext]" : "";

                            line += $"R{A()} {varCount}{style} {JUMP(D)}";
                            code++; // AUX
                            break;
                        }
                        case LuauOpcode.FORNPREP:
                        case LuauOpcode.FORNLOOP:
                        case LuauOpcode.FORGPREP:
                        case LuauOpcode.FORGPREP_NEXT:
                        case LuauOpcode.FORGPREP_INEXT:
                        {
                            line += $"R{A()}";
                            break;
                        }
                        case LuauOpcode.GETVARARGS:
                        {
                            line += $"R{A()} {(int)B() - 1}";
                            break;
                        }
                        case LuauOpcode.DUPTABLE:
                        case LuauOpcode.DUPCLOSURE:
                        {
                            line += $"R{A()} {CONST(D)}";
                            break;
                        }
                        case LuauOpcode.PREPVARARGS:
                        {
                            line += $"{A()}";
                            break;
                        }
                        case LuauOpcode.LOADKX:
                        {
                            line += $"R{A()} {CONST(B)}";
                            break;
                        }
                        case LuauOpcode.JUMPX:
                        {
                            line += $"{JUMP(E)}";
                            break;
                        }
                        case LuauOpcode.FASTCALL:
                        {
                            line += $"{(LuauBuiltinFunction)A()} {JUMP(C)}";
                            break;
                        }
                        case LuauOpcode.CAPTURE:
                        {
                            var captureType = (LuauCaptureType)A();
                            line += $"{captureType} ";

                            if (captureType == LuauCaptureType.REF || captureType == LuauCaptureType.VAL)
                                line += 'R';
                            else
                                line += 'U';

                            line += $"{B()}";
                            break;
                        }
                        case LuauOpcode.FASTCALL1:
                        {
                            line += $"{(LuauBuiltinFunction)A()} R{B()} {JUMP(C)}";
                            break;
                        }
                        case LuauOpcode.FASTCALL2:
                        {
                            line += $"{(LuauBuiltinFunction)A()} R{B()} R{AUX} {JUMP(C)}";
                            code++; // AUX
                            break;
                        }
                        case LuauOpcode.FASTCALL2K:
                        {
                            line += $"{(LuauBuiltinFunction)A()} R{B()} K{AUX} {JUMP(C)}";
                            code++; // AUX
                            break;
                        }
                        case LuauOpcode.FASTCALL3:
                        {
                            line += $"{(LuauBuiltinFunction)A()} R{B()} R{AUX & 0xFF} R{(AUX >> 8) & 0xFF}";
                            code++; // AUX
                            break;
                        }
                        case LuauOpcode.JUMPXEQKB:
                        case LuauOpcode.JUMPXEQKNIL:
                        {
                            var expect = (AUX & 1) > 0;
                            var not = (AUX >> 31) > 0;
                            string flag = "";

                            if (op == LuauOpcode.JUMPXEQKB)
                                flag = expect ? "TRUE " : "FALSE ";

                            line = line.Replace("X", not ? "IFNOT" : "IF");
                            line += $"R{A()} {flag}{JUMP(D)}";

                            code++; // AUX
                            break;
                        }
                        case LuauOpcode.JUMPXEQKN:
                        case LuauOpcode.JUMPXEQKS:
                        {
                            var index = AUX & 0xffffff;
                            var not = (AUX >> 31) > 0;

                            line = line.Replace("X", not ? "IFNOT" : "IF");
                            line += $"R{A()} {CONST(index)} {JUMP(D)}";

                            code++; // AUX
                            break;
                        }
                        default:
                        {
                            line += $" [!! UNIMPLEMENTED !!]";
                            break;
                        }
                    }

                    lines.Add(line.Trim());
                }

                return lines;
            }
        }
    }
}
