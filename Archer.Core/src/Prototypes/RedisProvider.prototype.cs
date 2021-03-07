using Archer.Archetype;

namespace Archer.Core.Prototypes
{
    public class RedisProvider : IDataServiceProvider
    {
        public string ConnectionString { get; set; }
        public int Database { get; set; }
        public string Key { get; set; }
        public bool UseBody { get; set; }
        public TaskProvider TaskProvider { get; set; }
        
    }
}