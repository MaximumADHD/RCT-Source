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

            Func<string> readString = () =>
            {
                int id = readVarInt();
                return id == 0 ? "NULL" : Strings[id - 1];
            };

            var version = reader.ReadByte();

            if (version == 0)
                throw new Exception("Ill-formatted Luau.");
            else if (version < 3 || version > 5)
                throw new Exception($"Bytecode version mismatch (expected [3..5], got {version})");

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

            var protoCount = readVarInt();
            Protos = new LuauProto[protoCount];

            for (var i = 0; i < protoCount; i++)
            {
                var proto = new LuauProto();
                proto.MaxStackSize = reader.ReadByte();
                proto.NumParams = reader.ReadByte();
                proto.NumUpvalues = reader.ReadByte();
                proto.IsVarArg = reader.ReadByte() > 0;

                if (version >= 4)
                {
                    proto.Flags = (LuauProtoFlags)reader.ReadByte();
                    var numTypes = readVarInt();

                    if (numTypes > 0 && typesVersion == 1)
                    {
                        byte[] types = reader.ReadBytes(numTypes);
                        proto.TypeInfo = types;
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
                        default:
                        {
                            Debug.Assert(false, "Unexpected constant kind");
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
                        var locvar = proto.LocVars[j];
                        locvar.VarName = readString();
                        locvar.StartPoint = readVarInt();
                        locvar.EndPoint = readVarInt();
                        locvar.Register = reader.ReadByte();
                    }

                    var sizeUpvalues = readVarInt();
                    proto.Upvalues = new string[sizeUpvalues];

                    for (int j = 0; j < sizeUpvalues; j++)
                    {
                        string str = readString();
                        proto.Upvalues[j] = str;
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
