using Archer.Archetype;

namespace Archer.Core.Prototypes
{
    public class MongoDbProvider : IDataServiceProvider
    {
        public string ConnectionString { get; set; }
        public string Database { get; set; }
        public string Collection { get; set; }
        public string Query { get; set; }
        public bool UseBody { get; set; }
    }
}