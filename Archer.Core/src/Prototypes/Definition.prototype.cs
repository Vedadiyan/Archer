using System.Collections.Generic;
using Archer.Archetype;

namespace Archer.Core.Prototypes
{
    public class Definition
    {
        public string RouteTemplate { get; set; }
        public string Authentication { get; set; }
        public string Logs { get; set; }
        public LogLevel LogLevel { get; set; }
        public string Method { get; set; }
        public Dictionary<int, string> Groups { get; set; }
        public Dictionary<string, string> Map { get; set; }
        public List<string> Exclude { get; set; }
        public IDataServiceProvider DataServiceProvider { get; set; }
        public bool IsWrapped { get; set; }
        public bool IsCamelCase { get; set; }
    }
    public enum LogLevel
    {
        Verbose = 5,
        Debug = 4,
        Information = 3,
        Warning = 2,
        Exception = 1
    }
}