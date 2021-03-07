using System.Collections.Generic;
using Archer.Archetype;

namespace Archer.Core.Prototypes
{
    public class RouteProvider : IDataServiceProvider
    {
        public string Url { get; set; }
        public Dictionary<string, string> Headers { get; set; }
        public string Method { get; set; }
        public string BodyTemplate { get; set; }
        public bool UseBody { get; set; }
    }
}