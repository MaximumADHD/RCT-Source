using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RobloxClientTracker
{
    public class NameTree : Dictionary<string, NameTree>
    {
        public string Name { get; private set; }

        public bool Invalid
        {
            get
            {
                int test;
                return int.TryParse(Name, out test);
            }
        }
        
        public NameTree(string name)
        {
            Name = name;
        }

        public NameTree Add(string name)
        {
            if (!ContainsKey(name))
            {
                NameTree branch = new NameTree(name);
                Add(name, branch);
            }

            return this[name];
        }

        public static int Compare(NameTree a, NameTree b)
        {
            int sizeofA = a.GetBranches().Count;
            int sizeofB = b.GetBranches().Count;

            int result = 0;

            if (sizeofA == 0 && sizeofB > 0)
                result = 1;
            else if (sizeofB == 0 && sizeofA > 0)
                result = -1;
            else
                result = string.Compare(a.Name, b.Name);

            return result;
        }

        public List<NameTree> GetBranches()
        {
            var query = Values.Where(branch => !branch.Invalid);
            return query.ToList();
        }

        private void WriteTreeImpl(StringBuilder builder, string prefix)
        {
            builder.AppendLine(prefix + Name);

            if (Count > 0)
            {
                builder.AppendLine(prefix + '{');

                var branches = GetBranches();
                branches.Sort(Compare);

                foreach (NameTree branch in branches)
                    branch.WriteTreeImpl(builder, '\t' + prefix);

                builder.AppendLine(prefix + '}');
            }
        }
        
        public string WriteTree()
        {
            var builder = new StringBuilder();
            WriteTreeImpl(builder, "");

            return builder.ToString();
        }
    }
}
