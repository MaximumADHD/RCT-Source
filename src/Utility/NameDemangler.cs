using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RobloxClientTracker
{
    public class NameDemangler
    {
        public string Name { get; private set; }

        public bool HasElements => (Tree.Count > 0);
        private Dictionary<string, NameDemangler> Tree;
        

        public NameDemangler(string name)
        {
            Name = name;
            Tree = new Dictionary<string, NameDemangler>();
        }

        public NameDemangler Add(string name)
        {
            if (!Tree.ContainsKey(name))
            {
                NameDemangler branch = new NameDemangler(name);
                Tree.Add(name, branch);
            }

            return Tree[name];
        }

        public static int Compare(NameDemangler a, NameDemangler b)
        {
            int sizeofA = a.GetBranches().Count;
            int sizeofB = b.GetBranches().Count;

            int result = 0;

            if (sizeofA == 0 && sizeofB > 0)
                result = 1;
            else if (sizeofB == 0 && sizeofA > 0)
                result = - 1;
            else
                result = string.Compare(a.Name, b.Name);

            return result;
        }

        public List<NameDemangler> GetBranches()
        {
            return Tree.Values
                .Where(branch => !branch.IsBad())
                .ToList();
        }

        public bool IsBad()
        {
            int test;
            return int.TryParse(Name, out test);
        }

        public void WriteTree(StringBuilder builder, int stack = 0)
        {
            string tabStack = "";

            for (int i = 0; i < stack; i++)
                tabStack += '\t';

            builder.AppendLine(tabStack + Name);

            if (HasElements)
            {
                builder.AppendLine(tabStack + "{");

                List<NameDemangler> branches = GetBranches();
                branches.Sort(Compare);

                foreach (NameDemangler branch in branches)
                    branch.WriteTree(builder, stack + 1);

                builder.AppendLine(tabStack + "}");
            }
        }
    }
}
