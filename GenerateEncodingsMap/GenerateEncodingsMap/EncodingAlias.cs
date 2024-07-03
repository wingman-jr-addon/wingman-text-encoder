using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenerateEncodingsMap
{
    public class EncodingAlias
    {
        public string CanonicalName { get; set; }
        public HashSet<string> Aliases { get; set; }

        public EncodingAlias(string canonicalName, IEnumerable<string> aliases)
        {
            CanonicalName = canonicalName;
            Aliases = new HashSet<string>(aliases);
        }
    }
}
