using System;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace RobloxClientTracker.Luau
{
    public class LuauConst
    {
        public LuauProto Proto;
        public LuauConstType Type;
        public object Value;

        public override string ToString()
        {
            string result = $"";

            switch (Type)
            {
                case LuauConstType.NIL:
                {
                    break;
                }
                case LuauConstType.BOOLEAN:
                case LuauConstType.NUMBER:
                {
                    result += Value.ToString();
                    break;
                }
                case LuauConstType.STRING:
                {
                    var value = Value.ToString();
                    var builder = new StringBuilder();

                    foreach (char c in value)
                    {
                        switch (c)
                        {
                            case '"':
                            {
                                builder.Append("\\\"");
                                break;
                            }
                            case '\\':
                            {
                                builder.Append("\\\\");
                                break;
                            }
                            case '\n':
                            {
                                builder.Append("\\n");
                                break;
                            }
                            case '\r':
                            {
                                builder.Append("\\r");
                                break;
                            }
                            case '\t':
                            {
                                builder.Append("\\t");
                                break;
                            }
                            case '\0':
                            {
                                builder.Append("\\0");
                                break;
                            }
                            default:
                            {
                                if (c < 32)
                                    builder.AppendFormat("\\x{0:X}", c);
                                else
                                    builder.Append(c);

                                break;
                            }
                        }
                    }

                    result = $"\"{builder}\"";
                    break;
                }
                case LuauConstType.TABLE:
                {
                    if (Value is Array array)
                    {
                        string[] constants = array.Cast<int>()
                            .Select(index => Proto.Consts[index].ToString())
                            .ToArray();

                        result += $"{{{string.Join(", ", constants)}}}";
                    }

                    break;
                }
                case LuauConstType.VECTOR:
                {
                    if (Value is Array array)
                    {
                        float[] vec = array
                            .Cast<float>()
                            .ToArray();

                        result += $"{{{string.Join(", ", vec)}}}";
                    }
                    
                    break;
                }
                case LuauConstType.CLOSURE:
                {
                    result += $"PROTO_{Value}";
                    break;
                }
                case LuauConstType.IMPORT:
                {
                    if (Value is uint ids)
                    {
                        var set = LuauInsn.ReadImportIds(ids)
                            .Select(id => Proto.Consts[id].Value)
                            .ToArray();

                        result += $"{string.Join(".", set)}";
                    }

                    break;
                }
            }

            return result.Trim();
        }
    }
}
