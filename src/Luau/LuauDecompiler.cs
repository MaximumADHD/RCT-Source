using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RobloxClientTracker.Luau
{
    public sealed class LuauDecompilerOptions
    {
        public bool IncludeFunctionHeaders = true;
        public bool IncludeInstructionComments = false;
        public bool IncludeProtoLabels = true;
        public bool DeclareRegisters = true;
    }

    public sealed class LuauDecompiler
    {
        public string Decompile(byte[] buffer, LuauDecompilerOptions options = null)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            return Decompile(new LuauDisassembly(buffer), options);
        }

        public string Decompile(LuauDisassembly bytecode, LuauDecompilerOptions options = null)
        {
            if (bytecode == null)
                throw new ArgumentNullException(nameof(bytecode));

            options = options ?? new LuauDecompilerOptions();
            var result = new StringBuilder();

            for (int protoId = 0; protoId < bytecode.Protos.Length; protoId++)
            {
                if (protoId > 0)
                    result.AppendLine();

                result.Append(DecompileProto(bytecode.Protos[protoId], protoId, options, new HashSet<int>(), protoId != bytecode.MainId));
            }

            return result.ToString();
        }

        private string DecompileProto(LuauProto proto, int protoId, LuauDecompilerOptions options, HashSet<int> activeProtos, bool includeHeader)
        {
            if (proto == null)
                throw new ArgumentNullException(nameof(proto));

            if (!activeProtos.Add(protoId))
                return "function()\n    -- recursive closure\nend";

            var result = new StringBuilder();
            var expressions = new Dictionary<int, string>();
            var declaredRegisters = new HashSet<int>();
            var pendingCallValues = new Dictionary<int, string>();
            var nameCalls = new Dictionary<int, string>();
            var closeBlocksAt = new Dictionary<int, int>();
            int indentLevel = includeHeader && options.IncludeFunctionHeaders ? 1 : 0;

            if (options.IncludeProtoLabels)
                result.AppendLine("-- PROTO_" + protoId);

            if (includeHeader && options.IncludeFunctionHeaders)
            {
                result.Append("function");

                if (!string.IsNullOrEmpty(proto.DebugName) && proto.DebugName != "NULL")
                    result.Append(" ").Append(SanitizeName(proto.DebugName));

                result.Append("(");

                for (int i = 0; i < proto.NumParams; i++)
                {
                    if (i > 0)
                        result.Append(", ");

                    result.Append(LocalName(proto, i, 0));
                }

                if (proto.IsVarArg)
                {
                    if (proto.NumParams > 0)
                        result.Append(", ");

                    result.Append("...");
                }

                result.AppendLine(")");

                for (int register = 0; register < proto.NumParams; register++)
                    declaredRegisters.Add(register);
            }

            for (int pc = 0; pc < proto.Code.Length; pc++)
            {
                int blocksToClose;
                if (closeBlocksAt.TryGetValue(pc, out blocksToClose))
                {
                    for (int i = 0; i < blocksToClose; i++)
                    {
                        indentLevel--;
                        result.Append(new string(' ', indentLevel * 4)).AppendLine("end");
                    }
                }

                uint instruction = proto.Code[pc];
                var op = LuauInsn.OP(instruction);

                uint a = LuauInsn.A(instruction);
                uint b = LuauInsn.B(instruction);
                uint c = LuauInsn.C(instruction);
                int d = LuauInsn.D(instruction);

                uint aux = pc + 1 < proto.Code.Length ? proto.Code[pc + 1] : 0;
                bool consumesAux = false;
                bool opensBlock = false;
                bool suppressStatement = false;
                string statement = null;

                switch (op)
                {
                    case LuauOpcode.LOADNIL:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, "nil", pc);
                        break;
                    }
                    case LuauOpcode.LOADB:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, b == 0 ? "false" : "true", pc);
                        if (c > 0)
                            pc += (int)c;
                        break;
                    }
                    case LuauOpcode.LOADN:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, d.ToString(CultureInfo.InvariantCulture), pc);
                        break;
                    }
                    case LuauOpcode.LOADK:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, Constant(proto, d), pc);
                        break;
                    }
                    case LuauOpcode.LOADKX:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, Constant(proto, (int)aux), pc);
                        consumesAux = true;
                        break;
                    }
                    case LuauOpcode.MOVE:
                    {
                        string target = LocalName(proto, (int)a, pc);
                        string pendingCall;
                        if (pendingCallValues.TryGetValue((int)b, out pendingCall))
                        {
                            expressions[(int)a] = target;
                            pendingCallValues.Remove((int)b);
                            statement = LocalMove(declaredRegisters, options.DeclareRegisters, (int)a, target, pendingCall);
                            break;
                        }

                        string source = Register(expressions, proto, b, pc);
                        expressions[(int)a] = source;
                        if (target == source)
                            suppressStatement = true;
                        else
                            statement = LocalMove(declaredRegisters, options.DeclareRegisters, (int)a, target, source);
                        break;
                    }
                    case LuauOpcode.GETGLOBAL:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, GlobalName(proto, (int)aux), pc);
                        consumesAux = true;
                        break;
                    }
                    case LuauOpcode.GETIMPORT:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, Import(proto, d, aux), pc);
                        consumesAux = true;
                        break;
                    }
                    case LuauOpcode.GETUPVAL:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, Upvalue(proto, b), pc);
                        break;
                    }
                    case LuauOpcode.GETTABLE:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, Register(expressions, proto, b, pc) + "[" + Register(expressions, proto, c, pc) + "]", pc);
                        break;
                    }
                    case LuauOpcode.GETTABLEKS:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, Register(expressions, proto, b, pc) + "." + MemberName(proto, (int)aux), pc);
                        consumesAux = true;
                        break;
                    }
                    case LuauOpcode.NAMECALL:
                    {
                        nameCalls[(int)a] = Register(expressions, proto, b, pc) + ":" + MemberName(proto, (int)aux);
                        suppressStatement = true;
                        consumesAux = true;
                        break;
                    }
                    case LuauOpcode.GETTABLEN:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, Register(expressions, proto, b, pc) + "[" + (c + 1) + "]", pc);
                        break;
                    }
                    case LuauOpcode.NEWTABLE:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, "{}", pc);
                        consumesAux = true;
                        break;
                    }
                    case LuauOpcode.SETLIST:
                    {
                        statement = SetList(proto, expressions, a, b, c, aux, indentLevel, pc);
                        consumesAux = true;
                        break;
                    }
                    case LuauOpcode.DUPTABLE:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, Constant(proto, d), pc);
                        break;
                    }
                    case LuauOpcode.NEWCLOSURE:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, ClosureExpression(proto, d), pc);
                        break;
                    }
                    case LuauOpcode.SETGLOBAL:
                    {
                        statement = GlobalName(proto, (int)aux) + " = " + Register(expressions, proto, a, pc) + ";";
                        consumesAux = true;
                        break;
                    }
                    case LuauOpcode.SETUPVAL:
                    {
                        statement = Upvalue(proto, b) + " = " + Register(expressions, proto, a, pc) + ";";
                        break;
                    }
                    case LuauOpcode.SETTABLE:
                    {
                        statement = Register(expressions, proto, b, pc) + "[" + Register(expressions, proto, c, pc) + "] = " + Register(expressions, proto, a, pc) + ";";
                        break;
                    }
                    case LuauOpcode.SETTABLEN:
                    {
                        statement = Register(expressions, proto, b, pc) + "[" + (c + 1) + "] = " + Register(expressions, proto, a, pc) + ";";
                        break;
                    }
                    case LuauOpcode.SETTABLEKS:
                    {
                        statement = Register(expressions, proto, b, pc) + "." + MemberName(proto, (int)aux) + " = " + Register(expressions, proto, a, pc) + ";";
                        consumesAux = true;
                        break;
                    }
                    case LuauOpcode.NOT:
                    case LuauOpcode.MINUS:
                    case LuauOpcode.LENGTH:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, (op == LuauOpcode.NOT ? "not " : op == LuauOpcode.MINUS ? "-" : "#") + Register(expressions, proto, b, pc), pc);
                        break;
                    }
                    case LuauOpcode.ADD:
                    case LuauOpcode.SUB:
                    case LuauOpcode.MUL:
                    case LuauOpcode.DIV:
                    case LuauOpcode.MOD:
                    case LuauOpcode.POW:
                    case LuauOpcode.IDIV:
                    case LuauOpcode.AND:
                    case LuauOpcode.OR:
                    case LuauOpcode.CONCAT:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, Binary(op, Register(expressions, proto, b, pc), Register(expressions, proto, c, pc)), pc);
                        break;
                    }
                    case LuauOpcode.ADDK:
                    case LuauOpcode.SUBK:
                    case LuauOpcode.MULK:
                    case LuauOpcode.DIVK:
                    case LuauOpcode.MODK:
                    case LuauOpcode.POWK:
                    case LuauOpcode.IDIVK:
                    case LuauOpcode.ANDK:
                    case LuauOpcode.ORK:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, Binary(op, Register(expressions, proto, b, pc), Constant(proto, (int)c)), pc);
                        break;
                    }
                    case LuauOpcode.SUBRK:
                    case LuauOpcode.DIVRK:
                    {
                        string value = Binary(op == LuauOpcode.SUBRK ? LuauOpcode.SUB : LuauOpcode.DIV, Constant(proto, (int)b), Register(expressions, proto, c, pc));
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, value, pc);
                        break;
                    }
                    case LuauOpcode.CALL:
                    case LuauOpcode.CALLFB:
                    {
                        string nameCall;
                        string call;

                        if (nameCalls.TryGetValue((int)a, out nameCall))
                        {
                            call = MethodCall(proto, expressions, nameCall, a, b, pc);
                            nameCalls.Remove((int)a);
                        }
                        else
                        {
                            call = Call(proto, expressions, a, b, c, pc);
                        }

                        statement = CallResult(proto, expressions, a, c, call, pc);
                        if (c == 2 && pc + 1 < proto.Code.Length && LuauInsn.OP(proto.Code[pc + 1]) == LuauOpcode.MOVE && LuauInsn.B(proto.Code[pc + 1]) == a)
                        {
                            pendingCallValues[(int)a] = call;
                            suppressStatement = true;
                        }

                        consumesAux = op == LuauOpcode.CALLFB;
                        break;
                    }
                    case LuauOpcode.GETVARARGS:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, "...", pc);
                        break;
                    }
                    case LuauOpcode.PREPVARARGS:
                    case LuauOpcode.CLOSEUPVALS:
                    {
                        suppressStatement = true;
                        break;
                    }
                    case LuauOpcode.FASTCALL:
                    case LuauOpcode.FASTCALL1:
                    case LuauOpcode.FASTCALL2:
                    case LuauOpcode.FASTCALL2K:
                    case LuauOpcode.FASTCALL3:
                    {
                        suppressStatement = true;
                        consumesAux = op == LuauOpcode.FASTCALL2 || op == LuauOpcode.FASTCALL2K || op == LuauOpcode.FASTCALL3;
                        break;
                    }
                    case LuauOpcode.DUPCLOSURE:
                    {
                        statement = Assign(proto, expressions, declaredRegisters, options.DeclareRegisters, a, DuplicateClosure(proto, d), pc);
                        break;
                    }
                    case LuauOpcode.CAPTURE:
                    {
                        suppressStatement = true;

                        if (options.IncludeInstructionComments)
                            statement = CaptureComment(proto, a, b, pc);

                        break;
                    }
                    case LuauOpcode.RETURN:
                    {
                        statement = "return " + ReturnValues(proto, expressions, a, b, pc) + ";";
                        break;
                    }
                    case LuauOpcode.NOP:
                    case LuauOpcode.BREAK:
                    {
                        suppressStatement = true;

                        if (options.IncludeInstructionComments)
                            statement = "-- " + op;

                        break;
                    }
                    case LuauOpcode.FORGPREP:
                    case LuauOpcode.FORGPREP_NEXT:
                    {
                        int loopInstruction = JumpTarget(op, instruction, pc);

                        int variableCount = op == LuauOpcode.FORGPREP && loopInstruction + 1 < proto.Code.Length
                            ? (int)(proto.Code[loopInstruction + 1] & 0xff)
                            : 2;

                        statement = GenericFor(proto, expressions, a, variableCount, pc);
                        AddBlockToClose(closeBlocksAt, loopInstruction);
                        opensBlock = true;

                        break;
                    }
                    case LuauOpcode.FORNPREP:
                    {
                        int loopInstruction = JumpTarget(op, instruction, pc);
                        statement = NumericFor(proto, expressions, a, pc);
                        AddBlockToClose(closeBlocksAt, loopInstruction);
                        opensBlock = true;
                        break;
                    }
                    case LuauOpcode.FORNLOOP:
                    {
                        suppressStatement = true;
                        break;
                    }
                    case LuauOpcode.FORGLOOP:
                    {
                        suppressStatement = true;
                        consumesAux = true;
                        break;
                    }
                    case LuauOpcode.JUMP:
                    case LuauOpcode.JUMPBACK:
                    case LuauOpcode.JUMPX:
                    {
                        int target = JumpTarget(op, instruction, pc);
                        statement = JumpComment(op, pc, target);
                        break;
                    }
                    case LuauOpcode.JUMPIF:
                    case LuauOpcode.JUMPIFNOT:
                    {
                        int target = JumpTarget(op, instruction, pc);
                        if (target > pc)
                        {
                            statement = "if " + (op == LuauOpcode.JUMPIF ? "not " : "") + Register(expressions, proto, a, pc) + " then";
                            AddBlockToClose(closeBlocksAt, target);
                            opensBlock = true;
                        }
                        else
                            statement = ConditionalJumpComment(op, Register(expressions, proto, a, pc), target);
                        break;
                    }
                    case LuauOpcode.JUMPIFEQ:
                    case LuauOpcode.JUMPIFLE:
                    case LuauOpcode.JUMPIFLT:
                    case LuauOpcode.JUMPIFNOTEQ:
                    case LuauOpcode.JUMPIFNOTLE:
                    case LuauOpcode.JUMPIFNOTLT:
                    {
                        int target = JumpTarget(op, instruction, pc);
                        string condition = RegisterComparison(proto, expressions, op, a, aux, pc);
                        if (target > pc)
                        {
                            statement = "if not (" + condition + ") then";
                            AddBlockToClose(closeBlocksAt, target);
                            opensBlock = true;
                        }
                        else
                            statement = ConditionalJumpComment(op, condition, target);

                        consumesAux = true;
                        break;
                    }
                    case LuauOpcode.JUMPXEQKNIL:
                    case LuauOpcode.JUMPXEQKB:
                    case LuauOpcode.JUMPXEQKN:
                    case LuauOpcode.JUMPXEQKS:
                    {
                        int target = JumpTarget(op, instruction, pc);
                        if (target > pc)
                        {
                            statement = "if not (" + JumpConstantCondition(proto, expressions, op, a, aux, pc) + ") then";
                            AddBlockToClose(closeBlocksAt, target);
                            opensBlock = true;
                        }
                        else
                            statement = ConditionalJumpComment(op, JumpConstantCondition(proto, expressions, op, a, aux, pc), target);
                        consumesAux = true;
                        break;
                    }
                    default:
                    {
                        statement = "-- unsupported " + op + " at pc " + pc;
                        break;
                    }
                }

                if (!suppressStatement)
                {
                    result.Append(new string(' ', indentLevel * 4)).Append(statement);
                    if (options.IncludeInstructionComments)
                        result.Append(" -- pc ").Append(pc).Append(" ").Append(op);
                    result.AppendLine();
                }
                else if (statement != null && options.IncludeInstructionComments)
                {
                    result.Append(new string(' ', indentLevel * 4)).Append(statement).AppendLine();
                }
                if (opensBlock)
                    indentLevel++;
                if (consumesAux)
                    pc++;
            }

            while (indentLevel > 0)
            {
                indentLevel--;
                result.Append(new string(' ', indentLevel * 4)).AppendLine("end");
            }

            activeProtos.Remove(protoId);
            return result.ToString();
        }

        private static string JumpComment(LuauOpcode op, int pc, int target)
        {
            return "-- " + op + " from pc " + pc + " to pc " + target + " requires structured control-flow reconstruction";
        }

        private static string ConditionalJumpComment(LuauOpcode op, string condition, int target)
        {
            return "-- " + op + " if " + condition + " to pc " + target + " requires structured control-flow reconstruction";
        }

        private static void AddBlockToClose(Dictionary<int, int> closeBlocksAt, int pc)
        {
            int count;
            closeBlocksAt.TryGetValue(pc, out count);
            closeBlocksAt[pc] = count + 1;
        }

        private static int JumpTarget(LuauOpcode op, uint instruction, int pc)
        {
            int offset = op == LuauOpcode.JUMPX ? LuauInsn.E(instruction) : LuauInsn.D(instruction);
            return pc + offset + 1;
        }

        private static string GenericFor(LuauProto proto, Dictionary<int, string> expressions, uint register, int variableCount, int pc)
        {
            var result = new StringBuilder("for ");

            for (int i = 0; i < variableCount; i++)
            {
                if (i > 0)
                    result.Append(", ");

                result.Append(LocalName(proto, (int)register + 3 + i, pc));
            }

            result.Append(" in ");
            result.Append(Register(expressions, proto, register, pc));
            result.Append(", ");
            result.Append(Register(expressions, proto, register + 1, pc));
            result.Append(", ");
            result.Append(Register(expressions, proto, register + 2, pc));
            result.AppendLine(" do");
            return result.ToString().TrimEnd('\r', '\n');
        }

        private static string NumericFor(LuauProto proto, Dictionary<int, string> expressions, uint register, int pc)
        {
            string variable = LocalName(proto, (int)register + 3, pc);
            string initial = Register(expressions, proto, register + 2, pc);
            string limit = Register(expressions, proto, register, pc);
            string step = Register(expressions, proto, register + 1, pc);

            return "for " + variable + " = " + initial + ", " + limit + ", " + step + " do";
        }

        private static string SetList(LuauProto proto, Dictionary<int, string> expressions, uint table, uint firstValue, uint valueCount, uint aux, int indentLevel, int pc)
        {
            int count = (int)valueCount - 1;
            if (valueCount == 0)
                return "-- SETLIST " + Register(expressions, proto, table, pc) + " uses all values up to top";

            var result = new StringBuilder();
            int firstIndex = (int)aux;
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    result.AppendLine().Append(new string(' ', indentLevel * 4));

                result.Append(Register(expressions, proto, table, pc));
                result.Append("[").Append(firstIndex + i).Append("] = ");
                result.Append(Register(expressions, proto, firstValue + (uint)i, pc));
                result.Append(";");
            }

            return result.ToString();
        }

        private static string ClosureExpression(LuauProto proto, int childId)
        {
            LuauProto child = proto.Children != null && childId >= 0 && childId < proto.Children.Length
                ? proto.Children[childId]
                : null;
            string parameters = child == null ? "..." : Parameters(child);
            return "function(" + parameters + ") end -- PROTO_" + ProtoId(child);
        }

        private static string DuplicateClosure(LuauProto proto, int constantId)
        {
            if (proto.Consts != null && constantId >= 0 && constantId < proto.Consts.Length)
            {
                LuauConst constant = proto.Consts[constantId];
                if (constant.Type == LuauConstType.CLOSURE && constant.Value is int)
                {
                    int protoId = (int)constant.Value;
                    LuauProto target = proto.Parent != null && proto.Parent.Protos != null && protoId >= 0 && protoId < proto.Parent.Protos.Length
                        ? proto.Parent.Protos[protoId]
                        : null;
                    return "function(" + (target == null ? "..." : Parameters(target)) + ") end -- PROTO_" + protoId;
                }
            }

            return "function(...) end -- DUPCLOSURE_K" + constantId;
        }

        private static string CaptureComment(LuauProto proto, uint captureType, uint source, int pc)
        {
            string sourceName;
            if ((LuauCaptureType)captureType == LuauCaptureType.UPVAL)
                sourceName = Upvalue(proto, source);
            else
                sourceName = LocalName(proto, (int)source, pc);

            return "-- CAPTURE " + (LuauCaptureType)captureType + " " + sourceName;
        }

        private static string Parameters(LuauProto proto)
        {
            var result = new StringBuilder();
            for (int i = 0; i < proto.NumParams; i++)
            {
                if (i > 0)
                    result.Append(", ");

                result.Append(LocalName(proto, i, 0));
            }

            if (proto.IsVarArg)
            {
                if (proto.NumParams > 0)
                    result.Append(", ");

                result.Append("...");
            }

            return result.ToString();
        }

        private static int ProtoId(LuauProto target)
        {
            if (target != null && target.Parent != null && target.Parent.Protos != null)
                for (int i = 0; i < target.Parent.Protos.Length; i++)
                    if (object.ReferenceEquals(target.Parent.Protos[i], target))
                        return i;

            return -1;
        }

        private static string Import(LuauProto proto, int firstConstant, uint aux)
        {
            int count = (int)(aux >> 30);
            var result = new StringBuilder();

            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    result.Append('.');

                int stringId = (int)((aux >> (20 - i * 10)) & 1023);
                result.Append(ImportPart(proto, stringId));
            }

            return result.Length == 0 ? Constant(proto, firstConstant) : result.ToString();
        }

        private static string ImportPart(LuauProto proto, int stringId)
        {
            string value = Constant(proto, stringId);
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                value = value.Substring(1, value.Length - 2);

            if (!value.StartsWith("constant_", StringComparison.Ordinal))
                return SanitizeName(value);

            return "import_" + stringId;
        }

        private static string JumpConstantCondition(LuauProto proto, Dictionary<int, string> expressions, LuauOpcode op, uint register, uint aux, int pc)
        {
            string value = Register(expressions, proto, register, pc);
            string constant;

            if (op == LuauOpcode.JUMPXEQKNIL)
                constant = "nil";
            else if (op == LuauOpcode.JUMPXEQKB)
                constant = LuauInsn.AUX_KB(aux) == 0 ? "false" : "true";
            else
                constant = Constant(proto, (int)LuauInsn.AUX_KV(aux));

            string comparison = value + " == " + constant;
            return LuauInsn.AUX_NOT(aux) != 0 ? "not (" + comparison + ")" : comparison;
        }

        private static string Assign(LuauProto proto, Dictionary<int, string> expressions, HashSet<int> declaredRegisters, bool declareLocals, uint register, string value, int pc)
        {
            string name = LocalName(proto, (int)register, pc);
            bool hasDebugName = name != "r" + register;

            if (!hasDebugName)
            {
                string nextName = LocalName(proto, (int)register, pc + 1);
                if (nextName != "r" + register)
                {
                    name = nextName;
                    hasDebugName = true;
                }
            }

            expressions[(int)register] = !hasDebugName && IsFoldableExpression(value) ? value : name;
            bool firstAssignment = declaredRegisters.Add((int)register);
            return (declareLocals && firstAssignment ? "local " : "") + name + " = " + value + ";";
        }

        private static string LocalMove(HashSet<int> declaredRegisters, bool declareLocals, int register, string target, string source)
        {
            bool firstAssignment = declaredRegisters.Add(register);
            return (declareLocals && firstAssignment ? "local " : "") + target + " = " + source + ";";
        }

        private static bool IsFoldableExpression(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            if (value.IndexOf('(') >= 0 || value.IndexOf(')') >= 0 || value.IndexOf('{') >= 0 || value.IndexOf('}') >= 0 || value.IndexOf('[') >= 0 || value.IndexOf(']') >= 0 || value.IndexOf('.') >= 0)
                return false;

            if (value.IndexOf(":") >= 0 || value == "...")
                return false;

            return true;
        }

        private static string LocalDeclarations(LuauProto proto)
        {
            var names = new List<string>();
            var seen = new HashSet<string>();

            for (int register = proto.NumParams; register < proto.MaxStackSize; register++)
            {
                string name = LocalName(proto, register, 0);
                if (seen.Add(name))
                    names.Add(name);
            }

            return string.Join(", ", names.ToArray());
        }

        private static string Register(Dictionary<int, string> expressions, LuauProto proto, uint register, int pc)
        {
            string value;

            if (expressions.TryGetValue((int)register, out value))
                return value;

            return LocalName(proto, (int)register, pc);
        }

        private static string LocalName(LuauProto proto, int register, int pc)
        {
            if (proto.LocVars != null)
                foreach (LuauLocVar local in proto.LocVars)
                    if (local.Register == register && pc >= local.StartPoint && pc < local.EndPoint)
                        return SanitizeName(local.VarName);

            return "r" + register;
        }

        private static string Upvalue(LuauProto proto, uint index)
        {
            if (proto.Upvalues != null && index < proto.Upvalues.Length)
                return SanitizeName(proto.Upvalues[index]);

            return "upvalue_" + index;
        }

        private static string Constant(LuauProto proto, int index)
        {
            if (proto.Consts == null || index < 0 || index >= proto.Consts.Length)
                return "nil -- invalid constant " + index;

            var constant = proto.Consts[index];

            if (constant.Type == LuauConstType.NIL)
                return "nil";
            else if (constant.Type == LuauConstType.BOOLEAN)
                return (bool)constant.Value ? "true" : "false";
            else if (constant.Type == LuauConstType.STRING)
                return Quote((string)constant.Value);
            else if (constant.Type == LuauConstType.NUMBER)
                return ((double)constant.Value).ToString("R", CultureInfo.InvariantCulture);
            else if (constant.Type == LuauConstType.INTEGER)
                return ((long)constant.Value).ToString(CultureInfo.InvariantCulture);

            return "constant_" + index;
        }

        private static string GlobalName(LuauProto proto, int index)
        {
            if (proto.Consts == null || index < 0 || index >= proto.Consts.Length || proto.Consts[index].Type != LuauConstType.STRING)
                return Constant(proto, index);

            string name = (string)proto.Consts[index].Value;
            if (!IsIdentifier(name) || IsKeyword(name))
                return "_G[" + Quote(name) + "]";

            return name;
        }

        private static bool IsIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || !((value[0] >= 'a' && value[0] <= 'z') || (value[0] >= 'A' && value[0] <= 'Z') || value[0] == '_'))
                return false;

            for (int i = 1; i < value.Length; i++)
            {
                char character = value[i];
                if (!((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9') || character == '_'))
                    return false;
            }

            return true;
        }

        private static bool IsKeyword(string value)
        {
            switch (value)
            {
                case "and":
                case "break":
                case "continue":
                case "do":
                case "else":
                case "elseif":
                case "end":
                case "export":
                case "false":
                case "for":
                case "function":
                case "if":
                case "in":
                case "local":
                case "nil":
                case "not":
                case "or":
                case "repeat":
                case "return":
                case "then":
                case "true":
                case "until":
                case "while":
                    return true;
                default:
                    return false;
            }
        }

        private static string MemberName(LuauProto proto, int index)
        {
            string value = Constant(proto, index);
            return value.Length > 1 && value[0] == '"' ? value.Substring(1, value.Length - 2) : "member_" + index;
        }

        private static string Binary(LuauOpcode op, string left, string right)
        {
            switch (op)
            {
                case LuauOpcode.ADDK: op = LuauOpcode.ADD; break;
                case LuauOpcode.SUBK: op = LuauOpcode.SUB; break;
                case LuauOpcode.MULK: op = LuauOpcode.MUL; break;
                case LuauOpcode.DIVK: op = LuauOpcode.DIV; break;
                case LuauOpcode.MODK: op = LuauOpcode.MOD; break;
                case LuauOpcode.POWK: op = LuauOpcode.POW; break;
                case LuauOpcode.IDIVK: op = LuauOpcode.IDIV; break;
                case LuauOpcode.ANDK: op = LuauOpcode.AND; break;
                case LuauOpcode.ORK: op = LuauOpcode.OR; break;
                case LuauOpcode.SUBRK: op = LuauOpcode.SUB; break;
                case LuauOpcode.DIVRK: op = LuauOpcode.DIV; break;
            }

            string symbol = op == LuauOpcode.ADD ? "+" : op == LuauOpcode.SUB ? "-" : op == LuauOpcode.MUL ? "*" : op == LuauOpcode.DIV ? "/" : op == LuauOpcode.MOD ? "%" : op == LuauOpcode.POW ? "^" : op == LuauOpcode.IDIV ? "//" : op == LuauOpcode.AND ? "and" : op == LuauOpcode.OR ? "or" : "..";
            return "(" + left + " " + symbol + " " + right + ")";
        }

        private static string Call(LuauProto proto, Dictionary<int, string> expressions, uint register, uint arguments, uint results, int pc)
        {
            var value = new StringBuilder(Register(expressions, proto, register, pc)).Append("(");
            int count = (int)arguments - 1;

            for (int i = 1; i <= count; i++)
            {
                if (i > 1)
                    value.Append(", ");

                value.Append(Register(expressions, proto, register + (uint)i, pc));
            }

            return value.Append(")").ToString();
        }

        private static string MethodCall(LuauProto proto, Dictionary<int, string> expressions, string method, uint register, uint arguments, int pc)
        {
            var value = new StringBuilder(method).Append("(");
            int count = arguments == 0 ? 0 : (int)arguments - 1;

            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    value.Append(", ");

                value.Append(Register(expressions, proto, register + 2 + (uint)i, pc));
            }

            return value.Append(")").ToString();
        }

        private static string CallResult(LuauProto proto, Dictionary<int, string> expressions, uint register, uint results, string call, int pc)
        {
            if (results <= 1)
                return call + ";";

            var value = new StringBuilder();
            int count = (int)results - 1;
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    value.Append(", ");

                value.Append(LocalName(proto, (int)register + i, pc));
                expressions[(int)register + i] = LocalName(proto, (int)register + i, pc);
            }

            return value.Append(" = ").Append(call).Append(";").ToString();
        }

        private static string RegisterComparison(LuauProto proto, Dictionary<int, string> expressions, LuauOpcode op, uint leftRegister, uint aux, int pc)
        {
            string left = Register(expressions, proto, leftRegister, pc);
            string right = Register(expressions, proto, aux, pc);
            string symbol = op == LuauOpcode.JUMPIFEQ || op == LuauOpcode.JUMPIFNOTEQ ? "==" : op == LuauOpcode.JUMPIFLE || op == LuauOpcode.JUMPIFNOTLE ? "<=" : "<";
            string condition = left + " " + symbol + " " + right;
            bool negate = op == LuauOpcode.JUMPIFNOTEQ || op == LuauOpcode.JUMPIFNOTLE || op == LuauOpcode.JUMPIFNOTLT;
            return negate ? "not (" + condition + ")" : condition;
        }

        private static string ReturnValues(LuauProto proto, Dictionary<int, string> expressions, uint register, uint count, int pc)
        {
            int valueCount = (int)count - 1;

            if (valueCount <= 0)
                return "";

            var result = new StringBuilder();

            for (int i = 0; i < valueCount; i++)
            {
                if (i > 0)
                    result.Append(", ");

                result.Append(Register(expressions, proto, register + (uint)i, pc));
            }

            return result.ToString();
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "NULL")
                return "anonymous";

            var result = new StringBuilder();

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];

                if ((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') || character == '_' || (i > 0 && character >= '0' && character <= '9'))
                    result.Append(character);
                else
                    result.Append('_');

            }

            return result.Length == 0 ? "anonymous" : result.ToString();
        }
    }
}
