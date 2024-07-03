using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenerateEncodingsMap
{
    public class MappedEncoding
    {
        public string name { get; set; }
        public List<string> aliases { get; set; }
        public string dotnet_name { get; set; }
        public List<UInt32> codePoints { get; set; }
        public List<UInt32[]> bytesForCodePoints { get; set; }
    }
}
