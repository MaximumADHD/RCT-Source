using System;
using System.Diagnostics;
using System.Linq;

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
                    result += $"\"{Value.ToString().Replace("\"", "\\\"")}\"";
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
