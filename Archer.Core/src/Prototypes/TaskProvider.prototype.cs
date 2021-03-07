using System;
using Archer.Archetype;

namespace Archer.Core.Prototypes
{
    public class TaskProvider
    {
        public TimeSpan Interval { get; set; }
        public IDataServiceProvider DataServiceProvider { get; set; }
    }
}