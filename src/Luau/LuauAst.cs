using RobloxFiles.BinaryFormat.Chunks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text.RegularExpressions;

namespace RobloxClientTracker.Luau
{
    public class AstExpression
    {
        private AstExpression _parent;
        protected List<AstExpression> _children = new List<AstExpression>();

        public bool IsAncestorOf(AstExpression desc)
        {
            var at = desc;

            while (at != null)
            {
                if (at == this)
                    return true;

                at = at.Parent;
            }

            return false;
        }

        public bool IsDescendantOf(AstExpression ancestor)
        {
            return ancestor?.IsAncestorOf(this) ?? false;
        }

        public AstExpression Parent
        {
            get => _parent;

            set
            {
                if (value == this)
                    throw new InvalidOperationException("Cannot parent to self.");

                if (IsAncestorOf(value))
                    throw new InvalidOperationException("New parent would be cyclic.");

                _parent?._children.Remove(this);
                value._children.Add(this);
                _parent = value;
            }
        }

        public IEnumerable<AstExpression> GetChildren()
        {
            return _children.AsEnumerable();
        }
    }

    public class AstVariable : AstExpression
    {
        public string Label = "";

        public AstVariable(string label)
        {
            Label = label;
        }

        public override string ToString()
        {
            return Label;
        }
    }

    public class AstBoolean : AstExpression
    {
        public bool Value;
        
        public AstBoolean(bool value)
        {
            Value = value;
        }

        public override string ToString()
        {
            return Value.ToString().ToLowerInvariant();
        }
    }

    public class AstNumber : AstExpression
    {
        public double Value;

        public AstNumber(double value)
        {
            Value = value;
        }

        public override string ToString()
        {
            return Value.ToString();
        }
    }

    public class AstChain : AstExpression
    {
        public string Symbol;
        public AstExpression Left;
        public AstExpression Right;

        public override string ToString()
        {
            return $"{Left}{Symbol}{Right}";
        }
    }

    public class AstTableIndex : AstExpression
    {
        public AstExpression Left;
        public AstExpression Right;

        public override string ToString()
        {
            string right = Right.ToString();

            if (Regex.IsMatch(right, "^[A-z][A-z0-9_]*$"))
                return $"{Left}.{Right}";

            return $"{Left}[{Right}]";
        }
    }


    public class AstConst : AstExpression
    {
        public LuauConst Value;

        public AstConst(LuauConst value)
        {
            Value = value;
        }

        public override string ToString()
        {
            return base.ToString();
        }
    }

    public class AstLocal : AstExpression
    {
        public AstExpression Exp;

        public AstLocal(AstExpression exp)
        {
            Exp = exp;
            exp.Parent = this;
        }

        public override string ToString()
        {
            return $"local {Exp}";
        }
    }

    public class AstTable : AstExpression
    {
        public LuauConst Source;
        public Dictionary<AstExpression, AstExpression> Data;

        public AstTable(LuauConst source = null)
        {
            Source = source;
            Data = new Dictionary<AstExpression, AstExpression>();
        }
    }

    public class AstFunction : AstExpression
    {
        public LuauProto Source;
        public AstExpression[] Registers = new AstExpression[255];

        public AstFunction(LuauProto source)
        {
            Source = source;
        }

        public void Evaluate()
        {
            var expressions = new List<AstExpression>();
            var codes = Source.Code;

            LuauOpcode op;
            var insn = 0u;
            var aux = 0u;

            Func<uint> A = () => LuauInsn.A(insn);
            Func<uint> B = () => LuauInsn.B(insn);
            Func<uint> C = () => LuauInsn.C(insn);

            Func<int> D = () => LuauInsn.D(insn);
            Func<int> E = () => LuauInsn.E(insn);

            for (int code = 0; code < codes.Length; code++)
            {
                insn = codes[code];
                op = LuauInsn.OP(insn);
                aux = 0u;

                if (code + 1 < codes.Length)
                    aux = codes[code + 1];

                switch (op)
                {
                    case LuauOpcode.LOADNIL:
                    {
                        Registers[A()] = null;
                        break;
                    }
                    case LuauOpcode.LOADB:
                    {
                        var boolean = new AstBoolean(B() > 0);
                        Registers[A()] = boolean;

                        expressions.Add(boolean);
                        code += (int)C();

                        break;
                    }
                    case LuauOpcode.LOADN:
                    {
                        var number = new AstNumber(D());
                        Registers[A()] = number;

                        expressions.Add(number);
                        break;
                    }
                    case LuauOpcode.LOADK:
                    {
                        var constant = new AstConst(Source.Consts[D()]);
                        Registers[A()] = constant;

                        expressions.Add(constant);
                        break;
                    }
                    case LuauOpcode.MOVE:
                    {
                        Registers[A()] = Registers[B()];
                        break;
                    }
                    case LuauOpcode.GETGLOBAL:
                    {
                        var constant = new AstConst(Source.Consts[aux]);
                        Registers[A()] = constant;

                        expressions.Add(constant);
                        code++; // AUX

                        break;
                    }
                    case LuauOpcode.SETGLOBAL:
                    {
                        var constant = new AstConst(Source.Consts[aux]);
                        var value = Registers[A()];

                        var assign = new AstChain()
                        {
                            Left = constant,
                            Right = value,
                            Symbol = " = ",
                        };

                        expressions.Add(assign);
                        code++; // AUX

                        break;
                    }
                    case LuauOpcode.GETUPVAL:
                    case LuauOpcode.SETUPVAL:
                    {
                        // TODO!
                        break;
                    }
                    case LuauOpcode.GETIMPORT:
                    {
                        var constant = Source.Consts[D()];
                        var import = new AstVariable(constant.ToString());

                        Registers[A()] = import;
                        expressions.Add(import);

                        code++; // AUX
                        break;
                    }
                    case LuauOpcode.GETTABLE:
                    case LuauOpcode.SETTABLE:
                    {
                        // TODO!
                        break;
                    }
                    case LuauOpcode.GETTABLEKS:
                    {
                        var table = Registers[B()];
                        var constant = new AstConst(Source.Consts[aux]);

                        var index = new AstChain()
                        {
                            Left = table,
                            Right = constant,
                            Symbol = ".",
                        };

                        Registers[A()] = index;
                        expressions.Add(index);

                        code++; // AUX
                        break;
                    }
                }
            }
        }
    }
}
