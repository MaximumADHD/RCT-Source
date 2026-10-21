using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace RobloxClientTracker.Luau
{
    public class LuauLocVar
    {
        public string VarName;
        public int StartPoint;
        public int EndPoint;
        public byte Register;
    }

    public class LuauClassShape
    {
        public int ClassName;
        public int[] PropertyNames;
        public int[] MethodNames;
    }

    public class LuauDisassembly
    {
        public readonly string[] Strings;
        public readonly int MainId;

        public readonly LuauProto[] Protos;
        public LuauProto Main => Protos[MainId];

        public LuauDisassembly(byte[] buffer)
        {
            var stream = new MemoryStream(buffer);
            var reader = new BinaryReader(stream);

            Func<int> readVarInt = () =>
            {
                int result = 0;
                int shift = 0;
                byte b;

                do
                {
                    b = reader.ReadByte();
                    result |= (b & 127) << shift;
                    shift += 7;
                } while ((b & 128) > 0);

                return result;
            };

            Func<ulong> readVarInt64 = () =>
            {
                ulong result = 0;
                int shift = 0;
                byte b;

                do
                {
                    b = reader.ReadByte();
                    result |= (ulong)(b & 127) << shift;
                    shift += 7;
                } while ((b & 128) > 0);

                return result;
            };

            Func<string> readString = () =>
            {
                int id = readVarInt();
                return id == 0 ? "NULL" : Strings[id - 1];
            };

            var version = reader.ReadByte();

            if (version == 0)
                throw new Exception("Ill-formatted Luau.");
            else if (version < 3 || version > 11)
                throw new Exception($"Bytecode version mismatch (expected [3..11], got {version})");

            var typesVersion = 0;

            if (version >= 4)
                typesVersion = reader.ReadByte();

            var stringCount = readVarInt();
            Strings = new string[stringCount];

            for (var i = 0; i < stringCount; i++)
            {
                var length = readVarInt();
                byte[] str = reader.ReadBytes(length);
                Strings[i] = Encoding.UTF8.GetString(str);
            }

            if (typesVersion == 3)
            {
                // userdata type name mapping: pairs of (1-based index, name string-table varint), terminated by 0
                byte index = reader.ReadByte();

                while (index != 0)
                {
                    readVarInt(); // name reference into string table
                    index = reader.ReadByte();
                }
            }

            var protoCount = readVarInt();
            Protos = new LuauProto[protoCount];

            for (var i = 0; i < protoCount; i++)
            {
                var proto = new LuauProto();
                proto.Parent = this;
                proto.MaxStackSize = reader.ReadByte();
                proto.NumParams = reader.ReadByte();
                proto.NumUpvalues = reader.ReadByte();
                proto.IsVarArg = reader.ReadByte() > 0;

                if (version >= 4)
                {
                    proto.Flags = (LuauProtoFlags)reader.ReadByte();

                    if (typesVersion == 1)
                    {
                        var typeSize = readVarInt();

                        if (typeSize > 0)
                        {
                            var headerSize = typeSize > 127 ? 4 : 3;
                            byte[] types = reader.ReadBytes(typeSize);

                            if (headerSize == 4)
                            {
                                types[0] = (byte)((typeSize & 127) | (1 << 7));
                                types[1] = (byte)(typeSize >> 7);
                                types[2] = 0;
                                types[3] = 0;
                            }
                            else
                            {
                                types[0] = (byte)typeSize;
                                types[1] = 0;
                                types[2] = 0;
                            }

                            proto.TypeInfo = types;
                        }
                    }
                    else if (typesVersion == 2 || typesVersion == 3)
                    {
                        var typeSize = readVarInt();

                        if (typeSize > 0)
                        {
                            proto.TypeInfo = reader.ReadBytes(typeSize);
                        }
                    }
                }

                var numCode = readVarInt();
                proto.Code = new uint[numCode];

                for (int j = 0; j < numCode; j++)
                    proto.Code[j] = reader.ReadUInt32();

                var numConstants = readVarInt();
                proto.Consts = new LuauConst[numConstants];

                for (int j = 0; j < numConstants; j++)
                {
                    var constant = new LuauConst();
                    constant.Type = (LuauConstType)reader.ReadByte();
                    constant.Proto = proto;

                    switch (constant.Type)
                    {
                        case LuauConstType.NIL:
                        {
                            // nothing to do.
                            break;
                        }
                        case LuauConstType.BOOLEAN:
                        {
                            constant.Value = reader.ReadBoolean();
                            break;
                        }
                        case LuauConstType.NUMBER:
                        {
                            constant.Value = reader.ReadDouble();
                            break;
                        }
                        case LuauConstType.STRING:
                        {
                            constant.Value = readString();
                            break;
                        }
                        case LuauConstType.IMPORT:
                        {
                            constant.Value = reader.ReadUInt32();
                            break;
                        }
                        case LuauConstType.TABLE:
                        {
                            int size = readVarInt();
                            var tbl = new int[size];

                            for (int k = 0; k < size; k++)
                                tbl[k] = readVarInt();

                            constant.Value = tbl;
                            break;
                        }
                        case LuauConstType.CLOSURE:
                        {
                            constant.Value = readVarInt();
                            break;
                        }
                        case LuauConstType.VECTOR:
                        {
                            float x = reader.ReadSingle(),
                                  y = reader.ReadSingle(),
                                  z = reader.ReadSingle(),
                                  w = reader.ReadSingle();

                            if (w == 0f)
                                constant.Value = new float[3] { x, y, z };
                            else
                                constant.Value = new float[4] { x, y, z, w };

                            break;
                        }
                        case LuauConstType.TABLE_WITH_CONSTANTS:
                        {
                            // Pairs of (key varint, value constant index as raw int32).
                            int size = readVarInt();
                            var tbl = new int[size * 2];

                            for (int k = 0; k < size; k++)
                            {
                                tbl[k * 2] = readVarInt();
                                tbl[k * 2 + 1] = reader.ReadInt32();
                            }

                            constant.Value = tbl;
                            break;
                        }
                        case LuauConstType.INTEGER:
                        {
                            // sign byte followed by varint absolute value; negative is encoded as ~v + 1
                            byte sign = reader.ReadByte();
                            ulong mag = readVarInt64();
                            long value = sign != 0 ? -(long)mag : (long)mag;
                            constant.Value = value;
                            break;
                        }
                        case LuauConstType.CLASS_SHAPE:
                        {
                            // className varint, propertyNames count varint, methodNames count varint, then names
                            int className = readVarInt();
                            int propCount = readVarInt();
                            int methodCount = readVarInt();

                            var props = new int[propCount];
                            for (int k = 0; k < propCount; k++)
                                props[k] = readVarInt();

                            var methods = new int[methodCount];
                            for (int k = 0; k < methodCount; k++)
                                methods[k] = readVarInt();

                            constant.Value = new LuauClassShape
                            {
                                ClassName = className,
                                PropertyNames = props,
                                MethodNames = methods,
                            };
                            break;
                        }
                        default:
                        {
                            Debug.Assert(false, $"Unexpected constant kind {constant.Type}");
                            break;
                        }
                    }

                    proto.Consts[j] = constant;
                }

                var numChildren = readVarInt();
                proto.Children = new LuauProto[numChildren];

                for (int j = 0; j < numChildren; j++)
                {
                    var fid = readVarInt();
                    proto.Children[j] = Protos[fid];
                }

                proto.LineDefined = readVarInt();
                proto.DebugName = readString();

                // Line Info
                if (reader.ReadByte() > 0)
                {
                    var lineGapLog2 = reader.ReadByte();
                    proto.LineGapLog2 = lineGapLog2;

                    int intervals = ((numCode - 1) >> lineGapLog2) + 1;
                    int absoffset = (numCode + 3) & ~3;

                    int sizeLineInfo = absoffset + intervals * sizeof(int);
                    proto.LineInfo = new byte[sizeLineInfo];

                    byte lastOffset = 0;
                    int lastLine = 0;

                    for (int j = 0; j < numCode; j++)
                    {
                        lastOffset += reader.ReadByte();
                        proto.LineInfo[j] = lastOffset;
                    }

                    for (int j = 0; j < intervals; j++)
                    {
                        var value = reader.ReadInt32();
                        lastLine += value;

                        var bytes = BitConverter.GetBytes(value);
                        var index = absoffset + (j * 4);

                        for (int k = 0; k < 4; k++)
                        {
                            byte b = bytes[k];
                            proto.LineInfo[index + k] = b;
                        }
                    }
                }

                // Debug Info
                if (reader.ReadByte() > 0)
                {
                    var numLocVars = readVarInt();
                    proto.LocVars = new LuauLocVar[numLocVars];

                    for (int j = 0; j < numLocVars; j++)
                    {
                        var locvar = new LuauLocVar();
                        locvar.VarName = readString();
                        locvar.StartPoint = readVarInt();
                        locvar.EndPoint = readVarInt();
                        locvar.Register = reader.ReadByte();
                        proto.LocVars[j] = locvar;
                    }

                    var sizeUpvalues = readVarInt();
                    proto.Upvalues = new string[sizeUpvalues];

                    for (int j = 0; j < sizeUpvalues; j++)
                    {
                        string str = readString();
                        proto.Upvalues[j] = str;
                    }
                }

                // Feedback slots (LuauEmitCallFeedback, bytecode version >= 11)
                if (version >= 11)
                {
                    int fbCount = readVarInt();

                    for (int j = 0; j < fbCount; j++)
                    {
                        reader.ReadByte(); // LuauFeedbackType (currently only CALLTARGET = 0)
                        readVarInt();      // pc of the feedback instruction
                    }
                }

                Protos[i] = proto;
            }

            MainId = readVarInt();
            reader.Dispose();
            stream.Dispose();
        }

        public string BuildDisassembly()
        {
            var builder = new StringBuilder();

            for (int i = 0; i < Protos.Length; i++)
            {
                var proto = Protos[i];

                if (i == MainId)
                    builder.AppendLine("MAIN:");
                else
                    builder.AppendLine($"PROTO_{i}:");

                foreach (var insn in proto.Disassembly)
                    builder.AppendLine($"  {insn}");

                if (i + 1 == Protos.Length)
                    break;

                builder.AppendLine();
            }

            return builder.ToString();
        }
    }
}
