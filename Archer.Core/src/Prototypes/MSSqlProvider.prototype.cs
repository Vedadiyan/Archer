using System.Collections.Generic;
using Archer.Archetype;

namespace Archer.Core.Prototypes
{
    public class MSSqlProvider : IDataServiceProvider
    {
        public string ConnectionString { get; set; }
        public string Command { get; set; }
        public string CommandType { get; set; }
        public bool UseBody { get; set; }
        public Dictionary<string, string> Parameters { get; set; }
    }
}