using Archer.Archetype;

namespace Archer.Core.Prototypes
{
    public class PushProvider : IDataServiceProvider
    {
        public string Connection { get; set; }
        public string[] Subjects { get; set; }
        public string Protocol { get; set; }
    }
}