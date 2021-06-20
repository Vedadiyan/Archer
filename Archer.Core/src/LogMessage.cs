using System;
using System.Diagnostics;
using Spider.Extensions.Logging.Abstraction;

namespace Archer.Core
{
    public class LogMessage<T> : ILogMessage<T>
    {
        public Type Source { get; set; }

        public string Location { get; set; }

        public string Message { get; set; }

        public DateTime DateTime { get; set; }
        public LogMessage(string message)
        {
            Source = typeof(T);
            Location = new StackTrace().GetFrame(1).GetMethod().Name;
            Message = message;
            DateTime = DateTime.Now;
        }
    }
}